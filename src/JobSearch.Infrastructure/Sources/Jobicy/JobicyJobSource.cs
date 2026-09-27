using System.Net.Http.Json;
using JobSearch.Application.Sources;
using Microsoft.Extensions.Logging;

namespace JobSearch.Infrastructure.Sources.Jobicy;

public sealed class JobicyJobSource(
    HttpClient httpClient,
    JobicyOptions options,
    TimeProvider timeProvider,
    ILogger<JobicyJobSource> logger) : IJobSource
{
    public string Key => "jobicy";

    public string Name => "Jobicy";

    public string Board => "Remote Jobs API";

    public TimeSpan Cooldown => TimeSpan.FromMinutes(Math.Max(1, options.CooldownMinutes));

    public async Task<JobSourceResult> FetchAsync(
        JobSourceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Jobicy source is disabled");
            return new JobSourceResult(0, []);
        }

        var jobsById = new Dictionary<long, JobicyJobDto>();
        var retrieved = 0;
        Exception? lastException = null;

        foreach (var term in request.SearchTerms
            .Where(term => term.Length is >= 3 and <= 50)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var requestUrl = BuildRequestUrl(term, request.MaximumResults);
                using var response = await httpClient.GetAsync(requestUrl, cancellationToken);
                response.EnsureSuccessStatusCode();
                var payload = await response.Content.ReadFromJsonAsync<JobicyResponse>(cancellationToken: cancellationToken)
                    ?? new JobicyResponse();
                retrieved += payload.Jobs.Count;
                foreach (var job in payload.Jobs)
                {
                    jobsById[job.Id] = job;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                lastException = exception;
                logger.LogWarning(exception, "Jobicy query failed for search term {SearchTerm}", term);
            }
        }

        if (jobsById.Count == 0 && lastException is not null)
        {
            throw new HttpRequestException("All Jobicy queries failed.", lastException);
        }

        var seenAt = timeProvider.GetUtcNow();
        var normalized = jobsById.Values
            .Select(job => JobicyNormalizer.Normalize(job, request, seenAt))
            .Where(job => job is not null)
            .Select(job => job!)
            .ToArray();

        return new JobSourceResult(retrieved, normalized);
    }

    private string BuildRequestUrl(string searchTerm, int maximumResults)
    {
        var count = Math.Clamp(maximumResults, 1, 200);
        return $"{options.BaseUrl}?count={count}&geo={Uri.EscapeDataString(options.Geography)}" +
            $"&industry={Uri.EscapeDataString(options.Industry)}&tag={Uri.EscapeDataString(searchTerm)}";
    }
}
