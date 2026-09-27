namespace JobSearch.Application.Sources;

public sealed record JobSourceRequest(
    IReadOnlyCollection<string> SearchTerms,
    IReadOnlyCollection<string> SkillKeywords,
    string CountryCode,
    int MaximumResults);
