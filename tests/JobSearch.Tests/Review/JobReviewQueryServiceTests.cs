using JobSearch.Application.Persistence;
using JobSearch.Application.Review;
using JobSearch.Domain.JobPostings;

namespace JobSearch.Tests.Review;

public sealed class JobReviewQueryServiceTests
{
    private readonly JobReviewQueryService _service = new();

    [Fact]
    public void FilterAndSort_AppliesCombinedFilters()
    {
        var now = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        var jobs = new[]
        {
            CreateJob("Azure API Engineer", "Beta", 82, now, ApplicationStatus.Interested, EmploymentType.Contract, 75m),
            CreateJob("React Developer", "Alpha", 90, now, ApplicationStatus.Unreviewed, EmploymentType.FullTime, 80m),
            CreateJob("Legacy .NET Developer", "Gamma", 65, now.AddDays(-20), ApplicationStatus.Interested, EmploymentType.Contract, null)
        };

        var result = _service.FilterAndSort(
            jobs,
            new JobListFilter(75, ApplicationStatus.Interested, WorkLocationType.Remote, EmploymentType.Contract, true, "Azure", now.AddDays(-7)),
            JobSort.Score);

        Assert.Equal("Azure API Engineer", Assert.Single(result).Posting.Title);
    }

    [Fact]
    public void FilterAndSort_SortsByCompensationThenScore()
    {
        var now = DateTimeOffset.UtcNow;
        var jobs = new[]
        {
            CreateJob("One", "Alpha", 90, now, ApplicationStatus.Unreviewed, EmploymentType.Contract, 60m),
            CreateJob("Two", "Beta", 70, now, ApplicationStatus.Unreviewed, EmploymentType.Contract, 90m)
        };

        var result = _service.FilterAndSort(jobs, new JobListFilter(), JobSort.Compensation);

        Assert.Equal("Two", result[0].Posting.Title);
    }

    internal static StoredJob CreateJob(
        string title,
        string company,
        int score,
        DateTimeOffset firstSeen,
        ApplicationStatus status,
        EmploymentType employmentType,
        decimal? maximumRate) => new(
            new JobPosting
            {
                Source = "Test",
                SourceJobId = Guid.NewGuid().ToString(),
                Title = title,
                Company = company,
                Url = new Uri("https://example.com/jobs/" + Guid.NewGuid()),
                Description = $"Remote {title} using Azure.",
                CountryCode = "US",
                WorkLocationType = WorkLocationType.Remote,
                EmploymentType = employmentType,
                MaximumHourlyRate = maximumRate,
                Skills = ["Azure"],
                Status = status
            },
            score,
            [],
            firstSeen,
            firstSeen,
            []);
}
