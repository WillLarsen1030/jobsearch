using JobSearch.Application.Applications;

namespace JobSearch.Web.Components.Pages;

public sealed class ApplicationQuestionReviewState
{
    private readonly Dictionary<Guid, string> answers = [];
    private readonly Dictionary<Guid, bool> saveForFuture = [];

    public void Synchronize(JobApplication? application)
    {
        if (application is null) return;
        foreach (var question in application.Questions.Where(value => !value.IsResolved))
        {
            answers.TryAdd(question.Id, question.DraftAnswer);
            saveForFuture.TryAdd(question.Id, false);
        }
    }

    public string Answer(PendingApplicationQuestion question) =>
        answers.GetValueOrDefault(question.Id, question.DraftAnswer);

    public void SetAnswer(Guid questionId, string? value) => answers[questionId] = value ?? string.Empty;

    public bool SaveForFuture(Guid questionId) => saveForFuture.GetValueOrDefault(questionId);

    public void SetSaveForFuture(Guid questionId, bool value) => saveForFuture[questionId] = value;

    public bool CanSaveAll(IEnumerable<PendingApplicationQuestion> questions) =>
        questions.All(question => !question.IsRequired || !string.IsNullOrWhiteSpace(Answer(question)));
}
