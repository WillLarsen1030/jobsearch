using JobSearch.Application.Applications;

namespace JobSearch.Tests.Applications;

public sealed class ApplicationGuidanceTests
{
    private readonly ApplicationGuidanceService guidance = new();

    [Theory]
    [InlineData(ApplicationWorkflowStatus.Draft, ApplicationActionTarget.Prepare, "Finish Preparing")]
    [InlineData(ApplicationWorkflowStatus.Prepared, ApplicationActionTarget.Approve, "Review Application")]
    [InlineData(ApplicationWorkflowStatus.Approved, ApplicationActionTarget.RunAutomation, "Fill Application")]
    [InlineData(ApplicationWorkflowStatus.InProgress, ApplicationActionTarget.None, "Continue Application")]
    [InlineData(ApplicationWorkflowStatus.NeedsInput, ApplicationActionTarget.AnswerQuestions, "Answer Required Questions")]
    [InlineData(ApplicationWorkflowStatus.ReadyToSubmit, ApplicationActionTarget.FocusBrowser, "Open Application for Final Review")]
    [InlineData(ApplicationWorkflowStatus.Submitted, ApplicationActionTarget.None, "Submitted")]
    [InlineData(ApplicationWorkflowStatus.Failed, ApplicationActionTarget.None, "Review Issue")]
    [InlineData(ApplicationWorkflowStatus.Withdrawn, ApplicationActionTarget.None, "No Action Required")]
    public void NextAction_MapsEverySupportedState(ApplicationWorkflowStatus status, ApplicationActionTarget target, string label)
    {
        var action = guidance.Resolve(status, 2);

        Assert.Equal(target, action.Target);
        Assert.Equal(label, action.ButtonLabel);
        Assert.Equal(status == ApplicationWorkflowStatus.Approved, action.StartsAutomation);
    }

    [Fact]
    public void NeedsInput_ActionExplainsQuestionCountAndRequiresAttention()
    {
        var action = guidance.Resolve(ApplicationWorkflowStatus.NeedsInput, 2);

        Assert.True(action.RequiresAttention);
        Assert.Contains("2 employer questions", action.Explanation);
    }

    [Fact]
    public void WorkflowProgress_MapsAttentionWithinFillStage()
    {
        var stages = guidance.Progress(ApplicationWorkflowStatus.NeedsInput);

        Assert.Equal(6, stages.Count);
        Assert.All(stages.Take(3), stage => Assert.True(stage.IsComplete));
        var current = Assert.Single(stages, stage => stage.IsCurrent);
        Assert.Equal(ApplicationWorkflowStage.Fill, current.Stage);
        Assert.True(current.RequiresAttention);
        Assert.False(stages.Single(stage => stage.Stage == ApplicationWorkflowStage.Submitted).IsComplete);
    }

    [Fact]
    public void Dashboard_CountsAttentionAndSelectsHighestPriorityApplication()
    {
        var now = DateTimeOffset.UtcNow;
        var prepared = Summary(ApplicationWorkflowStatus.Prepared, now.AddHours(-1));
        var ready = Summary(ApplicationWorkflowStatus.ReadyToSubmit, now.AddHours(-2));
        var input = Summary(ApplicationWorkflowStatus.NeedsInput, now.AddDays(-1), 2);
        var submitted = Summary(ApplicationWorkflowStatus.Submitted, now.AddDays(-3));

        var dashboard = guidance.Dashboard([prepared, ready, submitted, input], now);

        Assert.Equal(1, dashboard.NeedsReview);
        Assert.Equal(1, dashboard.NeedsInput);
        Assert.Equal(1, dashboard.ReadyToSubmit);
        Assert.Equal(1, dashboard.SubmittedThisWeek);
        Assert.Equal(input.Id, dashboard.NextApplication!.Id);
    }

    [Fact]
    public void ReadyReview_ReportsResumeAnswersOptionalPoliciesAndMeaningfulWarnings()
    {
        var question = new PendingApplicationQuestion(Guid.NewGuid(), "Why this team?", "why this team", "textarea", [], true, "Because it fits.", true);
        var application = Application(ApplicationWorkflowStatus.ReadyToSubmit, [question],
            ["resume: tailored.docx", "email"], ["Greenhouse submit controls were not activated.", "Review the unusual location value."]);
        var profile = new ApplicantProfile { LegalFirstName = "Test", LegalLastName = "Candidate", Email = "test@example.com", Phone = "5551234567", City = "Gallatin", State = "Tennessee", Country = "United States" };

        var review = guidance.ReadyReview(application, profile);

        Assert.True(review.ResumeUploaded);
        Assert.Equal("tailored.docx", review.ResumeFileName);
        Assert.Equal("Because it fits.", Assert.Single(review.Answers).Answer);
        Assert.Contains(review.OptionalFieldsLeftBlank, value => value.Contains("EEO"));
        Assert.Contains(review.OptionalFieldsLeftBlank, value => value.Contains("GitHub"));
        Assert.DoesNotContain(review.Warnings, value => value.Contains("submit controls"));
        Assert.Contains("unusual location", Assert.Single(review.Warnings));
        Assert.Equal("t***@example.com", review.Contact.Email);
        Assert.Equal("Ending in 4567", review.Contact.Phone);
    }

    [Fact]
    public void AutoSubmitPolicy_RemainsDisabledWithoutExplicitPlatformApproval()
    {
        var mode = ApplicationService.ResolveAutomationMode(AutomationMode.AutoSubmit, ApplicationPlatform.Greenhouse, false,
            new AutomationPolicy(new HashSet<ApplicationPlatform>()));

        Assert.Equal(AutomationMode.ReviewBeforeSubmit, mode);
    }

    private static ApplicationSummary Summary(ApplicationWorkflowStatus status, DateTimeOffset updated, int unanswered = 0) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Senior Engineer", "Example", status, ApplicationPlatform.Greenhouse,
            AutomationMode.ReviewBeforeSubmit, updated.AddDays(-1), updated, unanswered, 88, "Greenhouse");

    private static JobApplication Application(ApplicationWorkflowStatus status, IReadOnlyList<PendingApplicationQuestion> questions,
        IReadOnlyList<string> fields, IReadOnlyList<string> warnings) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Senior Engineer", "Example", status, DateTimeOffset.UtcNow, null, null, null,
            null, null, ApplicationPlatform.Greenhouse, new Uri("https://job-boards.greenhouse.io/example/jobs/1"),
            "C:\\resumes\\tailored.docx", AutomationMode.ReviewBeforeSubmit, false, "", "run", fields, warnings, questions, []);
}
