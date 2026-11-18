using PresidioDemo.Core.Models;

namespace PresidioDemo.Core.Logging;

/// <summary>
/// Formats transaction logs for the ETKT feed.
/// </summary>
public sealed class EtktFeedLogger : FeedLogger
{

    #region Properties, Indexers

    /// <inheritdoc />
    public override FeedType Feed => FeedType.ETKT;

    #endregion
}
