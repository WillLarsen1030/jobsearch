using JobSearch.Application.Persistence;
using JobSearch.Domain.JobPostings;

namespace JobSearch.Application.Review;

public sealed class DashboardSummaryService
{
    public DashboardSummary Calculate(IEnumerable<StoredJob> jobs, DateTimeOffset nowUtc)
    {
        var values = jobs.ToArray();
        return new DashboardSummary(
            values.Count(IsActive),
            values.Count(job => job.Posting.Status == ApplicationStatus.Unreviewed),
            values.Count(job => job.Score >= 75),
            values.Count(job => job.Posting.Status == ApplicationStatus.Interested),
            values.Count(job => job.Posting.Status == ApplicationStatus.Applied),
            values.Count(job => job.Posting.Status == ApplicationStatus.Interview),
            values.Count(job => job.Posting.Status is ApplicationStatus.Rejected or ApplicationStatus.Skipped),
            values.Count(job => job.FirstSeenUtc >= nowUtc.AddHours(-24)),
            values.Count(job => job.FirstSeenUtc >= nowUtc.AddDays(-7)));
    }

    private static bool IsActive(StoredJob job) => job.Posting.Status is not
        (ApplicationStatus.Rejected or ApplicationStatus.Skipped or ApplicationStatus.Expired);
}
