using JobSearch.Application.Matching;
using JobSearch.Application.Review;
using JobSearch.Domain.JobPostings;

namespace JobSearch.Application.Persistence;

public interface IJobRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredJob>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<StoredJob>> SearchAsync(
        JobListFilter filter,
        JobSort sort,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<DashboardSummary> GetDashboardSummaryAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceAnalytics>> GetSourceAnalyticsAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<StoredJob?> FindByIdPrefixAsync(string idPrefix, CancellationToken cancellationToken = default);

    Task<StoredJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<JobUpsertResult> UpsertAsync(
        JobPosting posting,
        JobScore score,
        Guid? existingJobId,
        DateTimeOffset seenAtUtc,
        CancellationToken cancellationToken = default);

    Task UpdateScoreAsync(
        Guid jobId,
        JobScore score,
        CancellationToken cancellationToken = default);

    Task<StoredJob> ChangeStatusAsync(
        Guid jobId,
        ApplicationStatus newStatus,
        string? note,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);

    Task<StoredJob> UpdateNotesAsync(
        Guid jobId,
        string notes,
        CancellationToken cancellationToken = default);
}
