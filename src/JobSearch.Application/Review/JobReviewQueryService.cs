using JobSearch.Application.Persistence;

namespace JobSearch.Application.Review;

public sealed class JobReviewQueryService
{
    public IReadOnlyList<StoredJob> FilterAndSort(
        IEnumerable<StoredJob> jobs,
        JobListFilter filter,
        JobSort sort)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(filter);

        var query = jobs.Where(job =>
            job.Score >= filter.MinimumScore &&
            (filter.Status is null || job.Posting.Status == filter.Status) &&
            (filter.WorkLocation is null || job.Posting.WorkLocationType == filter.WorkLocation) &&
            (filter.EmploymentType is null || job.Posting.EmploymentType == filter.EmploymentType) &&
            (filter.HasCompensation is null || HasCompensation(job) == filter.HasCompensation) &&
            (filter.DiscoveredAfterUtc is null || job.FirstSeenUtc >= filter.DiscoveredAfterUtc) &&
            MatchesKeyword(job, filter.Keyword));

        return sort switch
        {
            JobSort.Newest => query.OrderByDescending(job => job.FirstSeenUtc).ThenByDescending(job => job.Score).ToArray(),
            JobSort.Compensation => query.OrderByDescending(MaximumHourlyRate).ThenByDescending(job => job.Score).ToArray(),
            JobSort.Company => query.OrderBy(job => job.Posting.Company, StringComparer.OrdinalIgnoreCase).ThenByDescending(job => job.Score).ToArray(),
            JobSort.Title => query.OrderBy(job => job.Posting.Title, StringComparer.OrdinalIgnoreCase).ThenByDescending(job => job.Score).ToArray(),
            _ => query.OrderByDescending(job => job.Score).ThenByDescending(job => job.FirstSeenUtc).ToArray()
        };
    }

    private static bool MatchesKeyword(StoredJob job, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        return job.Posting.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            job.Posting.Company.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            job.Posting.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            job.Posting.Skills.Any(skill => skill.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasCompensation(StoredJob job) =>
        job.Posting.MinimumHourlyRate is not null ||
        job.Posting.MaximumHourlyRate is not null ||
        !string.IsNullOrWhiteSpace(job.Posting.CompensationDescription);

    private static decimal MaximumHourlyRate(StoredJob job) =>
        job.Posting.MaximumHourlyRate ?? job.Posting.MinimumHourlyRate ?? decimal.MinValue;
}
