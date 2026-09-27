namespace JobSearch.Application.Persistence;

public interface IFetchStateStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<DateTimeOffset?> GetLastSuccessfulFetchAsync(string source, CancellationToken cancellationToken = default);

    Task RecordSuccessfulFetchAsync(
        string sourceKey,
        string source,
        string board,
        DateTimeOffset fetchedAtUtc,
        CancellationToken cancellationToken = default);

    Task RecordFailedFetchAsync(
        string sourceKey,
        string source,
        string board,
        DateTimeOffset failedAtUtc,
        string error,
        CancellationToken cancellationToken = default);
}
