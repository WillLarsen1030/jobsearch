using JobSearch.Domain.JobPostings;

namespace JobSearch.Application.Review;

public sealed record JobListFilter(
    int MinimumScore = 0,
    ApplicationStatus? Status = null,
    WorkLocationType? WorkLocation = null,
    EmploymentType? EmploymentType = null,
    bool? HasCompensation = null,
    string Keyword = "",
    DateTimeOffset? DiscoveredAfterUtc = null);
