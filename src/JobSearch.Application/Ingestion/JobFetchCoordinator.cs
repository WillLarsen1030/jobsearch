using JobSearch.Application.Matching;
using JobSearch.Application.Persistence;
using JobSearch.Application.Sources;
using Microsoft.Extensions.Logging;

namespace JobSearch.Application.Ingestion;

public sealed class JobFetchCoordinator(
    JobSourceCatalog sourceCatalog,
    JobIngestionService ingestionService,
    IFetchStateStore fetchStateStore,
    TimeProvider timeProvider,
    ILogger<JobFetchCoordinator> logger)
{
    public async Task<FetchRunResult> FetchAsync(
        JobSourceRequest request,
        JobSearchPreferences preferences,
        bool enforceCooldown = true,
        CancellationToken cancellationToken = default)
    {
        await fetchStateStore.InitializeAsync(cancellationToken);
        var results = new List<SourceFetchResult>();
        foreach (var source in sourceCatalog.Sources)
        {
            var now = timeProvider.GetUtcNow();
            var lastFetch = await fetchStateStore.GetLastSuccessfulFetchAsync(source.Key, cancellationToken);
            var retryAt = lastFetch?.Add(source.Cooldown);
            if (enforceCooldown && retryAt > now)
            {
                logger.LogInformation("Skipping {SourceKey} until {RetryAtUtc} due to cooldown", source.Key, retryAt);
                results.Add(new SourceFetchResult(source.Key, source.Name, source.Board, SourceFetchStatus.Cooldown, RetryAtUtc: retryAt));
                continue;
            }

            try
            {
                var summary = await ingestionService.FetchAsync(source, request, preferences, cancellationToken);
                await fetchStateStore.RecordSuccessfulFetchAsync(source.Key, source.Name, source.Board, now, cancellationToken);
                results.Add(new SourceFetchResult(
                    source.Key,
                    source.Name,
                    source.Board,
                    SourceFetchStatus.Succeeded,
                    summary.Retrieved,
                    summary.Normalized,
                    summary.Duplicates,
                    summary.NewJobs,
                    summary.UpdatedJobs));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Job source {SourceKey} failed; continuing with remaining sources", source.Key);
                await fetchStateStore.RecordFailedFetchAsync(source.Key, source.Name, source.Board, now, exception.Message, cancellationToken);
                results.Add(new SourceFetchResult(source.Key, source.Name, source.Board, SourceFetchStatus.Failed, Error: exception.Message));
            }
        }

        await ingestionService.RescoreStoredJobsAsync(preferences, cancellationToken);

        return new FetchRunResult(results);
    }
}
