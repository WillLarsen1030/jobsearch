namespace JobSearch.Application.Career;

public static class EmployerFacingResumePolicy
{
    private static readonly string[] InternalTerms =
    [
        "ApplicantProfile",
        "CareerProfile",
        "Verified ApplicantProfile",
        "EvidenceId",
        "Confidence"
    ];

    public static string CategoryFor(CareerSkill skill)
    {
        if (skill.Name.Equals("Debugging & Performance Optimization", StringComparison.OrdinalIgnoreCase))
            return "Engineering Practices";

        if (!skill.Category.Contains("ApplicantProfile", StringComparison.OrdinalIgnoreCase))
            return skill.Category;

        return SkillNormalizer.Normalize(skill.Name) switch
        {
            "MongoDB" or "Redis" => "Databases",
            "GitHub" or "GitHub Actions" => "Cloud & DevOps",
            "Umbraco CMS" or "Umbraco" => "Back-End / APIs",
            _ => "Technical Skills"
        };
    }

    public static void EnsureSafe(string content)
    {
        var leaked = InternalTerms.FirstOrDefault(term => content.Contains(term, StringComparison.OrdinalIgnoreCase));
        if (leaked is not null)
            throw new InvalidOperationException($"Employer-facing resume contains internal terminology: {leaked}.");
    }
}
