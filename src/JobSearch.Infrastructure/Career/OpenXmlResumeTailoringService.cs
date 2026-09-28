using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using JobSearch.Application.Applications;
using JobSearch.Application.Career;
using JobSearch.Domain.JobPostings;

namespace JobSearch.Infrastructure.Career;

public sealed class OpenXmlResumeTailoringService(TimeProvider timeProvider) : IResumeTailoringService
{
    public Task<ResumeArtifact> GenerateMasterAsync(CareerProfile profile, ApplicantProfile applicantProfile, string outputDirectory, CancellationToken cancellationToken = default) =>
        GenerateAsync(null, profile, applicantProfile, null, outputDirectory, cancellationToken);

    public Task<ResumeArtifact> GenerateTailoredAsync(JobPosting job, CareerProfile profile, ApplicantProfile applicantProfile, JobFitAnalysis analysis, string outputDirectory, CancellationToken cancellationToken = default) =>
        GenerateAsync(job, profile, applicantProfile, analysis, outputDirectory, cancellationToken);

    private Task<ResumeArtifact> GenerateAsync(JobPosting? job, CareerProfile profile, ApplicantProfile applicant, JobFitAnalysis? analysis, string outputDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (profile.Status != CareerProfileStatus.Approved)
            throw new InvalidOperationException("CareerProfile must be approved before generating a resume.");

        Directory.CreateDirectory(outputDirectory);
        var kind = job is null ? ResumeArtifactKind.Master : ResumeArtifactKind.Tailored;
        var suffix = job is null ? "Master" : SanitizeFileName($"{job.Company}-{job.Title}");
        var fileName = $"William-Larsen-{suffix}-{timeProvider.GetUtcNow():yyyyMMdd-HHmmss}.docx";
        var path = Path.GetFullPath(Path.Combine(outputDirectory, fileName));
        var selected = SelectEvidence(profile, job).ToList();
        var generalEvidence = SelectGeneralEvidence(profile, job).ToList();
        ResumeEvidenceValidator.EnsureSupported(profile, selected.Select(value => value.Evidence.Id).Concat(generalEvidence.Select(value => value.Id)));

        using (var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document();
            AddStyles(main);
            AddNumbering(main);
            var body = main.Document.AppendChild(new Body());
            AddHeader(body, applicant, profile);
            AddSectionHeading(body, "Professional Summary");
            AddParagraph(body, profile.ProfessionalSummary, "Body", 0, 40);
            AddSectionHeading(body, "Core Technical Skills");
            foreach (var group in OrderSkills(profile, job).GroupBy(EmployerFacingResumePolicy.CategoryFor))
                AddParagraph(body, $"{group.Key}: {string.Join(", ", group.Select(value => value.Name))}", "Body", 0, 15);
            AddSectionHeading(body, "Professional Experience");
            for (var positionIndex = 0; positionIndex < profile.EmploymentHistory.Count; positionIndex++)
            {
                var position = profile.EmploymentHistory[positionIndex];
                if (positionIndex == 1) body.Append(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
                AddPositionHeading(body, position);
                if (position.Technologies.Count > 0)
                    AddParagraph(body, $"Technologies: {string.Join(", ", OrderTechnologies(position.Technologies, job))}", "Body", 0, 15, italic: true);
                foreach (var evidence in selected.Where(value => value.Position.Id == position.Id).Select(value => value.Evidence))
                    AddBullet(body, evidence.Statement);
            }
            AddSectionHeading(body, "Education");
            foreach (var education in profile.Education)
                AddParagraph(body, EducationText(education), "Body", 0, 20);
            AddPageSettings(body);
            EmployerFacingResumePolicy.EnsureSafe(body.InnerText);
            main.Document.Save();
        }

        var artifact = new ResumeArtifact
        {
            JobPostingId = job?.Id,
            Kind = kind,
            FileName = fileName,
            FilePath = path,
            GeneratedAtUtc = timeProvider.GetUtcNow(),
            CareerProfileVersion = profile.Version,
            EvidenceIds = selected.Select(value => value.Evidence.Id).Concat(generalEvidence.Select(value => value.Id)).Distinct().ToList()
        };
        return Task.FromResult(artifact);
    }

    private static IEnumerable<CareerEvidence> SelectGeneralEvidence(CareerProfile profile, JobPosting? job)
    {
        if (job is null) return profile.GeneralEvidence;
        var jobText = $"{job.Title} {job.Description} {string.Join(' ', job.Skills)}";
        return profile.GeneralEvidence.OrderByDescending(value => Relevance(value.Statement, jobText)).Take(5);
    }

    private static IEnumerable<(EmploymentExperience Position, CareerEvidence Evidence)> SelectEvidence(CareerProfile profile, JobPosting? job)
    {
        var jobText = job is null ? string.Empty : $"{job.Title} {job.Description} {string.Join(' ', job.Skills)}";
        foreach (var position in profile.EmploymentHistory)
        {
            var limit = job is null ? int.MaxValue : profile.EmploymentHistory.IndexOf(position) < 4 ? 5 : 2;
            foreach (var evidence in position.Evidence
                .OrderByDescending(item => job is null ? MasterPriority(item.Statement) : Relevance(item.Statement, jobText))
                .ThenBy(item => position.Evidence.IndexOf(item))
                .Take(job is null && profile.EmploymentHistory.IndexOf(position) == 0 ? 8 : limit))
                yield return (position, evidence);
        }
    }

    private static int MasterPriority(string statement)
    {
        var priorities = new[]
        {
            "600 ms", "75 ms", "mentor", "code review", "architect", "architecture", "technical ownership",
            "CI/CD", "YAML", "secrets", "environments", "ASP.NET Core", "REST", "performance", "production"
        };
        return priorities.Count(value => statement.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<CareerSkill> OrderSkills(CareerProfile profile, JobPosting? job)
    {
        var jobText = job is null ? string.Empty : $"{job.Title} {job.Description} {string.Join(' ', job.Skills)}";
        var hasCombinedPerformanceSkill = profile.Skills.Any(skill => skill.Name.Equals("Debugging & Performance Optimization", StringComparison.OrdinalIgnoreCase));
        return profile.Skills
            .Where(skill => !hasCombinedPerformanceSkill || !skill.Name.Equals("Performance Optimization", StringComparison.OrdinalIgnoreCase))
            .GroupBy(skill => SkillNormalizer.Normalize(skill.Name), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(DisplayPreference).ThenBy(skill => skill.Name).First())
            .OrderByDescending(skill => Relevance(skill.Name, jobText))
            .ThenBy(EmployerFacingResumePolicy.CategoryFor)
            .ThenBy(skill => skill.Name);
    }

    private static int DisplayPreference(CareerSkill skill) => skill.Name switch
    {
        "Umbraco CMS" => 10,
        _ => skill.DemonstratedByPositionIds.Count
    };

    private static IEnumerable<string> OrderTechnologies(IEnumerable<string> technologies, JobPosting? job)
    {
        var jobText = job is null ? string.Empty : $"{job.Title} {job.Description} {string.Join(' ', job.Skills)}";
        return technologies.OrderByDescending(value => Relevance(value, jobText)).ThenBy(value => value);
    }

    private static int Relevance(string text, string jobText)
    {
        if (string.IsNullOrWhiteSpace(jobText)) return 0;
        return text.Split([' ', ',', '/', '(', ')', '.', ':'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 2).Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(token => jobText.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddStyles(MainDocumentPart main)
    {
        var stylesPart = main.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = new Styles(
            Style("Normal", "Normal", "Arial", "19", false),
            Style("Body", "Body", "Arial", "20", false),
            Style("Name", "Name", "Arial", "32", true),
            Style("Headline", "Headline", "Arial", "22", true),
            Style("Section", "Section", "Arial", "21", true),
            Style("Position", "Position", "Arial", "20", true));
        stylesPart.Styles.Save();
    }

    private static void AddNumbering(MainDocumentPart main)
    {
        var part = main.AddNewPart<NumberingDefinitionsPart>();
        part.Numbering = new Numbering(
            new AbstractNum(
                new MultiLevelType { Val = MultiLevelValues.SingleLevel },
                new Level(
                    new NumberingFormat { Val = NumberFormatValues.Bullet },
                    new LevelText { Val = "\u2022" },
                    new LevelJustification { Val = LevelJustificationValues.Left },
                    new PreviousParagraphProperties(new Indentation { Left = "300", Hanging = "180" }))
                { LevelIndex = 0 })
            { AbstractNumberId = 1 },
            new NumberingInstance(new AbstractNumId { Val = 1 }) { NumberID = 1 });
        part.Numbering.Save();
    }

    private static Style Style(string id, string name, string font, string size, bool bold)
    {
        var runProperties = new StyleRunProperties(new RunFonts { Ascii = font, HighAnsi = font }, new FontSize { Val = size });
        if (bold) runProperties.Append(new Bold());
        return new Style
        {
            Type = StyleValues.Paragraph,
            StyleId = id,
            CustomStyle = id != "Normal",
            StyleName = new StyleName { Val = name },
            StyleRunProperties = runProperties
        };
    }

    private static void AddHeader(Body body, ApplicantProfile applicant, CareerProfile profile)
    {
        var name = string.Join(' ', new[] { applicant.LegalFirstName, applicant.LegalLastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
        AddParagraph(body, name, "Name", 0, 20, alignment: JustificationValues.Center);
        AddParagraph(body, ResumeHeadline(profile), "Headline", 0, 20, alignment: JustificationValues.Center);
        var contact = new[]
        {
            string.Join(", ", new[] { applicant.City, applicant.State }.Where(value => !string.IsNullOrWhiteSpace(value))),
            applicant.Email, applicant.Phone,
            profile.ProfessionalLinks.FirstOrDefault(value => value.Label == "LinkedIn")?.Url,
            profile.ProfessionalLinks.FirstOrDefault(value => value.Label == "Portfolio")?.Url
        };
        AddParagraph(body, string.Join(" | ", contact.Where(value => !string.IsNullOrWhiteSpace(value))), "Body", 0, 55, alignment: JustificationValues.Center);
    }

    private static void AddSectionHeading(Body body, string text) => AddParagraph(body, text, "Section", 90, 30);

    private static void AddPositionHeading(Body body, EmploymentExperience position)
    {
        var dates = string.IsNullOrWhiteSpace(position.StartDate) ? string.Empty : $" | {position.StartDate}-{position.EndDate}";
        AddParagraph(body, $"{position.Employer} | {position.Title}{dates}", "Position", 45, 10);
    }

    private static void AddBullet(Body body, string text)
    {
        var paragraph = AddParagraph(body, text, "Body", 0, 10);
        paragraph.ParagraphProperties ??= new ParagraphProperties();
        paragraph.ParagraphProperties.Append(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 1 }));
        paragraph.ParagraphProperties.Append(new Indentation { Left = "300", Hanging = "180" });
    }

    private static Paragraph AddParagraph(Body body, string text, string style, int before, int after, bool italic = false, JustificationValues? alignment = null)
    {
        var properties = new ParagraphProperties(new ParagraphStyleId { Val = style }, new SpacingBetweenLines { Before = before.ToString(), After = after.ToString(), Line = "230", LineRule = LineSpacingRuleValues.Auto });
        if (alignment is not null) properties.Append(new Justification { Val = alignment });
        var runProperties = new RunProperties();
        if (italic) runProperties.Append(new Italic());
        var paragraph = new Paragraph(properties, new Run(runProperties, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        body.Append(paragraph);
        return paragraph;
    }

    private static void AddPageSettings(Body body)
    {
        var section = new SectionProperties(
            new PageSize { Width = 12240, Height = 15840 },
            new PageMargin { Top = 720, Right = 720, Bottom = 720, Left = 720, Header = 360, Footer = 360, Gutter = 0 });
        body.Append(section);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        return string.Join('-', safe.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('-');
    }

    private static string ResumeHeadline(CareerProfile profile) =>
        profile.Headline.Contains("Senior", StringComparison.OrdinalIgnoreCase) &&
        profile.Skills.Any(value => value.NormalizedName is ".NET" or "ASP.NET Core")
            ? "Senior .NET / Full-Stack Engineer"
            : profile.Headline;

    private static string EducationText(CareerEducation education)
    {
        var program = education.CredentialEarned == false && !education.Program.Contains("coursework", StringComparison.OrdinalIgnoreCase)
            ? $"{education.Program} coursework"
            : education.Program;
        return $"{education.Institution} | {program} | {education.StartDate}-{education.EndDate}";
    }
}
