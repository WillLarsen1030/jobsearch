namespace JobSearch.Application.Ingestion;

public sealed record SourceFetchResult(
    string SourceKey,
    string Source,
    string Board,
    SourceFetchStatus Status,
    int Retrieved = 0,
    int Normalized = 0,
    int Duplicates = 0,
    int NewJobs = 0,
    int UpdatedJobs = 0,
    DateTimeOffset? RetryAtUtc = null,
    string Error = "");
