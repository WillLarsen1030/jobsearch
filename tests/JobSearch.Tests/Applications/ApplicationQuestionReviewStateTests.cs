using JobSearch.Application.Applications;
using JobSearch.Web.Components.Pages;

namespace JobSearch.Tests.Applications;

public sealed class ApplicationQuestionReviewStateTests
{
    [Fact]
    public void Synchronize_NewLongAndUnnamedQuestions_AreSafeToRender()
    {
        var longQuestion = new PendingApplicationQuestion(
            Guid.NewGuid(),
            "Based on what you know about Livefront/Zeal right now, why do you want to be a part of our team?",
            "why livefront",
            "textarea",
            ["One", "Two"],
            true,
            null,
            false,
            "Verified draft");
        var unnamed = new PendingApplicationQuestion(
            Guid.NewGuid(), "Unnamed required field", "unnamed required field", "input", [], true, null, false);
        var application = new JobApplication(
            Guid.NewGuid(), Guid.NewGuid(), "Engineer", "Livefront", ApplicationWorkflowStatus.NeedsInput,
            DateTimeOffset.UtcNow, null, null, null, null, null, ApplicationPlatform.Greenhouse,
            new Uri("https://job-boards.greenhouse.io/livefront/jobs/1"), "resume.docx",
            AutomationMode.ReviewBeforeSubmit, false, "", "run", [], [], [longQuestion, unnamed], []);
        var state = new ApplicationQuestionReviewState();

        state.Synchronize(application);

        Assert.Equal("Verified draft", state.Answer(longQuestion));
        Assert.Equal(string.Empty, state.Answer(unnamed));
        Assert.False(state.SaveForFuture(longQuestion.Id));
        state.SetAnswer(unnamed.Id, "N/A");
        Assert.Equal("N/A", state.Answer(unnamed));
    }

    [Fact]
    public void CanSaveAll_RequiresAnswersOnlyForRequiredQuestions()
    {
        var required = new PendingApplicationQuestion(Guid.NewGuid(), "Required", "required", "text", [], true, null, false);
        var optional = new PendingApplicationQuestion(Guid.NewGuid(), "Optional", "optional", "text", [], false, null, false);
        var state = new ApplicationQuestionReviewState();

        Assert.False(state.CanSaveAll([required, optional]));
        state.SetAnswer(required.Id, "Approved answer");
        Assert.True(state.CanSaveAll([required, optional]));
    }
}
