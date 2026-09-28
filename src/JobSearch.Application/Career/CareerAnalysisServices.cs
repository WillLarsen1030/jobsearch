using System.Text.RegularExpressions;
using JobSearch.Domain.JobPostings;

namespace JobSearch.Application.Career;

public static partial class SkillNormalizer
{
    private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["dotnet"] = ".NET",
        ["net"] = ".NET",
        ["net core"] = ".NET",
        ["aspnet"] = "ASP.NET",
        ["aspnet core"] = "ASP.NET Core",
        ["csharp"] = "C#",
        ["restful apis"] = "REST APIs",
        ["rest api"] = "REST APIs",
        ["postgres"] = "PostgreSQL",
        ["mssql"] = "SQL Server",
        ["typescript"] = "TypeScript",
        ["javascript"] = "JavaScript",
        ["javascript es6plus"] = "JavaScript",
        ["angular 2plus"] = "Angular",
        ["umbraco"] = "Umbraco CMS",
        ["umbraco cms"] = "Umbraco CMS",
        ["cicd"] = "CI/CD",
        ["github actions"] = "GitHub Actions"
    };

    public static string Normalize(string value)
    {
        var key = NonAlphaNumeric().Replace(value.ToLowerInvariant().Replace("#", "sharp").Replace("+", "plus"), " ").Trim();
        key = MultipleSpaces().Replace(key, " ");
        return Aliases.TryGetValue(key, out var canonical) ? canonical : value.Trim();
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphaNumeric();
    [GeneratedRegex("\\s+")]
    private static partial Regex MultipleSpaces();
}

public sealed class DeterministicCareerAnalysisService : ICareerAnalysisService
{
    private static readonly string[] KnownTechnologies =
    [
        "C#", ".NET", "ASP.NET", "ASP.NET Core", "Azure", "SQL", "SQL Server", "PostgreSQL", "MongoDB", "Redis",
        "React", "Angular", "Vue", "JavaScript", "TypeScript", "Node.js", "REST APIs", "Microservices", "Umbraco CMS",
        "Entity Framework", "Docker", "GitHub Actions", "CI/CD", "Power BI", "Terraform", "Jenkins", "Kafka"
    ];

    public JobFitAnalysis Analyze(JobPosting job, CareerProfile profile, DateTimeOffset now)
    {
        var jobText = $"{job.Title} {job.Description} {string.Join(' ', job.Skills)}";
        var requirements = KnownTechnologies.Where(technology => Contains(jobText, technology))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(technology => new JobRequirement(technology, SkillNormalizer.Normalize(technology), $"Detected in the posting for {job.Title}."))
            .ToList();
        var profileSkills = profile.Skills
            .GroupBy(skill => SkillNormalizer.Normalize(skill.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(skill => skill.DemonstratedByPositionIds.Count).First(), StringComparer.OrdinalIgnoreCase);
        var analysis = new JobFitAnalysis
        {
            JobPostingId = job.Id,
            CareerProfileVersion = profile.Version,
            CreatedAtUtc = now
        };

        foreach (var requirement in requirements)
        {
            if (profileSkills.TryGetValue(requirement.NormalizedName, out var skill))
            {
                analysis.MatchedRequirements.Add(requirement);
                analysis.TechnologiesMatched.Add(skill.Name);
                AddEvidence(profile, skill, analysis.RelevantEvidence);
            }
            else if (FindEvidence(profile, requirement.Name) is { Count: > 0 } evidenceMatches)
            {
                analysis.MatchedRequirements.Add(requirement);
                analysis.TechnologiesMatched.Add(requirement.Name);
                analysis.RelevantEvidence.AddRange(evidenceMatches);
            }
            else if (RelatedSkill(requirement.NormalizedName, profileSkills.Keys))
            {
                analysis.PartiallyMatchedRequirements.Add(requirement);
            }
            else
            {
                analysis.NotFoundRequirements.Add(requirement with { EvidenceText = "Not found in CareerProfile; this does not establish that the candidate lacks the skill." });
            }
        }

        if (job.RequiresSecurityClearance) analysis.Concerns.Add("The posting appears to require a security clearance; CareerProfile does not assert one.");
        if (job.WorkLocationType != WorkLocationType.Remote) analysis.Concerns.Add("The posting is not clearly remote.");
        if (requirements.Count == 0) analysis.Concerns.Add("The posting did not expose enough structured technical requirements for a high-confidence comparison.");
        if (analysis.NotFoundRequirements.Count > 0) analysis.Concerns.Add($"{analysis.NotFoundRequirements.Count} requirement(s) were not found in CareerProfile and need human review.");

        analysis.ResumeEmphasisRecommendations.AddRange(analysis.TechnologiesMatched.Take(8).Select(value => $"Emphasize verified {value} evidence."));
        if (job.Title.Contains("consult", StringComparison.OrdinalIgnoreCase))
            analysis.ResumeEmphasisRecommendations.Add("Emphasize verified client communication, delivery ownership, modernization, and production support work.");
        if (job.Description.Contains("health", StringComparison.OrdinalIgnoreCase))
            analysis.ResumeEmphasisRecommendations.Add("Emphasize the verified HIPAA-compliant healthcare platform experience.");

        var denominator = Math.Max(1, requirements.Count);
        var technical = (analysis.MatchedRequirements.Count + analysis.PartiallyMatchedRequirements.Count * .5) / denominator;
        var seniority = job.Title.Contains("senior", StringComparison.OrdinalIgnoreCase) && profile.TotalExperienceYears >= 10 ? 10 : 5;
        analysis.OverallFit = Math.Clamp((int)Math.Round(technical * 80 + seniority + (job.WorkLocationType == WorkLocationType.Remote ? 10 : 0)), 0, 100);
        analysis.RelevantEvidence = analysis.RelevantEvidence.DistinctBy(value => value.EvidenceId).Take(12).ToList();
        return analysis;
    }

    private static void AddEvidence(CareerProfile profile, CareerSkill skill, List<CareerEvidenceMatch> matches)
    {
        foreach (var position in profile.EmploymentHistory.Where(value => skill.DemonstratedByPositionIds.Contains(value.Id)))
            foreach (var evidence in position.Evidence.Where(value => Contains(value.Statement, skill.Name)).Take(2))
                matches.Add(new CareerEvidenceMatch(position.Id, evidence.Id, position.Employer, position.Title, evidence.Statement));
        foreach (var evidence in profile.GeneralEvidence.Where(value => Contains(value.Statement, skill.Name)).Take(2))
            matches.Add(new CareerEvidenceMatch(Guid.Empty, evidence.Id, "Career-wide evidence", string.Empty, evidence.Statement));
    }

    private static List<CareerEvidenceMatch> FindEvidence(CareerProfile profile, string requirement)
    {
        var matches = profile.EmploymentHistory.SelectMany(position => position.Evidence
            .Where(evidence => Contains(evidence.Statement, requirement))
            .Select(evidence => new CareerEvidenceMatch(position.Id, evidence.Id, position.Employer, position.Title, evidence.Statement)))
            .Take(3).ToList();
        matches.AddRange(profile.GeneralEvidence.Where(evidence => Contains(evidence.Statement, requirement))
            .Select(evidence => new CareerEvidenceMatch(Guid.Empty, evidence.Id, "Career-wide evidence", string.Empty, evidence.Statement)));
        return matches.Take(3).ToList();
    }

    private static bool RelatedSkill(string requirement, IEnumerable<string> skills) =>
        skills.Any(skill => requirement.Contains(skill, StringComparison.OrdinalIgnoreCase) || skill.Contains(requirement, StringComparison.OrdinalIgnoreCase));

    private static bool Contains(string text, string technology)
    {
        var aliases = technology switch
        {
            ".NET" => new[] { ".net" },
            "ASP.NET" => new[] { "asp.net" },
            "ASP.NET Core" => new[] { "asp.net core" },
            "C#" => new[] { "c#", "c sharp" },
            "REST APIs" => new[] { "rest api", "restful api" },
            "CI/CD" => new[] { "ci/cd", "continuous integration" },
            _ => new[] { technology }
        };
        return aliases.Any(alias => text.Contains(alias, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class DeterministicApplicationAnswerDraftingService : IApplicationAnswerDraftingService
{
    public ApplicationAnswerDraft Draft(string question, JobPosting job, CareerProfile profile, Applications.ApplicantProfile applicantProfile)
    {
        var evidence = profile.EmploymentHistory.SelectMany(position => position.Evidence.Select(item => (position, item)))
            .Where(value => job.Skills.Any(skill => value.item.Statement.Contains(skill, StringComparison.OrdinalIgnoreCase)))
            .Take(2).ToArray();
        if (evidence.Length == 0)
            evidence = profile.EmploymentHistory.SelectMany(position => position.Evidence.Take(1).Select(item => (position, item))).Take(2).ToArray();

        var name = string.IsNullOrWhiteSpace(applicantProfile.PreferredName) ? applicantProfile.LegalFirstName : applicantProfile.PreferredName;
        var details = string.Join(" ", evidence.Select(value => value.item.Statement));
        var draft = $"I am interested in the {job.Title} role at {job.Company} because it aligns with my verified background as {profile.Headline}. {details}".Trim();
        if (question.Contains("project", StringComparison.OrdinalIgnoreCase) && evidence.Length > 0)
            draft = $"One relevant example from my experience is my work at {evidence[0].position.Employer}: {evidence[0].item.Statement}";
        else if (question.Contains("good fit", StringComparison.OrdinalIgnoreCase) || question.Contains("relevant experience", StringComparison.OrdinalIgnoreCase))
            draft = $"My background aligns with this role through verified experience in {string.Join(", ", profile.Skills.Take(8).Select(value => value.Name))}. {details}";
        else if (!string.IsNullOrWhiteSpace(name))
            draft = $"{draft} I would welcome the opportunity to discuss how that experience can support this role.";
        return new ApplicationAnswerDraft(question, draft, evidence.Select(value => value.item.Id).ToArray());
    }
}

public static class ResumeEvidenceValidator
{
    public static void EnsureSupported(CareerProfile profile, IEnumerable<Guid> evidenceIds)
    {
        var supported = profile.EmploymentHistory.SelectMany(position => position.Evidence).Concat(profile.GeneralEvidence).Select(value => value.Id).ToHashSet();
        var unsupported = evidenceIds.Where(id => !supported.Contains(id)).Distinct().ToArray();
        if (unsupported.Length > 0)
            throw new InvalidOperationException($"Resume content referenced {unsupported.Length} unsupported CareerProfile evidence item(s).");
    }
}
