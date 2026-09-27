namespace JobSearch.Infrastructure.Persistence.Entities;

internal sealed class JobSourceEntity
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public JobEntity Job { get; set; } = null!;
    public string Source { get; set; } = string.Empty;
    public string Board { get; set; } = string.Empty;
    public string FeedKey { get; set; } = string.Empty;
    public string SourceKey { get; set; } = string.Empty;
    public string SourceJobId { get; set; } = string.Empty;
    public string OriginalUrl { get; set; } = string.Empty;
    public DateTimeOffset FirstSeenUtc { get; set; }
    public long FirstSeenUnixSeconds { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
}
