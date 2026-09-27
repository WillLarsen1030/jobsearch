namespace JobSearch.Application.Matching;

public sealed record JobEligibility(
    bool IsEligible,
    IReadOnlyCollection<string> Problems,
    IReadOnlyCollection<string> Unknowns)
{
    public static JobEligibility Eligible { get; } = new(true, [], []);
}
