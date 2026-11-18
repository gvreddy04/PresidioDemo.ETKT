using System.Text;
using System.Text.Json;
using PresidioDemo.Core.Decoder;
using PresidioDemo.Core.Models;
using PresidioDemo.Core.Pipeline;

#region Execution

// Generic XDR codec checks (V-03). Output is value-free: offsets, lengths and counts only.
string project = AppContext.BaseDirectory;
while (!File.Exists(Path.Combine(project, "PresidioDemo.slnx")))
{
    project = Directory.GetParent(project)?.FullName ?? throw new Exception("Project root not found.");
}
// Candidate counts measured against the removed per-feed schemas in T-003 (the scanner found exactly the schema strings
// of four or more characters), and the frozen value-free parity baseline.
var expectedCandidates = new Dictionary<string, int>
{
    ["ETKT.txt"] = 7, ["ETKT_02.txt"] = 7, ["ETKT_03.txt"] = 17, ["ETKT_04.txt"] = 7, ["ETKT_05.txt"] = 9, ["ETKT_06.txt"] = 7
};
using var baseline = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, "tests", "fixtures", "parity-baseline.json")));
var locations = baseline.RootElement.GetProperty("protected").EnumerateArray().Concat(baseline.RootElement.GetProperty("pnrs").EnumerateArray())
    .Where(e => e.GetProperty("feed").GetString() == "ETKT")
    .Select(e => (Sample: e.GetProperty("sample").GetString()!, Offset: int.Parse(e.GetProperty("location").GetString()!), Length: e.GetProperty("length").GetInt32()))
    .ToArray();
var samples = Directory.EnumerateFiles(Path.Combine(project, "docs", "sample-docs"), "*.txt", SearchOption.AllDirectories)
    .Order(StringComparer.Ordinal)
    .Select(path => (Path: path, Feed: FeedFileRouting.FromPath(path)))
    .ToArray();
Check(samples.Length == 6, "6 ETKT samples expected, found " + samples.Length);

int compared = 0;
foreach (var (path, feed) in samples)
{
    string name = Path.GetFileName(path);
    var message = new FeedMessage(new FeedDelivery(feed, Guid.NewGuid(), name, path), File.ReadAllBytes(path));
    var codec = FeedCodecRegistry.Create(feed);
    var parsed = codec.Parse(message);
    var candidates = XdrFeedDecoder.Candidates(parsed);

    // A-002: the scanner still finds the strings the removed schemas held, and every baseline value of four or more
    // characters lies inside one candidate (shorter ones are raw words, WR-015).
    Check(candidates.Count == expectedCandidates[name], $"{name}: {candidates.Count} candidates, expected {expectedCandidates[name]}");
    foreach (var location in locations.Where(l => l.Sample == name))
    {
        bool inside = candidates.Any(c => location.Offset >= c.Offset && location.Offset + location.Length <= c.Offset + c.Length);
        var containing = candidates.FirstOrDefault(c => location.Offset >= c.Offset && location.Offset < c.Offset + c.Length);
        Check(inside || containing is null, $"{name}: a baseline value at offset {location.Offset} crosses a candidate boundary");
        Check(inside || location.Length < XdrTextScanner.MinimumLength || IsShortString(envelopeOf(message), location.Offset, location.Length),
            $"{name}: baseline value at offset {location.Offset} is not inside a candidate");
    }
    compared += candidates.Count;

    // Unchanged values rebuild the original bytes exactly.
    var unchanged = parsed.Fields.ToDictionary(f => f.Id, f => f.Value);
    byte[] same = codec.Serialize(parsed, unchanged);
    Check(same.AsSpan().SequenceEqual(message.Content.Span), name + ": unmasked round trip changed bytes");
    codec.Validate(parsed, same);

    // Full-width masks keep framing, length and the string layout.
    var masked = parsed.Fields.ToDictionary(f => f.Id, f => new string('*', f.Value.Length));
    byte[] protectedContent = codec.Serialize(parsed, masked);
    codec.Validate(parsed, protectedContent);
    Check(protectedContent.Length == message.Content.Length, name + ": masked message changed length");

    // A changed width is refused.
    var first = parsed.Fields[0];
    Reject(() => codec.Serialize(parsed, unchanged.ToDictionary(p => p.Key, p => p.Key == first.Id ? p.Value + "X" : p.Value)), name + ": width change");

    // A changed byte outside the strings is refused.
    var envelope = FeedPayloadReader.Read(message);
    byte[] tamperedPayload = envelope.Payload.ToArray();
    int outside = Enumerable.Range(0, tamperedPayload.Length).First(i => !candidates.Any(c => i >= c.Offset && i < c.Offset + c.Length));
    tamperedPayload[outside] ^= 0x01;
    Reject(() => codec.Validate(parsed, envelope.Serialize(tamperedPayload)), name + ": changed raw word");

    Console.WriteLine($"PASS {name,-14} {candidates.Count,3} candidates | baseline locations inside candidates or short raw words");
}

// Unaligned payloads are framing failures.
Reject(() => XdrTextScanner.Scan(new byte[] { 0, 0, 0 }), "unaligned payload");
// Short spans are never candidates.
Check(XdrTextScanner.Scan(new byte[] { 0, 0, 0, 3, 0x31, 0x32, 0x41, 0 }).Count == 0, "3-character span must stay raw words");
Check(XdrTextScanner.Scan(new byte[] { 0, 0, 0, 4, 0x44, 0x45, 0x4E, 0x31 }).Count == 1, "4-character string must be a candidate");
// Non-printable text or non-zero padding is not a string.
Check(XdrTextScanner.Scan(new byte[] { 0, 0, 0, 5, 0x41, 0x42, 0x43, 0x44, 0x45, 1, 0, 0 }).Count == 0, "non-zero padding must not be a string");
// Text in another encoding cannot be checked for personal data, so it fails closed (WR-016), as the removed schemas did.
Reject(() => XdrTextScanner.Scan(new byte[] { 0, 0, 0, 4, 0x41, 0x09, 0x43, 0x44 }), "a tab inside a counted string");
Reject(() => XdrTextScanner.Scan(new byte[] { 0, 0, 0, 6, 0x4D, 0xDC, 0x4C, 0x4C, 0x45, 0x52, 0, 0 }), "Latin-1 text (MÜLLER)");
Reject(() => XdrTextScanner.Scan(new byte[] { 0, 0, 0, 5, 0xE2, 0xD4, 0xC9, 0xE3, 0xC8, 0, 0, 0 }), "EBCDIC text");
// Integers almost always hold NUL bytes, so a length-like word followed by an integer stays raw words.
Check(XdrTextScanner.Scan(new byte[] { 0, 0, 0, 4, 0xFF, 0xFF, 0xFF, 0x00 }).Count == 0, "an integer with a NUL byte stays raw words");
Check(XdrTextScanner.Scan(new byte[] { 0, 0, 0, 4, 0, 0, 0, 0 }).Count == 0, "NUL bytes stay raw words");
// A count followed by integer bytes without NUL (R-013) is not text in another encoding.
Check(XdrTextScanner.Scan(new byte[] { 0, 0, 0, 4, 0x8A, 0x1B, 0x2C, 0x3D }).Count == 0, "a count followed by integer bytes stays raw words");
// WR-015: the words 00000001 31000000 read as a one-character string "1" but are raw words, never a candidate.
Check(XdrTextScanner.Scan(new byte[] { 0, 0, 0, 1, 0x31, 0, 0, 0 }).Count == 0, "a one-character counted string stays raw words");
// Lowercase and mixed-case EBCDIC (code page 500) must fail closed too, not pass as raw words (review R-001). Fictional values.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var ebcdic = Encoding.GetEncoding(500);
foreach (string text in new[] { "miller/jordan", "Jordan Miller", "jordan.miller@example.com", "row 23c window" })
{
    Reject(() => XdrTextScanner.Scan(Counted(ebcdic.GetBytes(text))), "EBCDIC text: " + text.Length + " characters");
}
// A length word announcing printable text that is too long or runs past the payload fails closed (review R-002).
Reject(() => XdrTextScanner.Scan([0, 1, 0, 0, .. Encoding.ASCII.GetBytes("MILLER/JORDAN MR 0000")]), "a counted string longer than 65535 bytes");
Reject(() => XdrTextScanner.Scan([0, 0, 0, 100, .. Encoding.ASCII.GetBytes("MILLER/JORDAN MR")]), "a counted string that runs past the payload");
// A large integer followed by a real counted string is not oversized text.
Check(XdrTextScanner.Scan([0, 1, 0, 0, 0, 0, 0, 4, 0x44, 0x45, 0x4E, 0x31]).Count == 1, "a large integer before a real string stays raw words");
Check(XdrTextScanner.Scan([0, 0, 0, 100, 0, 0, 0, 7]).Count == 0, "a length past the payload without text stays raw words");

Console.WriteLine($"PASS Generic XDR codec checks complete | {samples.Length} samples | {compared} candidates");

static byte[] envelopeOf(FeedMessage message) => FeedPayloadReader.Read(message).Payload;

// An XDR counted string: big-endian length, the bytes, zero padding to a word boundary.
static byte[] Counted(byte[] text) =>
    [(byte)(text.Length >> 24), (byte)(text.Length >> 16), (byte)(text.Length >> 8), (byte)text.Length, .. text, .. new byte[XdrTextScanner.Padded(text.Length) - text.Length]];

// A short counted string: its length word sits just before the value and equals the value length (1-3 characters).
static bool IsShortString(byte[] payload, int offset, int length) =>
    offset >= 4 && System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(offset - 4, 4)) == (uint)length;

static void Check(bool condition, string label)
{
    if (!condition)
    {
        throw new Exception(label);
    }
}

static void Reject(Action action, string label)
{
    try
    {
        action();
    }
    catch (InvalidDataException)
    {
        return;
    }
    throw new Exception("Expected rejection: " + label);
}

#endregion
