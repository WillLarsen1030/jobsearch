using JobSearch.Domain.JobPostings;

namespace JobSearch.Application.Persistence;

public sealed record JobStatusChange(
    Guid Id,
    ApplicationStatus OldStatus,
    ApplicationStatus NewStatus,
    DateTimeOffset ChangedAtUtc,
    string Note);
