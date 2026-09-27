namespace JobSearch.Application.Matching;

public sealed record JobScore(
    int Value,
    IReadOnlyCollection<ScoreReason> Reasons,
    JobEligibility? Eligibility = null)
{
    public bool IsStrongMatch => Value >= 75;

    public JobEligibility EligibilityAssessment => Eligibility ?? JobEligibility.Eligible;
}
