using JobSearch.Application.Matching;
using JobSearch.Domain.JobPostings;

namespace JobSearch.Application.Persistence;

public sealed record StoredJob(
    JobPosting Posting,
    int Score,
    IReadOnlyCollection<ScoreReason> ScoreReasons,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    IReadOnlyCollection<JobSourceReference> Sources,
    string Notes = "",
    IReadOnlyCollection<JobStatusChange>? StatusHistory = null,
    JobEligibility? Eligibility = null)
{
    public IReadOnlyCollection<JobStatusChange> History => StatusHistory ?? [];

    public JobEligibility EligibilityAssessment => Eligibility ?? JobEligibility.Eligible;
}
