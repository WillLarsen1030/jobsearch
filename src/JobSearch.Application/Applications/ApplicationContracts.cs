namespace JobSearch.Application.Applications;

public interface IApplicationRepository
{
    Task<JobApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<JobApplication?> GetForJobAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApplicationSummary>> ListAsync(CancellationToken cancellationToken = default);
    Task<JobApplication> CreateAsync(Guid jobId, ApplicationPlatform platform, Uri url, string resumePath, AutomationMode mode, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<JobApplication> TransitionAsync(Guid id, ApplicationWorkflowStatus status, DateTimeOffset now, string message, CancellationToken cancellationToken = default);
    Task<JobApplication> RecordAutomationResultAsync(Guid id, AutomationResult result, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<JobApplication> ResolveQuestionAsync(Guid applicationId, Guid questionId, string answer, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<JobApplication> SaveQuestionDraftAsync(Guid applicationId, Guid questionId, string draft, CancellationToken cancellationToken = default);
    Task<JobApplication> UpdateResumeAsync(Guid id, string resumePath, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<JobApplication> SetAutoSubmitApprovalAsync(Guid id, bool approved, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApplicationAnswer>> GetAnswersAsync(CancellationToken cancellationToken = default);
    Task<ApplicationAnswer> SaveAnswerAsync(ApplicationAnswer answer, CancellationToken cancellationToken = default);
    Task DeleteAnswerAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IApplicationAutomator
{
    Task<AutomationResult> RunAsync(ApplicationAutomationRequest request, CancellationToken cancellationToken = default);
    Task<bool> FocusExistingSessionAsync(Guid applicationId, CancellationToken cancellationToken = default);
}

public interface IApplicationPlatformHandler
{
    ApplicationPlatform Platform { get; }
    bool CanHandle(Uri url);
    Task<AutomationResult> FillAsync(ApplicationAutomationContext context, CancellationToken cancellationToken);
}

public sealed record ApplicationAutomationRequest(
    Guid ApplicationId,
    Uri Url,
    ApplicationPlatform Platform,
    AutomationMode Mode,
    bool AutoSubmitApproved,
    string ResumePath,
    ApplicantProfile Profile,
    IReadOnlyList<ApplicationAnswer> Answers);

public sealed record ApplicationAutomationContext(
    Guid ApplicationId,
    Uri Url,
    AutomationMode Mode,
    bool AutoSubmitApproved,
    string ResumePath,
    ApplicantProfile Profile,
    IReadOnlyList<ApplicationAnswer> Answers,
    object Page,
    string RunId,
    string ArtifactDirectory);

public sealed record DetectedQuestion(
    string Question,
    string FieldType,
    IReadOnlyList<string> Options,
    bool IsRequired);

public sealed record AutomationResult(
    string RunId,
    ApplicationWorkflowStatus Status,
    IReadOnlyList<string> FieldsFilled,
    IReadOnlyList<string> AnswersUsed,
    IReadOnlyList<DetectedQuestion> UnknownQuestions,
    IReadOnlyList<string> Warnings,
    string? FailureReason = null,
    string? ScreenshotPath = null,
    bool Submitted = false);
