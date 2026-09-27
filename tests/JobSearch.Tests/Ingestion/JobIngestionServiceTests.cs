using JobSearch.Application.Deduplication;
using JobSearch.Application.Ingestion;
using JobSearch.Application.Matching;
using JobSearch.Application.Sources;
using JobSearch.Domain.JobPostings;
using JobSearch.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobSearch.Tests.Ingestion;

public sealed class JobIngestionServiceTests
{
    [Fact]
    public async Task FetchAsync_WhenOneSourceFails_ContinuesWithOtherSources()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<JobSearchDbContext>().UseSqlite(connection).Options;
        await using var context = new JobSearchDbContext(options);
        var repository = new EfJobRepository(context, NullLogger<EfJobRepository>.Instance);
        var posting = new JobPosting
        {
            Source = "Working",
            SourceJobId = "1",
            Title = "Senior .NET Contractor",
            Company = "Acme",
            Url = new Uri("https://example.com/jobs/1"),
            CountryCode = "US",
            WorkLocationType = WorkLocationType.Remote,
            EmploymentType = EmploymentType.Contract
        };
        IJobSource[] sources = [new FailingSource(), new SuccessfulSource(posting)];
        var service = new JobIngestionService(
            repository,
            new DefaultJobDeduplicator(),
            new DefaultJobScorer(),
            TimeProvider.System,
            NullLogger<JobIngestionService>.Instance);

        var coordinator = new JobFetchCoordinator(
            new JobSourceCatalog(sources),
            service,
            repository,
            TimeProvider.System,
            NullLogger<JobFetchCoordinator>.Instance);
        var result = await coordinator.FetchAsync(
            new JobSourceRequest([".NET"], [".NET"], "US", 10),
            new JobSearchPreferences());

        Assert.Equal(1, result.FailedSources);
        Assert.Equal(1, result.NewJobs);
        Assert.Single(await repository.GetAllAsync());
    }

    [Fact]
    public async Task FetchCoordinator_BlocksSecondFetchDuringCooldown()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<JobSearchDbContext>().UseSqlite(connection).Options;
        await using var context = new JobSearchDbContext(options);
        var repository = new EfJobRepository(context, NullLogger<EfJobRepository>.Instance);
        var source = new CountingSource();
        var ingestion = new JobIngestionService(
            repository,
            new DefaultJobDeduplicator(),
            new DefaultJobScorer(),
            TimeProvider.System,
            NullLogger<JobIngestionService>.Instance);
        var coordinator = new JobFetchCoordinator(
            new JobSourceCatalog([source]),
            ingestion,
            repository,
            TimeProvider.System,
            NullLogger<JobFetchCoordinator>.Instance);
        var request = new JobSourceRequest([".NET"], [".NET"], "US", 10);

        var first = await coordinator.FetchAsync(request, new JobSearchPreferences());
        var second = await coordinator.FetchAsync(request, new JobSearchPreferences());

        Assert.Equal(SourceFetchStatus.Succeeded, Assert.Single(first.Sources).Status);
        var skipped = Assert.Single(second.Sources);
        Assert.Equal(SourceFetchStatus.Cooldown, skipped.Status);
        Assert.NotNull(skipped.RetryAtUtc);
        Assert.Equal(1, source.CallCount);
    }

    [Fact]
    public async Task FetchCoordinator_CooldownForOneBoard_DoesNotSkipAnotherBoard()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<JobSearchDbContext>().UseSqlite(connection).Options;
        await using var context = new JobSearchDbContext(options);
        var repository = new EfJobRepository(context, NullLogger<EfJobRepository>.Instance);
        await repository.InitializeAsync();
        var cooling = new CountingSource("greenhouse:one");
        var available = new CountingSource("greenhouse:two");
        await repository.RecordSuccessfulFetchAsync(cooling.Key, cooling.Name, cooling.Board, DateTimeOffset.UtcNow);
        var ingestion = new JobIngestionService(
            repository,
            new DefaultJobDeduplicator(),
            new DefaultJobScorer(),
            TimeProvider.System,
            NullLogger<JobIngestionService>.Instance);
        var coordinator = new JobFetchCoordinator(
            new JobSourceCatalog([cooling, available]),
            ingestion,
            repository,
            TimeProvider.System,
            NullLogger<JobFetchCoordinator>.Instance);

        var result = await coordinator.FetchAsync(
            new JobSourceRequest([".NET"], [".NET"], "US", 10),
            new JobSearchPreferences());

        Assert.Equal(SourceFetchStatus.Cooldown, result.Sources[0].Status);
        Assert.Equal(SourceFetchStatus.Succeeded, result.Sources[1].Status);
        Assert.Equal(0, cooling.CallCount);
        Assert.Equal(1, available.CallCount);
    }

    private sealed class FailingSource : IJobSource
    {
        public string Name => "Failing";

        public Task<JobSourceResult> FetchAsync(JobSourceRequest request, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("Expected fixture failure.");
    }

    private sealed class SuccessfulSource(JobPosting posting) : IJobSource
    {
        public string Name => "Working";

        public Task<JobSourceResult> FetchAsync(JobSourceRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new JobSourceResult(1, [posting]));
    }

    private sealed class CountingSource(string key = "counting") : IJobSource
    {
        public string Key => key;
        public string Name => "Counting";
        public string Board => key;
        public int CallCount { get; private set; }

        public Task<JobSourceResult> FetchAsync(JobSourceRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new JobSourceResult(0, []));
        }
    }
}
