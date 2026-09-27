namespace JobSearch.Application.Review;

public sealed record SourceAnalytics(
    string SourceKey,
    string Source,
    string Board,
    int StoredJobs,
    int DiscoveredLast24Hours,
    int DiscoveredLast7Days,
    double AverageScore,
    int StrongMatches,
    DateTimeOffset? LastSuccessfulFetchUtc,
    DateTimeOffset? LastFailedFetchUtc,
    string LastFailure);
