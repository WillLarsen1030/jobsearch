using JobSearch.Application.Sources;
using JobSearch.Infrastructure.Configuration;
using JobSearch.Infrastructure.Sources.Greenhouse;
using JobSearch.Infrastructure.Sources.Jobicy;
using JobSearch.Infrastructure.Sources.Lever;
using Microsoft.Extensions.Logging;

namespace JobSearch.Infrastructure.Sources;

public static class JobSourceFactory
{
    public static IReadOnlyList<IJobSource> Create(
        JobSearchAppSettings settings,
        Func<string, HttpClient> httpClientFactory,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
    {
        var sources = new List<IJobSource>();
        if (settings.JobSources.Jobicy.Enabled)
        {
            sources.Add(new JobicyJobSource(
                httpClientFactory("Jobicy"),
                settings.JobSources.Jobicy,
                timeProvider,
                loggerFactory.CreateLogger<JobicyJobSource>()));
        }

        if (settings.JobSources.Greenhouse.Enabled)
        {
            sources.AddRange(settings.JobSources.Greenhouse.Boards
                .Where(board => board.Enabled)
                .Select(board => new GreenhouseJobSource(
                    httpClientFactory("Greenhouse"),
                    settings.JobSources.Greenhouse,
                    board,
                    timeProvider)));
        }

        if (settings.JobSources.Lever.Enabled)
        {
            sources.AddRange(settings.JobSources.Lever.Boards
                .Where(board => board.Enabled)
                .Select(board => new LeverJobSource(
                    httpClientFactory("Lever"),
                    settings.JobSources.Lever,
                    board,
                    timeProvider)));
        }

        return sources;
    }
}
