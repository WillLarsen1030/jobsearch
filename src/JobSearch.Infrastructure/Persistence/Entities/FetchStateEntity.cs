namespace JobSearch.Infrastructure.Persistence.Entities;

internal sealed class FetchStateEntity
{
    public string Source { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string Board { get; set; } = string.Empty;
    public DateTimeOffset? LastSuccessfulFetchUtc { get; set; }
    public DateTimeOffset? LastFailedFetchUtc { get; set; }
    public string LastFailure { get; set; } = string.Empty;
}
