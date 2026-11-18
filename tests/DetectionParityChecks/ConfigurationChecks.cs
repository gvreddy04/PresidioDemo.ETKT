using PresidioDemo.Core.Models;
using PresidioDemo.Core.Protection;

namespace PresidioDemo.DetectionParity;

/// <summary>
/// Checks the shipped recognizer file (WR-014): it loads, declares the ETKT feed's custom recognizers, and every entity type
/// of the frozen baseline is covered by a custom recognizer or by a Presidio predefined entity.
/// </summary>
public static class ConfigurationChecks
{

    #region Fields and Constants

    /// <summary>
    /// The entity types Presidio's predefined recognizers detect in English (Presidio 2.2.364), which run unless switched off.
    /// </summary>
    public static readonly IReadOnlySet<string> PredefinedEntities = new HashSet<string>(StringComparer.Ordinal)
    {
        "CREDIT_CARD", "CRYPTO", "DATE_TIME", "EMAIL_ADDRESS", "IBAN_CODE", "IP_ADDRESS", "LOCATION", "MAC_ADDRESS", "MEDICAL_LICENSE",
        "NRP", "PERSON", "PHONE_NUMBER", "UK_NHS", "URL", "US_BANK_NUMBER", "US_DRIVER_LICENSE", "US_ITIN", "US_PASSPORT", "US_SSN"
    };

    #endregion

    #region Methods

    /// <summary>
    /// Runs the checks.
    /// </summary>
    /// <param name="root">The repository root.</param>
    /// <returns>The number of checks that passed.</returns>
    public static int Run(string root)
    {
        string directory = Path.Combine(root, "src", "PresidioDemo.Core", "Configuration", "recognizers");
        var files = RecognizerFileReader.ReadAll(directory);
        int passed = 0;
        Check(files.Count == FeedCatalog.All.Count && files.ContainsKey(FeedType.ETKT), "the ETKT recognizer file loads", ref passed);
        var etkt = files[FeedType.ETKT];
        Check(etkt.Recognizers.Any(r => r.Entity == "TICKET_REFERENCE") && etkt.Recognizers.Any(r => r.Entity == "UNCLASSIFIED_IDENTIFIER")
            && etkt.Recognizers.Any(r => r.Entity == "PERSON"),
            "ticket-number, status-prefix and passenger-name recognizers are declared", ref passed);
        Check(etkt.Anonymizers.ContainsKey(FeedAnonymizer.DefaultEntity), "the shipped file sets a DEFAULT anonymizer", ref passed);
        var baseline = ParityBaseline.Load(root);
        var uncovered = UncoveredEntities(baseline, files);
        Check(uncovered.Count == 0, "every baseline entity has a custom recognizer or a Presidio predefined entity"
            + (uncovered.Count == 0 ? "" : ": missing " + string.Join(", ", uncovered)), ref passed);
        var withoutTicketNumber = new Dictionary<FeedType, RecognizerFile>(files)
        {
            [FeedType.ETKT] = etkt with { Recognizers = etkt.Recognizers.Where(r => r.Entity != "TICKET_REFERENCE").ToArray() },
        };
        Check(UncoveredEntities(baseline, withoutTicketNumber).Contains("ETKT TICKET_REFERENCE"),
            "the coverage check reports a baseline entity removed from its feed's file", ref passed);
        return passed;
    }

    /// <summary>
    /// Returns the baseline entity types, per feed, that neither a custom recognizer nor a Presidio predefined entity covers.
    /// </summary>
    /// <param name="baseline">The frozen baseline.</param>
    /// <param name="files">The recognizer files by feed.</param>
    /// <returns>"FEED ENTITY" entries in ordinal order.</returns>
    public static IReadOnlyList<string> UncoveredEntities(ParityBaselineDocument baseline, IReadOnlyDictionary<FeedType, RecognizerFile> files) =>
        baseline.Protected
            .Select(e => (Type: FeedCatalog.All.Single(f => f.FolderName == e.Feed).Type, Name: e.Feed, e.Entity))
            .Distinct()
            .Where(e => !files[e.Type].Recognizers.Any(r => r.Entity == e.Entity) && !PredefinedEntities.Contains(e.Entity))
            .Select(e => e.Name + " " + e.Entity)
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Throws when a condition fails.
    /// </summary>
    private static void Check(bool condition, string label, ref int passed)
    {
        if (!condition)
        {
            throw new Exception(label);
        }
        Console.WriteLine("PASS " + label);
        passed++;
    }

    #endregion
}
