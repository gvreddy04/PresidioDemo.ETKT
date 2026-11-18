using PresidioDemo.Core.Models;

namespace PresidioDemo.Core.Decoder;

/// <summary>
/// Decodes, rebuilds and validates ETKT messages; the shared logic is in <see cref="XdrFeedDecoder"/>.
/// </summary>
public sealed class EtktDecoder() : XdrFeedDecoder(FeedType.ETKT)
{
}
