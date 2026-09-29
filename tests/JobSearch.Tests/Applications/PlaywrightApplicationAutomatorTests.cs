using JobSearch.Application.Applications;
using JobSearch.Infrastructure.Applications;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobSearch.Tests.Applications;

public sealed class PlaywrightApplicationAutomatorTests
{
    [Fact]
    public async Task GreenhouseFixture_FillsTrustedFieldsAndStopsBeforeSubmit()
    {
        var temp = CreateTemp();
        try
        {
            var resume = Path.Combine(temp, "resume.pdf"); await File.WriteAllTextAsync(resume, "fixture");
            await using var automator = CreateAutomator(temp);
            var answer = new ApplicationAnswer(Guid.NewGuid(), "Will you require employment sponsorship?", QuestionMatcher.Normalize("Will you require employment sponsorship?"), "No", AnswerType.YesNo, 1m, false, "", DateTimeOffset.UtcNow);
            var result = await automator.RunAsync(Request("greenhouse.html", ApplicationPlatform.Greenhouse, resume, [answer]));
            Assert.True(result.Status == ApplicationWorkflowStatus.ReadyToSubmit,
                $"Unknown: {string.Join(" | ", result.UnknownQuestions.Select(value => value.Question))}; warnings: {string.Join(" | ", result.Warnings)}");
            Assert.Contains(result.FieldsFilled, value => value == "resume: resume.pdf");
            Assert.Contains(result.FieldsFilled, value => value.Contains("first name"));
            Assert.Empty(result.UnknownQuestions);
            Assert.False(result.Submitted);
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public async Task LeverFixture_UnknownRequiredQuestionProducesNeedsInput()
    {
        var temp = CreateTemp();
        try
        {
            var resume = Path.Combine(temp, "resume.pdf"); await File.WriteAllTextAsync(resume, "fixture");
            await using var automator = CreateAutomator(temp);
            var result = await automator.RunAsync(Request("lever.html", ApplicationPlatform.Lever, resume, []));
            Assert.Equal(ApplicationWorkflowStatus.NeedsInput, result.Status);
            Assert.Contains(result.UnknownQuestions, value => value.Question.Contains("favorite database"));
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public async Task GenericFixture_FillsOnlyHighConfidenceObviousFields()
    {
        var temp = CreateTemp();
        try
        {
            var resume = Path.Combine(temp, "resume.pdf"); await File.WriteAllTextAsync(resume, "fixture");
            await using var automator = CreateAutomator(temp);
            var result = await automator.RunAsync(Request("generic.html", ApplicationPlatform.Generic, resume, []));
            Assert.True(result.Status == ApplicationWorkflowStatus.ReadyToSubmit,
                $"Unknown: {string.Join(" | ", result.UnknownQuestions.Select(value => value.Question))}; warnings: {string.Join(" | ", result.Warnings)}");
            Assert.Contains(result.FieldsFilled, value => value.Contains("email"));
            Assert.DoesNotContain(result.FieldsFilled, value => value.Contains("creative"));
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public async Task GreenhouseHiddenFileInput_UploadsSelectedTailoredResumeAndVerifiesFilename()
    {
        var temp = CreateTemp();
        try
        {
            const string fileName = "William-Larsen-Livefront-tailored.docx";
            var resume = Path.Combine(temp, fileName);
            await File.WriteAllTextAsync(resume, "fixture");
            await using var automator = CreateAutomator(temp);

            var request = Request("greenhouse-modern.html", ApplicationPlatform.Greenhouse, resume, LivefrontAnswers());
            var result = await automator.RunAsync(request);

            Assert.True(result.Status == ApplicationWorkflowStatus.ReadyToSubmit,
                $"Unknown: {string.Join(" | ", result.UnknownQuestions.Select(value => value.Question))}; warnings: {string.Join(" | ", result.Warnings)}");
            Assert.Contains($"resume: {fileName}", result.FieldsFilled);
            Assert.DoesNotContain(result.Warnings, value => value.Contains("Resume upload", StringComparison.OrdinalIgnoreCase));
            Assert.True(await automator.FocusExistingSessionAsync(request.ApplicationId));
            Assert.False(await automator.FocusExistingSessionAsync(Guid.NewGuid()));
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public async Task GreenhouseCustomControls_SelectVerifiedValuesAndSuppressValidationCompanions()
    {
        var temp = CreateTemp();
        try
        {
            var resume = Path.Combine(temp, "tailored.docx");
            await File.WriteAllTextAsync(resume, "fixture");
            await using var automator = CreateAutomator(temp);

            var result = await automator.RunAsync(Request("greenhouse-modern.html", ApplicationPlatform.Greenhouse, resume, LivefrontAnswers()));

            Assert.True(result.Status == ApplicationWorkflowStatus.ReadyToSubmit,
                $"Unknown: {string.Join(" | ", result.UnknownQuestions.Select(value => value.Question))}; warnings: {string.Join(" | ", result.Warnings)}");
            Assert.Contains("country", result.FieldsFilled);
            Assert.Contains("candidate location", result.FieldsFilled);
            Assert.Contains(result.AnswersUsed, value => value.Contains("work authorization status", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.UnknownQuestions, value => value.Question == "Unnamed required field");
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public async Task GreenhouseReferralNA_HidesOptionalReferrerAndNeverActivatesSubmit()
    {
        var temp = CreateTemp();
        try
        {
            var resume = Path.Combine(temp, "tailored.docx");
            await File.WriteAllTextAsync(resume, "fixture");
            await using var automator = CreateAutomator(temp);

            var result = await automator.RunAsync(Request("greenhouse-modern.html", ApplicationPlatform.Greenhouse, resume, LivefrontAnswers()));

            Assert.DoesNotContain(result.UnknownQuestions, value => value.Question.Contains("name of the person", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.AnswersUsed, value => value.Contains("referred to Livefront", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("Greenhouse submit controls were not activated.", result.Warnings);
            Assert.False(result.Submitted);
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public async Task GreenhouseResumeUpload_RejectsDisallowedSelectedFileType()
    {
        var temp = CreateTemp();
        try
        {
            var resume = Path.Combine(temp, "tailored.pdf");
            await File.WriteAllTextAsync(resume, "fixture");
            await using var automator = CreateAutomator(temp);

            var result = await automator.RunAsync(Request("greenhouse-modern.html", ApplicationPlatform.Greenhouse, resume, LivefrontAnswers()));

            Assert.Equal(ApplicationWorkflowStatus.NeedsInput, result.Status);
            Assert.Contains(result.Warnings, value => value.Contains(".pdf is not allowed", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.UnknownQuestions, value => value.Question.Contains("Resume", StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public async Task Failure_CapturesScreenshotPathAndDoesNotSubmit()
    {
        var temp = CreateTemp();
        try
        {
            await using var automator = CreateAutomator(temp);
            var request = new ApplicationAutomationRequest(Guid.NewGuid(), new Uri("file:///definitely-not-present.html"), ApplicationPlatform.Generic, AutomationMode.ReviewBeforeSubmit, false, "missing.pdf", Profile(), []);
            var result = await automator.RunAsync(request);
            Assert.Equal(ApplicationWorkflowStatus.Failed, result.Status);
            Assert.False(result.Submitted);
            Assert.NotNull(result.ScreenshotPath);
            Assert.True(File.Exists(result.ScreenshotPath));
        }
        finally { Directory.Delete(temp, true); }
    }

    private static PlaywrightApplicationAutomator CreateAutomator(string artifacts) => new(
        [new GreenhouseApplicationHandler(), new LeverApplicationHandler(), new GenericApplicationHandler()],
        new BrowserAutomationOptions(true, artifacts), NullLogger<PlaywrightApplicationAutomator>.Instance);
    private static ApplicationAutomationRequest Request(string fixture, ApplicationPlatform platform, string resume, IReadOnlyList<ApplicationAnswer> answers) =>
        new(Guid.NewGuid(), new Uri(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Applications", fixture)), platform, AutomationMode.ReviewBeforeSubmit, false, resume, Profile(), answers);
    private static ApplicantProfile Profile() => new()
    {
        PreferredName = "Tester",
        LegalFirstName = "Test",
        LegalLastName = "Applicant",
        Email = "test@example.com",
        Phone = "555-0100",
        City = "Gallatin",
        State = "Tennessee",
        Country = "United States",
        LinkedInUrl = "https://linkedin.example/test"
    };
    private static IReadOnlyList<ApplicationAnswer> LivefrontAnswers()
    {
        var now = DateTimeOffset.UtcNow;
        return
        [
            Answer("To be considered for this role, you must be legally authorized to work in the United States without the need for employer sponsorship. Please select your current work authorization status:", "U.S. Citizen", now),
            Answer("Based on what you know about Livefront/Zeal right now, why do you want to be a part of our team?", "Verified reason", now),
            Answer("If you were referred to Livefront/Zeal, tell us how.", "N/A", now)
        ];
    }
    private static ApplicationAnswer Answer(string question, string answer, DateTimeOffset now) =>
        new(Guid.NewGuid(), question, QuestionMatcher.Normalize(question), answer, AnswerType.Text, 1m, false, "test", now);
    private static string CreateTemp() { var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
}
