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
            Assert.Equal(ApplicationWorkflowStatus.ReadyToSubmit, result.Status);
            Assert.Contains("resume", result.FieldsFilled);
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
            Assert.Equal(ApplicationWorkflowStatus.ReadyToSubmit, result.Status);
            Assert.Contains(result.FieldsFilled, value => value.Contains("email"));
            Assert.DoesNotContain(result.FieldsFilled, value => value.Contains("creative"));
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
    private static ApplicantProfile Profile() => new() { LegalFirstName = "Test", LegalLastName = "Applicant", Email = "test@example.com", Phone = "555-0100", LinkedInUrl = "https://linkedin.example/test" };
    private static string CreateTemp() { var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
}
