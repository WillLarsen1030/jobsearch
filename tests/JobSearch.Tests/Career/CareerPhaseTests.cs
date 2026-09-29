using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using JobSearch.Application.Applications;
using JobSearch.Application.Career;
using JobSearch.Application.Matching;
using JobSearch.Domain.JobPostings;
using JobSearch.Infrastructure.Career;
using JobSearch.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobSearch.Tests.Career;

public sealed class CareerPhaseTests
{
    [Fact]
    public async Task DocxImport_ExtractsEvidenceSkillsAndConflictsWithoutChangingSource()
    {
        var directory = NewDirectory();
        try
        {
            var path = CreateResume(directory);
            var before = File.ReadAllBytes(path);
            var applicant = new ApplicantProfile { Email = "existing@example.com", CurrentTitle = "Developer", YearsOfExperience = 9, TechnologySkills = ["MongoDB"] };
            var review = await new DocxResumeImportService(TimeProvider.System).CreateReviewAsync(path, null, applicant);

            Assert.Equal("Senior .NET Engineer", review.ProposedProfile.Headline);
            Assert.Single(review.ProposedProfile.EmploymentHistory);
            Assert.Contains(review.ProposedProfile.Skills, value => value.NormalizedName == "C#");
            Assert.Contains(review.ProposedProfile.Skills, value => value.Name == "MongoDB" && value.EvidenceStrength == "Manually verified in ApplicantProfile");
            Assert.NotEmpty(review.ProposedProfile.EmploymentHistory[0].Evidence);
            Assert.Contains(review.Comparisons, value => value.Field == "Email" && value.HasConflict);
            Assert.Equal(before, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DocxImport_HandlesManualLineBreaksInsideParagraphs()
    {
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "line-breaks.docx");
            using (var document = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
            {
                var main = document.AddMainDocumentPart();
                var body = new Body();
                main.Document = new Document(body);
                body.Append(LineBreakParagraph("Test Candidate", "Senior .NET Engineer", "Remote | candidate@example.com | 555-555-0100"));
                body.Append(LineBreakParagraph("Professional Summary", "Engineer with 10+ years of experience."));
                foreach (var value in new[] { "Core Technical Skills", "Languages: C#", "Professional Experience" }) body.Append(new Paragraph(new Run(new Text(value))));
                body.Append(LineBreakParagraph("Example Health – Senior Engineer", "Remote | 2020 – Present", "Stack: C#, ASP.NET Core"));
                body.Append(new Paragraph(new Run(new Text("Built ASP.NET Core APIs."))));
                body.Append(LineBreakParagraph("Education", "Example Institute – Computer Programming (2008 – 2010)"));
                main.Document.Save();
            }
            var review = await new DocxResumeImportService(TimeProvider.System).CreateReviewAsync(path, null, new ApplicantProfile());
            Assert.Equal("Senior .NET Engineer", review.ProposedProfile.Headline);
            Assert.Single(review.ProposedProfile.EmploymentHistory);
            Assert.Single(review.ProposedProfile.Education);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("csharp", "C#")]
    [InlineData("Postgres", "PostgreSQL")]
    [InlineData("Umbraco CMS", "Umbraco CMS")]
    public void SkillNormalization_UsesCanonicalNames(string input, string expected) =>
        Assert.Equal(expected, SkillNormalizer.Normalize(input));

    [Fact]
    public async Task CareerProfilePersistence_PreservesEvidenceRelationshipsAndVersions()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var profile = Profile();
        var first = await fixture.Careers.SaveProfileAsync(profile);
        first.Headline = "Updated headline";
        var second = await fixture.Careers.SaveProfileAsync(first);
        var loaded = await fixture.Careers.GetProfileAsync();

        Assert.Equal(2, second.Version);
        Assert.Equal("Updated headline", loaded!.Headline);
        Assert.Equal(profile.EmploymentHistory[0].Id, loaded.Skills[0].DemonstratedByPositionIds[0]);
        Assert.Equal(profile.EmploymentHistory[0].Evidence[0].Id, loaded.EmploymentHistory[0].Evidence[0].Id);
    }

    [Fact]
    public async Task ResumeGeneration_ProducesMasterAndTailoredArtifactsUsingOnlyProfileEvidence()
    {
        var directory = NewDirectory();
        try
        {
            var profile = Profile();
            var applicant = new ApplicantProfile { LegalFirstName = "Test", LegalLastName = "Candidate", Email = "test@example.com" };
            var generator = new OpenXmlResumeTailoringService(TimeProvider.System);
            var master = await generator.GenerateMasterAsync(profile, applicant, directory);
            var job = Job();
            var analysis = new DeterministicCareerAnalysisService().Analyze(job, profile, DateTimeOffset.UtcNow);
            var tailored = await generator.GenerateTailoredAsync(job, profile, applicant, analysis, directory);
            var supported = profile.EmploymentHistory.SelectMany(value => value.Evidence).Select(value => value.Id).ToHashSet();

            Assert.True(File.Exists(master.FilePath));
            Assert.True(File.Exists(tailored.FilePath));
            Assert.All(tailored.EvidenceIds, id => Assert.Contains(id, supported));
            Assert.Contains("Built ASP.NET Core APIs", ReadDocumentText(tailored.FilePath));
            Assert.Throws<InvalidOperationException>(() => ResumeEvidenceValidator.EnsureSupported(profile, [Guid.NewGuid()]));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Phase61Evidence_PersistsLeadershipArchitecturePerformanceDeliveryAndEducationStatus()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var profile = Profile();
        var position = profile.EmploymentHistory[0];
        position.Evidence.AddRange(
        [
            new CareerEvidence { Statement = "Mentor junior developers through code reviews, technical feedback, troubleshooting, and feature ownership guidance without people-management responsibility.", Source = "User verified" },
            new CareerEvidence { Statement = "Participate in architecture and implementation decisions, changing technologies or approaches when needed to improve compatibility and usability.", Source = "User verified" },
            new CareerEvidence { Statement = "Reduced a representative operation's response time from approximately 600 ms to 75 ms, an approximately 87.5% reduction.", Source = "User verified" },
            new CareerEvidence { Statement = "Write YAML CI/CD workflows, use secrets securely in deployment configuration, and deploy applications to multiple environments.", Source = "User verified" }
        ]);
        profile.Education = [new CareerEducation { Institution = "ITT Technical Institute", Program = "Computer Program Management", StartDate = "2007", EndDate = "2009", CredentialEarned = false, Source = "User verified" }];
        profile.GeneralEvidence.Add(new CareerEvidence { Statement = "Write YAML CI/CD workflows, use secrets securely in deployment configuration, and deploy applications to multiple environments.", Source = "User verified" });
        profile.Skills.AddRange(
        [
            new CareerSkill { Name = "CI/CD", NormalizedName = "CI/CD", Category = "Cloud & DevOps", DemonstratedByPositionIds = [position.Id], EvidenceStrength = "Demonstrated" },
            new CareerSkill { Name = "YAML", NormalizedName = "YAML", Category = "Cloud & DevOps", DemonstratedByPositionIds = [position.Id], EvidenceStrength = "Demonstrated" },
            new CareerSkill { Name = "MongoDB", NormalizedName = "MongoDB", Category = "Verified ApplicantProfile", EvidenceStrength = "Manually verified professional experience" }
        ]);

        await fixture.Careers.SaveProfileAsync(profile);
        var loaded = await fixture.Careers.GetProfileAsync();

        Assert.False(Assert.Single(loaded!.Education).CredentialEarned);
        Assert.Contains(loaded.EmploymentHistory[0].Evidence, value => value.Statement.Contains("Mentor junior developers", StringComparison.Ordinal));
        Assert.Contains(loaded.EmploymentHistory[0].Evidence, value => value.Statement.Contains("architecture", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(loaded.EmploymentHistory[0].Evidence, value => value.Statement.Contains("600 ms to 75 ms", StringComparison.Ordinal));
        Assert.Contains(loaded.GeneralEvidence, value => value.Statement.Contains("YAML CI/CD", StringComparison.Ordinal));
        Assert.Contains(loaded.Skills, value => value.NormalizedName == "CI/CD");
        Assert.Contains(loaded.Skills, value => value.NormalizedName == "MongoDB");
    }

    [Fact]
    public async Task EmployerFacingResume_UsesPublicCategoriesAndCourseworkWithoutInternalTerminology()
    {
        var directory = NewDirectory();
        try
        {
            var profile = Profile();
            profile.Education = [new CareerEducation { Institution = "ITT Technical Institute", Program = "Computer Program Management", StartDate = "2007", EndDate = "2009", CredentialEarned = false }];
            profile.Skills.Add(new CareerSkill { Name = "MongoDB", NormalizedName = "MongoDB", Category = "Verified ApplicantProfile", EvidenceStrength = "Manually verified professional experience" });
            profile.Skills.AddRange(
            [
                new CareerSkill { Name = "Umbraco", NormalizedName = "Umbraco", Category = "Verified ApplicantProfile", EvidenceStrength = "Manually verified professional experience" },
                new CareerSkill { Name = "Umbraco CMS", NormalizedName = "Umbraco CMS", Category = "Back-End / APIs", EvidenceStrength = "Demonstrated" },
                new CareerSkill { Name = "Performance Optimization", NormalizedName = "Performance Optimization", Category = "Engineering Practices", EvidenceStrength = "Demonstrated" },
                new CareerSkill { Name = "Debugging & Performance Optimization", NormalizedName = "Debugging & Performance Optimization", Category = "Other", EvidenceStrength = "Demonstrated" }
            ]);
            var artifact = await new OpenXmlResumeTailoringService(TimeProvider.System).GenerateMasterAsync(
                profile, new ApplicantProfile { LegalFirstName = "Test", LegalLastName = "Candidate" }, directory);
            var text = ReadDocumentText(artifact.FilePath);

            Assert.Contains("Databases: MongoDB", text);
            Assert.Contains("Computer Program Management coursework", text);
            Assert.Contains("Back-End / APIs: Umbraco CMS", text);
            Assert.DoesNotContain("Umbraco, Umbraco CMS", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Engineering Practices: Debugging & Performance Optimization", text);
            Assert.DoesNotContain("Engineering Practices: Performance Optimization", text);
            Assert.DoesNotContain("degree", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("credential", text, StringComparison.OrdinalIgnoreCase);
            foreach (var term in new[] { "ApplicantProfile", "CareerProfile", "Verified ApplicantProfile", "EvidenceId", "Confidence" })
                Assert.DoesNotContain(term, text, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void EnrichedFitAnalysis_MatchesCiCdAndMongoDbButKeepsUnsupportedTechnologiesNotFound()
    {
        var profile = Profile();
        profile.Skills.AddRange(
        [
            new CareerSkill { Name = "CI/CD", NormalizedName = "CI/CD", Category = "Cloud & DevOps", EvidenceStrength = "Demonstrated" },
            new CareerSkill { Name = "MongoDB", NormalizedName = "MongoDB", Category = "Databases", EvidenceStrength = "Verified professional experience" }
        ]);
        var baseline = Job();
        var job = baseline with { Description = baseline.Description + " Use CI/CD, MongoDB, Entity Framework, Jenkins, Kafka, and Terraform." };

        var analysis = new DeterministicCareerAnalysisService().Analyze(job, profile, DateTimeOffset.UtcNow);

        Assert.Contains(analysis.MatchedRequirements, value => value.Name == "CI/CD");
        Assert.Contains(analysis.MatchedRequirements, value => value.Name == "MongoDB");
        foreach (var technology in new[] { "Entity Framework", "Terraform", "Jenkins", "Kafka" })
            Assert.Contains(analysis.NotFoundRequirements, value => value.Name == technology);
    }

    [Fact]
    public async Task TailoredResume_TracksCareerWideCiCdEvidence()
    {
        var directory = NewDirectory();
        try
        {
            var profile = Profile();
            var evidence = new CareerEvidence { Statement = "Write YAML CI/CD workflows, use secrets securely in deployment configuration, and deploy applications to multiple environments.", Source = "User verified" };
            profile.GeneralEvidence.Add(evidence);
            profile.Skills.Add(new CareerSkill { Name = "CI/CD", NormalizedName = "CI/CD", Category = "Cloud & DevOps", EvidenceStrength = "Demonstrated" });
            var baseline = Job();
            var job = baseline with { Description = baseline.Description + " Design and enhance CI/CD pipelines." };
            var analysis = new DeterministicCareerAnalysisService().Analyze(job, profile, DateTimeOffset.UtcNow);

            var artifact = await new OpenXmlResumeTailoringService(TimeProvider.System).GenerateTailoredAsync(
                job, profile, new ApplicantProfile { LegalFirstName = "Test", LegalLastName = "Candidate" }, analysis, directory);

            Assert.Contains(evidence.Id, artifact.EvidenceIds);
            ResumeEvidenceValidator.EnsureSupported(profile, artifact.EvidenceIds);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void JobFitAnalysis_SeparatesMatchedFromNotFoundAndCarriesEvidence()
    {
        var analysis = new DeterministicCareerAnalysisService().Analyze(Job(), Profile(), DateTimeOffset.UtcNow);

        Assert.Contains(analysis.MatchedRequirements, value => value.Name == "C#");
        Assert.Contains(analysis.MatchedRequirements, value => value.Name == "ASP.NET Core");
        Assert.Contains(analysis.MatchedRequirements, value => value.Name == "Power BI");
        Assert.Contains(analysis.NotFoundRequirements, value => value.Name == "Terraform");
        Assert.Contains(analysis.NotFoundRequirements, value => value.EvidenceText.StartsWith("Not found in CareerProfile", StringComparison.Ordinal));
        Assert.NotEmpty(analysis.RelevantEvidence);
    }

    [Fact]
    public void JobFitAnalysis_CollapsesEquivalentNormalizedSkills()
    {
        var profile = Profile();
        profile.Skills.Add(new CareerSkill { Name = "Umbraco", NormalizedName = "Umbraco", Category = "Back-End / APIs" });
        profile.Skills.Add(new CareerSkill { Name = "Umbraco CMS", NormalizedName = "Umbraco CMS", Category = "Back-End / APIs" });
        var baseline = Job();
        var job = baseline with { Description = baseline.Description + " Maintain an Umbraco CMS application." };

        var analysis = new DeterministicCareerAnalysisService().Analyze(job, profile, DateTimeOffset.UtcNow);

        Assert.Single(analysis.MatchedRequirements, value => value.Name == "Umbraco CMS");
    }

    [Fact]
    public async Task GeneratedAnswerDraft_RemainsUnresolvedUntilHumanApproval()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var posting = Job();
        await fixture.Jobs.UpsertAsync(posting, new JobScore(90, []), null, DateTimeOffset.UtcNow);
        var application = await fixture.Applications.CreateAsync(posting.Id, ApplicationPlatform.Greenhouse, posting.Url, "resume.docx", AutomationMode.ReviewBeforeSubmit, DateTimeOffset.UtcNow);
        application = await fixture.Applications.TransitionAsync(application.Id, ApplicationWorkflowStatus.Prepared, DateTimeOffset.UtcNow, "prepared");
        application = await fixture.Applications.TransitionAsync(application.Id, ApplicationWorkflowStatus.Approved, DateTimeOffset.UtcNow, "approved");
        application = await fixture.Applications.TransitionAsync(application.Id, ApplicationWorkflowStatus.InProgress, DateTimeOffset.UtcNow, "started");
        application = await fixture.Applications.RecordAutomationResultAsync(application.Id,
            new AutomationResult("run", ApplicationWorkflowStatus.NeedsInput, [], [], [new("Why are you a good fit?", "text", [], true)], []), DateTimeOffset.UtcNow);
        var question = Assert.Single(application.Questions);

        application = await fixture.Applications.SaveQuestionDraftAsync(application.Id, question.Id, "A generated draft");
        var drafted = Assert.Single(application.Questions);
        Assert.False(drafted.IsResolved);
        Assert.Null(drafted.Answer);
        Assert.Equal("A generated draft", drafted.DraftAnswer);

        application = await fixture.Applications.ResolveQuestionAsync(application.Id, question.Id, drafted.DraftAnswer, DateTimeOffset.UtcNow);
        Assert.True(Assert.Single(application.Questions).IsResolved);
    }

    [Fact]
    public async Task PrepareApplication_SelectsTailoredThenMasterResume()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var directory = NewDirectory();
        try
        {
            var master = Path.Combine(directory, "master.docx");
            var tailored = Path.Combine(directory, "tailored.docx");
            await File.WriteAllTextAsync(master, "master");
            await File.WriteAllTextAsync(tailored, "tailored");
            var posting = Job();
            await fixture.Jobs.UpsertAsync(posting, new JobScore(90, []), null, DateTimeOffset.UtcNow);
            await fixture.Careers.SaveResumeAsync(new ResumeArtifact { JobPostingId = null, Kind = ResumeArtifactKind.Master, FileName = "master.docx", FilePath = master, GeneratedAtUtc = DateTimeOffset.UtcNow, CareerProfileVersion = 1 });
            await fixture.Careers.SaveResumeAsync(new ResumeArtifact { JobPostingId = posting.Id, Kind = ResumeArtifactKind.Tailored, FileName = "tailored.docx", FilePath = tailored, GeneratedAtUtc = DateTimeOffset.UtcNow, CareerProfileVersion = 1 });
            var service = new ApplicationService(fixture.Applications, fixture.Jobs, new TestProfileStore(master), fixture.Careers,
                new DeterministicApplicationAnswerDraftingService(), new NoOpAutomator(), new AutomationPolicy(new HashSet<ApplicationPlatform>()), TimeProvider.System);

            var application = await service.PrepareAsync(posting.Id);
            Assert.Equal(tailored, application.ResumePath);
            Assert.Equal(ApplicationWorkflowStatus.Prepared, application.Status);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task PrepareApplication_UpdatesPreparedPackageToNewestTailoredResume()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var directory = NewDirectory();
        try
        {
            var oldResume = Path.Combine(directory, "old.docx");
            var newResume = Path.Combine(directory, "new.docx");
            await File.WriteAllTextAsync(oldResume, "old");
            await File.WriteAllTextAsync(newResume, "new");
            var posting = Job();
            await fixture.Jobs.UpsertAsync(posting, new JobScore(90, []), null, DateTimeOffset.UtcNow);
            var application = await fixture.Applications.CreateAsync(posting.Id, ApplicationPlatform.Greenhouse, posting.Url, oldResume, AutomationMode.ReviewBeforeSubmit, DateTimeOffset.UtcNow);
            await fixture.Applications.TransitionAsync(application.Id, ApplicationWorkflowStatus.Prepared, DateTimeOffset.UtcNow, "prepared");
            await fixture.Careers.SaveResumeAsync(new ResumeArtifact { JobPostingId = posting.Id, Kind = ResumeArtifactKind.Tailored, FileName = "new.docx", FilePath = newResume, GeneratedAtUtc = DateTimeOffset.UtcNow.AddMinutes(1), CareerProfileVersion = 2 });
            var service = new ApplicationService(fixture.Applications, fixture.Jobs, new TestProfileStore(oldResume), fixture.Careers,
                new DeterministicApplicationAnswerDraftingService(), new NoOpAutomator(), new AutomationPolicy(new HashSet<ApplicationPlatform>()), TimeProvider.System);

            application = await service.PrepareAsync(posting.Id);

            Assert.Equal(newResume, application.ResumePath);
            Assert.Equal(ApplicationWorkflowStatus.Prepared, application.Status);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task MarkSubmitted_UpdatesApplicationHistoryAndJobPipeline()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var posting = Job();
        await fixture.Jobs.UpsertAsync(posting, new JobScore(90, []), null, DateTimeOffset.UtcNow);
        var application = await fixture.Applications.CreateAsync(posting.Id, ApplicationPlatform.Greenhouse, posting.Url, "resume.docx", AutomationMode.ReviewBeforeSubmit, DateTimeOffset.UtcNow);
        application = await fixture.Applications.TransitionAsync(application.Id, ApplicationWorkflowStatus.Prepared, DateTimeOffset.UtcNow, "prepared");
        application = await fixture.Applications.TransitionAsync(application.Id, ApplicationWorkflowStatus.Approved, DateTimeOffset.UtcNow, "approved");
        application = await fixture.Applications.TransitionAsync(application.Id, ApplicationWorkflowStatus.InProgress, DateTimeOffset.UtcNow, "started");
        application = await fixture.Applications.RecordAutomationResultAsync(application.Id, new AutomationResult("run", ApplicationWorkflowStatus.ReadyToSubmit, [], [], [], []), DateTimeOffset.UtcNow);
        var service = new ApplicationService(fixture.Applications, fixture.Jobs, new TestProfileStore("resume.docx"), fixture.Careers,
            new DeterministicApplicationAnswerDraftingService(), new NoOpAutomator(), new AutomationPolicy(new HashSet<ApplicationPlatform>()), TimeProvider.System);

        application = await service.MarkSubmittedAsync(application.Id);
        var job = await fixture.Jobs.GetByIdAsync(posting.Id);

        Assert.Equal(ApplicationWorkflowStatus.Submitted, application.Status);
        Assert.NotNull(application.SubmittedAtUtc);
        Assert.Contains(application.Events, value => value.Type == ApplicationEventType.Submitted);
        Assert.Equal(ApplicationStatus.Applied, job!.Posting.Status);
    }

    [Fact]
    public void AnswerDraft_UsesOnlyProfileEvidenceAndRequiresApproval()
    {
        var profile = Profile();
        var draft = new DeterministicApplicationAnswerDraftingService().Draft("Describe relevant experience", Job(), profile, new ApplicantProfile());
        Assert.False(draft.IsApproved);
        Assert.NotEmpty(draft.EvidenceIds);
        ResumeEvidenceValidator.EnsureSupported(profile, draft.EvidenceIds);
    }

    private static CareerProfile Profile()
    {
        var position = new EmploymentExperience
        {
            Employer = "Example Health",
            Title = "Senior Full-Stack Developer",
            StartDate = "2020",
            EndDate = "Present",
            IsCurrent = true,
            Technologies = ["C#", "ASP.NET Core", "Azure", "SQL Server"],
            Source = "fixture.docx",
            Evidence =
            [
                new CareerEvidence { Statement = "Built ASP.NET Core APIs in C# for a healthcare platform hosted in Azure.", Source = "fixture.docx" },
                new CareerEvidence { Statement = "Converted Power BI reports into an Angular dashboard.", Source = "fixture.docx" }
            ]
        };
        return new CareerProfile
        {
            Status = CareerProfileStatus.Approved,
            Headline = "Senior .NET Engineer",
            ProfessionalSummary = "Senior engineer with verified .NET and full-stack experience.",
            TotalExperienceYears = 10,
            EmploymentHistory = [position],
            Skills =
            [
                new CareerSkill { Name = "C#", NormalizedName = "C#", Category = "Languages", DemonstratedByPositionIds = [position.Id], EvidenceStrength = "Demonstrated" },
                new CareerSkill { Name = "ASP.NET Core", NormalizedName = "ASP.NET Core", Category = "Back End", DemonstratedByPositionIds = [position.Id], EvidenceStrength = "Demonstrated" },
                new CareerSkill { Name = "Azure", NormalizedName = "Azure", Category = "Cloud", DemonstratedByPositionIds = [position.Id], EvidenceStrength = "Demonstrated" }
            ]
        };
    }

    private static JobPosting Job() => new()
    {
        Source = "Fixture",
        SourceJobId = "1",
        Title = "Senior C# ASP.NET Core Engineer",
        Company = "Example",
        Url = new Uri("https://example.test/jobs/1"),
        WorkLocationType = WorkLocationType.Remote,
        Skills = ["C#", "ASP.NET Core", "Terraform"],
        Description = "Build C# ASP.NET Core services in Azure using Terraform and Power BI."
    };

    private static string CreateResume(string directory)
    {
        var path = Path.Combine(directory, "resume.docx");
        using var document = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        main.Document = new Document(new Body());
        foreach (var text in new[]
        {
            "Test Candidate", "Senior .NET Engineer", "Remote | candidate@example.com | 555-555-0100 | https://linkedin.example/test",
            "Professional Summary", "Engineer with 10+ years of experience building web applications.",
            "Core Technical Skills", "Languages: C#, JavaScript", "Back-End / APIs: ASP.NET Core, RESTful APIs", "Cloud & DevOps: Azure", "Databases: SQL Server",
            "Professional Experience", "Example Health – Senior .NET Engineer", "Remote | Jan 2020 – Present", "Stack: C#, ASP.NET Core, Azure", "Built ASP.NET Core APIs in C#.",
            "Education", "Example Institute – Computer Programming (2008 – 2010)"
        }) main.Document.Body!.Append(new Paragraph(new Run(new Text(text))));
        main.Document.Save();
        return path;
    }

    private static string ReadDocumentText(string path)
    {
        using var document = WordprocessingDocument.Open(path, false);
        return document.MainDocumentPart!.Document!.Body!.InnerText;
    }

    private static Paragraph LineBreakParagraph(params string[] lines)
    {
        var paragraph = new Paragraph();
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0) paragraph.Append(new Run(new Break()));
            paragraph.Append(new Run(new Text(lines[index])));
        }
        return paragraph;
    }

    private static string NewDirectory()
    {
        var value = Path.Combine(Path.GetTempPath(), $"jobsearch-career-{Guid.NewGuid():N}");
        Directory.CreateDirectory(value);
        return value;
    }

    private sealed class TestProfileStore(string resumePath) : IApplicantProfileStore
    {
        public Task<ApplicantProfile> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ApplicantProfile { LegalFirstName = "Test", LegalLastName = "Candidate", Email = "test@example.com", Phone = "555", ResumePath = resumePath });
        public Task SaveAsync(ApplicantProfile profile, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ProfileValidation Validate(ApplicantProfile profile) => new(File.Exists(profile.ResumePath), []);
    }

    private sealed class NoOpAutomator : IApplicationAutomator
    {
        public Task<AutomationResult> RunAsync(ApplicationAutomationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AutomationResult("none", ApplicationWorkflowStatus.ReadyToSubmit, [], [], [], []));
        public Task<bool> FocusExistingSessionAsync(Guid applicationId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class DatabaseFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly JobSearchDbContext context;
        public EfJobRepository Jobs { get; }
        public EfApplicationRepository Applications { get; }
        public EfCareerProfileRepository Careers { get; }

        private DatabaseFixture(SqliteConnection connection, JobSearchDbContext context)
        {
            this.connection = connection; this.context = context;
            Jobs = new EfJobRepository(context, NullLogger<EfJobRepository>.Instance);
            Applications = new EfApplicationRepository(context);
            Careers = new EfCareerProfileRepository(context);
        }

        public static async Task<DatabaseFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new JobSearchDbContext(new DbContextOptionsBuilder<JobSearchDbContext>().UseSqlite(connection).Options);
            var fixture = new DatabaseFixture(connection, context);
            await fixture.Jobs.InitializeAsync();
            return fixture;
        }

        public async ValueTask DisposeAsync() { await context.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
