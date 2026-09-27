using JobSearch.Application.Deduplication;
using JobSearch.Application.Matching;
using JobSearch.Application.Persistence;
using JobSearch.Application.Sources;
using Microsoft.Extensions.Logging;

namespace JobSearch.Application.Ingestion;

public sealed class JobIngestionService(
    IJobRepository repository,
    IJobDeduplicator deduplicator,
    IJobScorer scorer,
    TimeProvider timeProvider,
    ILogger<JobIngestionService> logger)
{
    public async Task<int> RescoreStoredJobsAsync(
        JobSearchPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        var jobs = await repository.GetAllAsync(cancellationToken);
        foreach (var job in jobs)
        {
            await repository.UpdateScoreAsync(job.Posting.Id, scorer.Score(job.Posting, preferences), cancellationToken);
        }

        logger.LogInformation("Rescored {JobCount} stored jobs", jobs.Count);
        return jobs.Count;
    }

    public async Task<IngestionSummary> FetchAsync(
        IJobSource source,
        JobSourceRequest request,
        JobSearchPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        await repository.InitializeAsync(cancellationToken);
        var candidates = (await repository.GetAllAsync(cancellationToken)).ToList();
        var retrieved = 0;
        var normalized = 0;
        var duplicates = 0;
        var newJobs = 0;
        var updatedJobs = 0;
        logger.LogInformation("Querying {Source} board {Board}", source.Name, source.Board);
        var result = await source.FetchAsync(request, cancellationToken);
        retrieved = result.RetrievedCount;
        normalized = result.Jobs.Count;
        logger.LogInformation(
            "{Source} board {Board} retrieved {RetrievedCount} records and normalized {NormalizedCount} jobs",
            source.Name,
            source.Board,
            result.RetrievedCount,
            result.Jobs.Count);

        foreach (var posting in result.Jobs)
        {
            var duplicate = deduplicator.FindDuplicate(posting, candidates);
            if (duplicate is not null)
            {
                duplicates++;
                logger.LogDebug(
                    "Duplicate detected for {Company} / {Title}: {Reason}",
                    posting.Company,
                    posting.Title,
                    duplicate.Reason);
            }

            var score = scorer.Score(posting, preferences);
            var upsert = await repository.UpsertAsync(
                posting,
                score,
                duplicate?.JobId,
                timeProvider.GetUtcNow(),
                cancellationToken);

            if (upsert.IsNewJob)
            {
                newJobs++;
                candidates.Add(upsert.Job);
            }
            else
            {
                updatedJobs++;
                var index = candidates.FindIndex(candidate => candidate.Posting.Id == upsert.Job.Posting.Id);
                if (index >= 0)
                {
                    candidates[index] = upsert.Job;
                }
            }
        }

        logger.LogInformation(
            "{Source} board {Board} complete: {NewJobs} new, {UpdatedJobs} updated, {Duplicates} duplicates",
            source.Name,
            source.Board,
            newJobs,
            updatedJobs,
            duplicates);

        return new IngestionSummary(retrieved, normalized, duplicates, newJobs, updatedJobs, 0);
    }
}
