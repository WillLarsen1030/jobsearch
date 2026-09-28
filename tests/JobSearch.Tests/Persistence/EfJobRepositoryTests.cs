using JobSearch.Application.Matching;
using JobSearch.Domain.JobPostings;
using JobSearch.Infrastructure.Persistence;
using JobSearch.Application.Review;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobSearch.Tests.Persistence;

public sealed class EfJobRepositoryTests
{
    [Fact]
    public async Task UpsertAsync_PersistsAndRetrievesJob()
    {
        await using var fixture = await RepositoryFixture.CreateAsync();
        var posting = CreatePosting();
        var score = new JobScore(82, [new ScoreReason("Skills", 20, "Matched skills.")]);

        var result = await fixture.Repository.UpsertAsync(posting, score, null, fixture.FirstSeen);
        var stored = Assert.Single(await fixture.Repository.GetAllAsync());

        Assert.True(result.IsNewJob);
        Assert.Equal(82, stored.Score);
        Assert.Equal(fixture.FirstSeen, stored.FirstSeenUtc);
        Assert.Equal(fixture.FirstSeen, stored.LastSeenUtc);
        Assert.Single(stored.Sources);
    }

    [Fact]
    public async Task UpsertAsync_UpdatesPostingButPreservesTrackingState()
    {
        await using var fixture = await RepositoryFixture.CreateAsync();
        var original = CreatePosting() with
        {
            Status = ApplicationStatus.Applied,
            DateAppliedUtc = fixture.FirstSeen
        };
        var inserted = await fixture.Repository.UpsertAsync(original, new JobScore(70, []), null, fixture.FirstSeen);
        var updatedPosting = original with
        {
            Id = Guid.NewGuid(),
            Title = "Senior .NET Engineer - Updated",
            Status = ApplicationStatus.Unreviewed,
            DateAppliedUtc = null
        };

        await fixture.Repository.UpsertAsync(
            updatedPosting,
            new JobScore(88, []),
            inserted.Job.Posting.Id,
            fixture.FirstSeen.AddHours(2));
        var stored = Assert.Single(await fixture.Repository.GetAllAsync());

        Assert.Equal("Senior .NET Engineer - Updated", stored.Posting.Title);
        Assert.Equal(ApplicationStatus.Applied, stored.Posting.Status);
        Assert.Equal(fixture.FirstSeen, stored.Posting.DateAppliedUtc);
        Assert.Equal(fixture.FirstSeen.AddHours(2), stored.LastSeenUtc);
        Assert.Equal(88, stored.Score);
    }

    [Fact]
    public async Task UpsertAsync_DuplicateFromAnotherSource_RetainsBothSources()
    {
        await using var fixture = await RepositoryFixture.CreateAsync();
        var first = await fixture.Repository.UpsertAsync(
            CreatePosting(),
            new JobScore(75, []),
            null,
            fixture.FirstSeen);
        var secondSource = CreatePosting() with
        {
            Id = Guid.NewGuid(),
            Source = "Lever",
            SourceJobId = "lever-456",
            Url = new Uri("https://jobs.lever.co/acme/456")
        };

        var result = await fixture.Repository.UpsertAsync(
            secondSource,
            new JobScore(80, []),
            first.Job.Posting.Id,
            fixture.FirstSeen.AddMinutes(5));
        var stored = Assert.Single(await fixture.Repository.GetAllAsync());

        Assert.False(result.IsNewJob);
        Assert.True(result.AddedSource);
        Assert.Equal(2, stored.Sources.Count);
        Assert.Contains(stored.Sources, source => source.Source == "Jobicy");
        Assert.Contains(stored.Sources, source => source.Source == "Lever");
    }

    [Fact]
    public async Task ChangeStatusAsync_CreatesHistoryAndAppliedTimestamp()
    {
        await using var fixture = await RepositoryFixture.CreateAsync();
        var inserted = await fixture.Repository.UpsertAsync(CreatePosting(), new JobScore(80, []), null, fixture.FirstSeen);
        var changedAt = fixture.FirstSeen.AddHours(1);

        var updated = await fixture.Repository.ChangeStatusAsync(
            inserted.Job.Posting.Id,
            ApplicationStatus.Applied,
            "Submitted through company site.",
            changedAt);

        var change = Assert.Single(updated.History);
        Assert.Equal(ApplicationStatus.Unreviewed, change.OldStatus);
        Assert.Equal(ApplicationStatus.Applied, change.NewStatus);
        Assert.Equal("Submitted through company site.", change.Note);
        Assert.Equal(changedAt, updated.Posting.DateAppliedUtc);
    }

    [Fact]
    public async Task UpdateNotesAsync_PersistsNotesIndependentlyOfHistory()
    {
        await using var fixture = await RepositoryFixture.CreateAsync();
        var inserted = await fixture.Repository.UpsertAsync(CreatePosting(), new JobScore(80, []), null, fixture.FirstSeen);

        var updated = await fixture.Repository.UpdateNotesAsync(inserted.Job.Posting.Id, "Ask about weekly meeting load.");

        Assert.Equal("Ask about weekly meeting load.", updated.Notes);
        Assert.Empty(updated.History);
        Assert.Equal(ApplicationStatus.Unreviewed, updated.Posting.Status);
    }

    [Fact]
    public async Task InitializeAsync_AppliesAllMigrations()
    {
        await using var fixture = await RepositoryFixture.CreateAsync();

        var pending = await fixture.Context.Database.GetPendingMigrationsAsync();
        var applied = await fixture.Context.Database.GetAppliedMigrationsAsync();

        Assert.Empty(pending);
        Assert.Equal(5, applied.Count());
    }

    [Fact]
    public async Task SearchAsync_FiltersSortsAndPaginatesInDatabase()
    {
        await using var fixture = await RepositoryFixture.CreateAsync();
        var values = new[]
        {
            ("Senior Azure .NET Engineer", 91, ApplicationStatus.Interested),
            ("Senior ASP.NET Developer", 84, ApplicationStatus.Interested),
            ("React Engineer", 72, ApplicationStatus.Unreviewed)
        };
        for (var index = 0; index < values.Length; index++)
        {
            var value = values[index];
            var posting = CreatePosting() with { Title = value.Item1, Status = value.Item3 };
            posting = posting with
            {
                SourceJobId = $"job-{index}",
                Url = new Uri($"https://jobicy.com/jobs/{index}")
            };
            await fixture.Repository.UpsertAsync(posting, new JobScore(value.Item2, []), null, fixture.FirstSeen);
        }

        var page = await fixture.Repository.SearchAsync(
            new JobListFilter(80, ApplicationStatus.Interested, Keyword: ".NET"),
            JobSort.Score,
            1,
            1);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal("Senior Azure .NET Engineer", Assert.Single(page.Items).Posting.Title);
    }

    [Fact]
    public async Task GetSourceAnalyticsAsync_ReturnsCountsScoresAndFetchHealth()
    {
        await using var fixture = await RepositoryFixture.CreateAsync();
        await fixture.Repository.UpsertAsync(CreatePosting(), new JobScore(80, []), null, fixture.FirstSeen);
        await fixture.Repository.RecordSuccessfulFetchAsync("jobicy", "Jobicy", "Remote Jobs API", fixture.FirstSeen);
        await fixture.Repository.RecordFailedFetchAsync("jobicy", "Jobicy", "Remote Jobs API", fixture.FirstSeen.AddMinutes(5), "rate limited");

        var analytics = Assert.Single(await fixture.Repository.GetSourceAnalyticsAsync(fixture.FirstSeen.AddMinutes(10)));

        Assert.Equal(1, analytics.StoredJobs);
        Assert.Equal(80, analytics.AverageScore);
        Assert.Equal(1, analytics.StrongMatches);
        Assert.Equal(fixture.FirstSeen, analytics.LastSuccessfulFetchUtc);
        Assert.Equal("rate limited", analytics.LastFailure);
    }

    private static JobPosting CreatePosting() => new()
    {
        Source = "Jobicy",
        SourceBoard = "Remote Jobs API",
        SourceFeedKey = "jobicy",
        SourceJobId = "123",
        Title = "Senior .NET Engineer",
        Company = "Acme",
        Url = new Uri("https://jobicy.com/jobs/123"),
        Description = "Remote ASP.NET Core contract role.",
        Location = "USA",
        CountryCode = "US",
        WorkLocationType = WorkLocationType.Remote,
        EmploymentType = EmploymentType.Contract,
        Skills = ["C#", ".NET"]
    };

    private sealed class RepositoryFixture : IAsyncDisposable
    {
        private RepositoryFixture(SqliteConnection connection, JobSearchDbContext context, EfJobRepository repository)
        {
            Connection = connection;
            Context = context;
            Repository = repository;
        }

        private SqliteConnection Connection { get; }
        public JobSearchDbContext Context { get; }
        public EfJobRepository Repository { get; }
        public DateTimeOffset FirstSeen { get; } = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

        public static async Task<RepositoryFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<JobSearchDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new JobSearchDbContext(options);
            var repository = new EfJobRepository(context, NullLogger<EfJobRepository>.Instance);
            await repository.InitializeAsync();
            return new RepositoryFixture(connection, context, repository);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
