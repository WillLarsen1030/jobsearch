using JobSearch.Application.Applications;

namespace JobSearch.Tests.Applications;

public sealed class ApplicationModelTests
{
    [Theory]
    [InlineData("https://job-boards.greenhouse.io/acme/jobs/1", ApplicationPlatform.Greenhouse)]
    [InlineData("https://jobs.lever.co/acme/1", ApplicationPlatform.Lever)]
    [InlineData("https://acme.example/apply", ApplicationPlatform.Generic)]
    public void PlatformDetection_UsesApplicationUrl(string url, ApplicationPlatform expected) =>
        Assert.Equal(expected, ApplicationPlatformDetector.Detect(new Uri(url)));

    [Fact]
    public void QuestionMatcher_NormalizesAliasesAndHonorsConfidenceThreshold()
    {
        var answer = new ApplicationAnswer(Guid.NewGuid(), "Will you require employment sponsorship?",
            QuestionMatcher.Normalize("Will you require employment sponsorship?"), "No", AnswerType.YesNo, 1m, false, "", DateTimeOffset.UtcNow);

        var match = QuestionMatcher.Match("Do you require employment sponsorship?", [answer]);

        Assert.True(match.IsTrusted());
        Assert.Equal("No", match.Answer!.Answer);
    }

    [Fact]
    public void QuestionMatcher_RequiresHumanWhenConfidenceIsLowOrConfirmationRequired()
    {
        var low = new ApplicationAnswer(Guid.NewGuid(), "Are you authorized to work in the US?", "authorized work us", "Yes", AnswerType.YesNo, .5m, false, "", DateTimeOffset.UtcNow);
        var sensitive = low with { Confidence = 1m, AlwaysRequireConfirmation = true };
        Assert.False(QuestionMatcher.Match("Are you authorized to work in the US?", [low]).IsTrusted());
        Assert.False(QuestionMatcher.Match("Are you authorized to work in the US?", [sensitive]).IsTrusted());
    }

    [Fact]
    public void StateMachine_RejectsSkippingApproval()
    {
        Assert.True(ApplicationWorkflow.CanTransition(ApplicationWorkflowStatus.Prepared, ApplicationWorkflowStatus.Approved));
        Assert.Throws<InvalidOperationException>(() => ApplicationWorkflow.EnsureTransition(ApplicationWorkflowStatus.Prepared, ApplicationWorkflowStatus.InProgress));
    }

    [Fact]
    public void AutoSubmit_RequiresGlobalPlatformAndPerApplicationApproval()
    {
        var disabled = new AutomationPolicy(new HashSet<ApplicationPlatform>());
        var enabled = new AutomationPolicy(new HashSet<ApplicationPlatform> { ApplicationPlatform.Greenhouse });
        Assert.Equal(AutomationMode.ReviewBeforeSubmit, ApplicationService.ResolveAutomationMode(AutomationMode.AutoSubmit, ApplicationPlatform.Greenhouse, true, disabled));
        Assert.Equal(AutomationMode.ReviewBeforeSubmit, ApplicationService.ResolveAutomationMode(AutomationMode.AutoSubmit, ApplicationPlatform.Greenhouse, false, enabled));
        Assert.Equal(AutomationMode.AutoSubmit, ApplicationService.ResolveAutomationMode(AutomationMode.AutoSubmit, ApplicationPlatform.Greenhouse, true, enabled));
    }
}
