using JobSearch.Application.Review;
using JobSearch.Domain.JobPostings;

namespace JobSearch.Tests.Review;

public sealed class DashboardSummaryServiceTests
{
    [Fact]
    public void Calculate_ReturnsPipelineAndDateCounts()
    {
        var now = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        var jobs = new[]
        {
            JobReviewQueryServiceTests.CreateJob("One", "A", 80, now.AddHours(-2), ApplicationStatus.Unreviewed, EmploymentType.Contract, 80),
            JobReviewQueryServiceTests.CreateJob("Two", "B", 70, now.AddDays(-2), ApplicationStatus.Interested, EmploymentType.Contract, 70),
            JobReviewQueryServiceTests.CreateJob("Three", "C", 90, now.AddDays(-8), ApplicationStatus.Applied, EmploymentType.FullTime, 90),
            JobReviewQueryServiceTests.CreateJob("Four", "D", 40, now.AddHours(-25), ApplicationStatus.Interview, EmploymentType.Contract, null),
            JobReviewQueryServiceTests.CreateJob("Five", "E", 30, now.AddHours(-25), ApplicationStatus.Skipped, EmploymentType.Contract, null)
        };

        var result = new DashboardSummaryService().Calculate(jobs, now);

        Assert.Equal(4, result.Active);
        Assert.Equal(1, result.Unreviewed);
        Assert.Equal(2, result.StrongMatches);
        Assert.Equal(1, result.Interested);
        Assert.Equal(1, result.Applied);
        Assert.Equal(1, result.Interviews);
        Assert.Equal(1, result.RejectedOrSkipped);
        Assert.Equal(1, result.AddedLast24Hours);
        Assert.Equal(4, result.AddedLast7Days);
    }
}
