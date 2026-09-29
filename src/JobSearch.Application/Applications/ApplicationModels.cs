namespace JobSearch.Application.Applications;

public enum ApplicationWorkflowStatus
{
    Draft,
    Prepared,
    Approved,
    InProgress,
    NeedsInput,
    ReadyToSubmit,
    Submitted,
    Failed,
    Withdrawn
}

public enum AutomationMode
{
    Manual,
    FillOnly,
    ReviewBeforeSubmit,
    AutoSubmit
}

public enum ApplicationPlatform
{
    Generic,
    Greenhouse,
    Lever
}

public enum ApplicationEventType
{
    Created,
    Prepared,
    Approved,
    AutomationStarted,
    FieldFilled,
    AnswerUsed,
    NeedsInput,
    ReadyToSubmit,
    Submitted,
    Failed,
    Resumed,
    Withdrawn,
    Warning
}

public enum AnswerType
{
    Text,
    YesNo,
    Number,
    Choice
}

public sealed record ApplicationEvent(
    Guid Id,
    ApplicationEventType Type,
    DateTimeOffset OccurredAtUtc,
    string Message);

public sealed record PendingApplicationQuestion(
    Guid Id,
    string Question,
    string NormalizedQuestion,
    string FieldType,
    IReadOnlyList<string> Options,
    bool IsRequired,
    string? Answer,
    bool IsResolved,
    string DraftAnswer = "");

public sealed record JobApplication(
    Guid Id,
    Guid JobPostingId,
    string JobTitle,
    string Company,
    ApplicationWorkflowStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PreparedAtUtc,
    DateTimeOffset? ApprovedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    ApplicationPlatform Platform,
    Uri ApplicationUrl,
    string ResumePath,
    AutomationMode AutomationMode,
    bool AutoSubmitApproved,
    string FailureReason,
    string BrowserRunId,
    IReadOnlyList<string> FieldsFilled,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<PendingApplicationQuestion> Questions,
    IReadOnlyList<ApplicationEvent> Events);

public sealed record ApplicationSummary(
    Guid Id,
    Guid JobPostingId,
    string JobTitle,
    string Company,
    ApplicationWorkflowStatus Status,
    ApplicationPlatform Platform,
    AutomationMode AutomationMode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int UnansweredQuestions,
    int MatchScore = 0,
    string Source = "");

public sealed record ApplicationAnswer(
    Guid Id,
    string Pattern,
    string NormalizedPattern,
    string Answer,
    AnswerType AnswerType,
    decimal Confidence,
    bool AlwaysRequireConfirmation,
    string Notes,
    DateTimeOffset LastConfirmedAtUtc);

public sealed record AnswerMatch(ApplicationAnswer? Answer, decimal Confidence)
{
    public bool IsTrusted(decimal threshold = 0.85m) =>
        Answer is not null && Confidence >= threshold && !Answer.AlwaysRequireConfirmation;
}
