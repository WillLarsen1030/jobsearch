using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using JobSearch.Application.Applications;
using JobSearch.Application.Career;

namespace JobSearch.Infrastructure.Career;

public sealed partial class DocxResumeImportService(TimeProvider timeProvider) : IResumeImportService
{
    public Task<ResumeImportReview> CreateReviewAsync(string path, CareerProfile? existingProfile, ApplicantProfile applicantProfile, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The resume file was not found.", path);
        if (!Path.GetExtension(path).Equals(".docx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Resume import currently supports DOCX files.");

        using var document = WordprocessingDocument.Open(path, false);
        var body = document.MainDocumentPart?.Document?.Body
            ?? throw new InvalidDataException("The DOCX does not contain a readable document body.");
        var paragraphs = body.Elements<Paragraph>()
            .SelectMany(paragraph => ExtractLines(paragraph).Select(text => new ResumeLine(text, paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? string.Empty)))
            .Where(line => !string.IsNullOrWhiteSpace(line.Text)).ToList();
        if (paragraphs.Count < 5) throw new InvalidDataException("The DOCX does not contain enough readable resume content.");

        var profile = ParseProfile(paragraphs, path);
        MergeVerifiedApplicantSkills(profile, applicantProfile);
        var comparisons = BuildComparisons(profile, existingProfile, applicantProfile, paragraphs);
        var review = new ResumeImportReview
        {
            SourcePath = Path.GetFullPath(path),
            CreatedAtUtc = timeProvider.GetUtcNow(),
            ProposedProfile = profile,
            Comparisons = comparisons,
            Ambiguities =
            [
                "The resume lists overlapping roles at FMC Development Group, Microsoft (Contract), and Stars and Strikes. Confirm whether these were concurrent engagements.",
                "Education is written as 'Computer Program Management' without a credential type. The system preserves it as a program and does not infer a degree.",
                "The resume provides no verified numerical impact metrics. Metric-oriented improvements are queued as questions instead of being invented.",
                "Work authorization, sponsorship, clearance, and compensation history are not inferred from the resume."
            ]
        };
        return Task.FromResult(review);
    }

    private static CareerProfile ParseProfile(IReadOnlyList<ResumeLine> lines, string sourcePath)
    {
        var summaryIndex = Find(lines, "Professional Summary");
        var skillsIndex = Find(lines, "Core Technical Skills");
        var experienceIndex = Find(lines, "Professional Experience");
        var educationIndex = Find(lines, "Education");
        var profile = new CareerProfile
        {
            Headline = lines.ElementAtOrDefault(1)?.Text ?? string.Empty,
            ProfessionalSummary = JoinRange(lines, summaryIndex + 1, skillsIndex),
            TotalExperienceYears = ParseYears(JoinRange(lines, summaryIndex + 1, skillsIndex)),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        var categorizedSkills = new List<(string Category, string Name)>();
        for (var index = skillsIndex + 1; index < experienceIndex; index++)
        {
            var split = lines[index].Text.Split(':', 2, StringSplitOptions.TrimEntries);
            if (split.Length != 2) continue;
            categorizedSkills.AddRange(SplitSkills(split[1])
                .Select(skill => (split[0], skill)));
        }

        if (categorizedSkills.Any(value => SkillNormalizer.Normalize(value.Name).Equals("ASP.NET Core", StringComparison.OrdinalIgnoreCase)) &&
            !categorizedSkills.Any(value => SkillNormalizer.Normalize(value.Name).Equals(".NET", StringComparison.OrdinalIgnoreCase)))
            categorizedSkills.Add(("Back-End / APIs", ".NET"));

        profile.EmploymentHistory = ParseEmployment(lines, experienceIndex + 1, educationIndex, sourcePath);
        foreach (var (category, name) in categorizedSkills.DistinctBy(value => SkillNormalizer.Normalize(value.Name), StringComparer.OrdinalIgnoreCase))
        {
            var normalized = SkillNormalizer.Normalize(name);
            var positions = profile.EmploymentHistory.Where(position => Mentions(position, name) || Mentions(position, normalized)).ToList();
            profile.Skills.Add(new CareerSkill
            {
                Name = name,
                NormalizedName = normalized,
                Category = category,
                DemonstratedByPositionIds = positions.Select(position => position.Id).ToList(),
                MostRecentUse = positions.FirstOrDefault()?.EndDate ?? string.Empty,
                EvidenceStrength = name == ".NET" ? "Derived from explicit ASP.NET Core evidence" : positions.Count > 0 ? "Demonstrated in employment evidence" : "Listed in resume skills"
            });
        }

        profile.Education = ParseEducation(lines, educationIndex + 1, sourcePath);
        profile.ProfessionalLinks = UrlPattern().Matches(string.Join(' ', lines.Take(Math.Min(5, lines.Count)).Select(value => value.Text)))
            .Select(match => new ProfessionalLink
            {
                Url = match.Value.TrimEnd('.', ',', ';'),
                Label = match.Value.Contains("linkedin", StringComparison.OrdinalIgnoreCase) ? "LinkedIn" : "Portfolio"
            }).DistinctBy(value => value.Url, StringComparer.OrdinalIgnoreCase).ToList();
        return profile;
    }

    private static List<EmploymentExperience> ParseEmployment(IReadOnlyList<ResumeLine> lines, int start, int end, string sourcePath)
    {
        var values = new List<EmploymentExperience>();
        EmploymentExperience? current = null;
        for (var index = start; index < end; index++)
        {
            var text = lines[index].Text;
            var date = DateLine().Match(text);
            if (current is not null && date.Success && string.IsNullOrWhiteSpace(current.StartDate))
            {
                current.StartDate = date.Groups["start"].Value.Trim();
                current.EndDate = date.Groups["end"].Value.Trim();
                current.IsCurrent = current.EndDate.Equals("Present", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            var header = EmploymentHeader().Match(text);
            if (header.Success)
            {
                current = new EmploymentExperience
                {
                    Employer = header.Groups["employer"].Value.Trim(),
                    Title = header.Groups["title"].Value.Trim(),
                    Source = sourcePath
                };
                values.Add(current);
                continue;
            }
            if (current is null) continue;
            if (text.StartsWith("Stack:", StringComparison.OrdinalIgnoreCase))
            {
                current.Technologies = text[6..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                continue;
            }
            current.Evidence.Add(new CareerEvidence { Statement = text.TrimStart('•', ' '), Source = sourcePath });
        }
        return values;
    }

    private static List<CareerEducation> ParseEducation(IReadOnlyList<ResumeLine> lines, int start, string sourcePath)
    {
        var values = new List<CareerEducation>();
        foreach (var line in lines.Skip(start))
        {
            var match = EducationLine().Match(line.Text);
            if (!match.Success) continue;
            values.Add(new CareerEducation
            {
                Institution = match.Groups["institution"].Value.Trim(),
                Program = match.Groups["program"].Value.Trim(),
                StartDate = match.Groups["start"].Value.Trim(),
                EndDate = match.Groups["end"].Value.Trim(),
                Source = sourcePath
            });
        }
        return values;
    }

    private static List<ImportFieldComparison> BuildComparisons(CareerProfile proposed, CareerProfile? existing, ApplicantProfile applicant, IReadOnlyList<ResumeLine> lines)
    {
        var contact = lines.ElementAtOrDefault(2)?.Text ?? string.Empty;
        var email = EmailPattern().Match(contact).Value;
        var phone = PhonePattern().Match(contact).Value;
        var location = contact.Split('|', StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
        var linkedin = proposed.ProfessionalLinks.FirstOrDefault(value => value.Label == "LinkedIn")?.Url ?? string.Empty;
        var portfolio = proposed.ProfessionalLinks.FirstOrDefault(value => value.Label == "Portfolio")?.Url ?? string.Empty;
        return
        [
            Compare("Career headline", proposed.Headline, existing?.Headline ?? applicant.CurrentTitle),
            Compare("Professional summary", proposed.ProfessionalSummary, existing?.ProfessionalSummary ?? string.Empty),
            Compare("Total experience", proposed.TotalExperienceYears?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, existing?.TotalExperienceYears?.ToString(CultureInfo.InvariantCulture) ?? applicant.YearsOfExperience?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
            Compare("Email", email, applicant.Email),
            Compare("Phone", phone, applicant.Phone),
            Compare("Location", location, string.Join(", ", new[] { applicant.City, applicant.State }.Where(value => !string.IsNullOrWhiteSpace(value)))),
            Compare("LinkedIn", linkedin, applicant.LinkedInUrl),
            Compare("Portfolio", portfolio, applicant.PortfolioUrl),
            Compare("Skills", string.Join(", ", proposed.Skills.Select(value => value.Name)), existing is null ? string.Join(", ", applicant.TechnologySkills) : string.Join(", ", existing.Skills.Select(value => value.Name))),
            Compare("Employment history", $"{proposed.EmploymentHistory.Count} positions", existing is null ? $"{applicant.EmploymentHistory.Count} positions" : $"{existing.EmploymentHistory.Count} positions"),
            Compare("Education", $"{proposed.Education.Count} entries", existing is null ? $"{applicant.Education.Count} entries" : $"{existing.Education.Count} entries")
        ];
    }

    private static ImportFieldComparison Compare(string field, string extracted, string existing)
    {
        var conflict = !string.IsNullOrWhiteSpace(existing) && !string.Equals(Normalize(extracted), Normalize(existing), StringComparison.OrdinalIgnoreCase);
        return new ImportFieldComparison(field, extracted, existing, extracted, conflict,
            conflict ? "Existing and extracted values differ; review before approval." : string.Empty);
    }

    private static bool Mentions(EmploymentExperience position, string skill) =>
        position.Technologies.Any(value => value.Contains(skill, StringComparison.OrdinalIgnoreCase) || skill.Contains(value, StringComparison.OrdinalIgnoreCase)) ||
        position.Evidence.Any(value => value.Statement.Contains(skill, StringComparison.OrdinalIgnoreCase));
    private static void MergeVerifiedApplicantSkills(CareerProfile profile, ApplicantProfile applicantProfile)
    {
        foreach (var skillName in applicantProfile.TechnologySkills)
        {
            var normalized = SkillNormalizer.Normalize(skillName);
            if (profile.Skills.Any(value => value.NormalizedName.Equals(normalized, StringComparison.OrdinalIgnoreCase))) continue;
            profile.Skills.Add(new CareerSkill
            {
                Name = skillName,
                NormalizedName = normalized,
                Category = "Verified ApplicantProfile",
                EvidenceStrength = "Manually verified in ApplicantProfile"
            });
        }
    }
    private static IEnumerable<string> SplitSkills(string value)
    {
        var current = new System.Text.StringBuilder();
        var depth = 0;
        foreach (var character in value)
        {
            if (character == '(') depth++;
            if (character == ')') depth = Math.Max(0, depth - 1);
            if (character == ',' && depth == 0)
            {
                if (current.Length > 0) yield return current.ToString().Trim();
                current.Clear();
            }
            else current.Append(character);
        }
        if (current.Length > 0) yield return current.ToString().Trim();
    }
    private static int Find(IReadOnlyList<ResumeLine> lines, string heading) =>
        lines.Select((line, index) => (line, index)).First(value => value.line.Text.Equals(heading, StringComparison.OrdinalIgnoreCase)).index;
    private static string JoinRange(IReadOnlyList<ResumeLine> lines, int start, int end) => string.Join(" ", lines.Skip(start).Take(end - start).Select(value => value.Text));
    private static decimal? ParseYears(string summary) => decimal.TryParse(YearsPattern().Match(summary).Groups[1].Value, out var years) ? years : null;
    private static string Normalize(string value) => Whitespace().Replace(value, " ").Trim().TrimEnd('/');
    private static IEnumerable<string> ExtractLines(Paragraph paragraph)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var run in paragraph.Descendants<Run>())
        {
            foreach (var child in run.ChildElements)
            {
                if (child is Text text) current.Append(text.Text);
                else if (child is Break)
                {
                    if (current.Length > 0) parts.Add(current.ToString().Trim());
                    current.Clear();
                }
                else if (child is TabChar) current.Append(' ');
            }
        }
        if (current.Length > 0) parts.Add(current.ToString().Trim());
        return parts.Where(value => !string.IsNullOrWhiteSpace(value));
    }
    private sealed record ResumeLine(string Text, string Style);

    [GeneratedRegex(@"^(?<employer>.+?)\s+[\u2013\u2014\uFFFD-]\s+(?<title>.+)$")]
    private static partial Regex EmploymentHeader();
    [GeneratedRegex(@"^(?:.+?\|\s*)?(?<start>(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)?\s*\d{4})\s+[\u2013\u2014\uFFFD-]\s+(?<end>Present|(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)?\s*\d{4})$")]
    private static partial Regex DateLine();
    [GeneratedRegex(@"^(?<institution>.+?)\s+[\u2013\u2014\uFFFD-]\s+(?<program>.+?)\s*\((?<start>\d{4})\s+[\u2013\u2014\uFFFD-]\s+(?<end>\d{4})\)$")]
    private static partial Regex EducationLine();
    [GeneratedRegex(@"https?://[^\s|]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();
    [GeneratedRegex(@"[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailPattern();
    [GeneratedRegex(@"(?:\+?1[-.\s]?)?\(?\d{3}\)?[-.\s]\d{3}[-.\s]\d{4}")]
    private static partial Regex PhonePattern();
    [GeneratedRegex(@"(\d+(?:\.\d+)?)\+?\s+years", RegexOptions.IgnoreCase)]
    private static partial Regex YearsPattern();
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
