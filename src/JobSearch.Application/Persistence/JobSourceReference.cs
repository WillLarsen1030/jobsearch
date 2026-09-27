namespace JobSearch.Application.Persistence;

public sealed record JobSourceReference(
    string Source,
    string Board,
    string FeedKey,
    string SourceJobId,
    Uri Url,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc);
