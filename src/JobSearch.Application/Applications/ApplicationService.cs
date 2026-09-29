using JobSearch.Application.Persistence;
using JobSearch.Application.Career;

namespace JobSearch.Application.Applications;

public sealed record AutomationPolicy(IReadOnlySet<ApplicationPlatform> AutoSubmitEnabledPlatforms)
{
    public bool AllowsAutoSubmit(ApplicationPlatform platform) => AutoSubmitEnabledPlatforms.Contains(platform);
}

public sealed class ApplicationService(
    IApplicationRepository applications,
    IJobRepository jobs,
    IApplicantProfileStore profiles,
    ICareerProfileRepository careers,
    IApplicationAnswerDraftingService answerDrafting,
    IApplicationAutomator automator,
    AutomationPolicy policy,
    TimeProvider timeProvider)
{
    public async Task<JobApplication> PrepareAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await jobs.GetByIdAsync(jobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Job '{jobId}' was not found.");
        var profile = await profiles.GetAsync(cancellationToken);
        var selectedResume = await SelectResumeAsync(jobId, profile.ResumePath, cancellationToken);
        var application = await applications.CreateAsync(jobId, ApplicationPlatformDetector.Detect(job.Posting.Url),
            job.Posting.Url, selectedResume, AutomationMode.ReviewBeforeSubmit, timeProvider.GetUtcNow(), cancellationToken);
        if (!string.Equals(application.ResumePath, selectedResume, StringComparison.OrdinalIgnoreCase))
            application = await applications.UpdateResumeAsync(application.Id, selectedResume, timeProvider.GetUtcNow(), cancellationToken);
        if (application.Status != ApplicationWorkflowStatus.Draft) return application;
        if (!File.Exists(selectedResume)) return application;
        return await applications.TransitionAsync(application.Id, ApplicationWorkflowStatus.Prepared, timeProvider.GetUtcNow(),
            "Application package prepared and the configured resume was verified.", cancellationToken);
    }

    public Task<JobApplication> ApproveAsync(Guid id, CancellationToken cancellationToken = default) =>
        applications.TransitionAsync(id, ApplicationWorkflowStatus.Approved, timeProvider.GetUtcNow(),
            "Application package approved by the user.", cancellationToken);

    public async Task<JobApplication> RunAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var application = await applications.GetAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Application '{id}' was not found.");
        var profile = await profiles.GetAsync(cancellationToken);
        var validation = profiles.Validate(profile);
        if (!validation.ResumeExists) throw new InvalidOperationException("The configured resume does not exist.");

        var mode = ResolveAutomationMode(application.AutomationMode, application.Platform, application.AutoSubmitApproved, policy);

        var bank = (await applications.GetAnswersAsync(cancellationToken)).ToList();
        bank.AddRange(application.Questions.Where(question => question.IsResolved && !string.IsNullOrWhiteSpace(question.Answer))
            .Select(question => new ApplicationAnswer(Guid.NewGuid(), question.Question, question.NormalizedQuestion,
                question.Answer!, AnswerType.Text, 1m, false, "Approved for this application.", timeProvider.GetUtcNow())));

        application = await applications.TransitionAsync(id, ApplicationWorkflowStatus.InProgress, timeProvider.GetUtcNow(),
            application.Status == ApplicationWorkflowStatus.NeedsInput ? "Application automation resumed." : "Application automation started.", cancellationToken);
        var result = await automator.RunAsync(new ApplicationAutomationRequest(application.Id, application.ApplicationUrl,
            application.Platform, mode, application.AutoSubmitApproved, application.ResumePath, profile, bank), cancellationToken);
        return await applications.RecordAutomationResultAsync(id, result, timeProvider.GetUtcNow(), cancellationToken);
    }

    public async Task<JobApplication> AnswerQuestionAsync(Guid applicationId, Guid questionId, string answer, bool saveForFuture, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(answer)) throw new ArgumentException("An answer is required.", nameof(answer));
        var application = await applications.ResolveQuestionAsync(applicationId, questionId, answer, timeProvider.GetUtcNow(), cancellationToken);
        var question = application.Questions.Single(value => value.Id == questionId);
        if (saveForFuture)
        {
            await applications.SaveAnswerAsync(new ApplicationAnswer(Guid.NewGuid(), question.Question,
                question.NormalizedQuestion, answer, AnswerType.Text, 1m, false,
                "Saved from an application Needs Input review.", timeProvider.GetUtcNow()), cancellationToken);
        }
        return application;
    }

    public Task<JobApplication> SaveQuestionDraftAsync(Guid applicationId, Guid questionId, string draft, CancellationToken cancellationToken = default) =>
        applications.SaveQuestionDraftAsync(applicationId, questionId, draft, cancellationToken);

    public async Task<bool> FocusBrowserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var application = await applications.GetAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Application '{id}' was not found.");
        if (application.Status != ApplicationWorkflowStatus.ReadyToSubmit)
            throw new InvalidOperationException("The employer browser can only be opened for final review after filling is complete.");
        return await automator.FocusExistingSessionAsync(id, cancellationToken);
    }

    public async Task<JobApplication> DraftQuestionAsync(Guid applicationId, Guid questionId, CancellationToken cancellationToken = default)
    {
        var application = await applications.GetAsync(applicationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Application '{applicationId}' was not found.");
        var question = application.Questions.SingleOrDefault(value => value.Id == questionId)
            ?? throw new KeyNotFoundException($"Question '{questionId}' was not found.");
        var job = await jobs.GetByIdAsync(application.JobPostingId, cancellationToken)
            ?? throw new KeyNotFoundException($"Job '{application.JobPostingId}' was not found.");
        var career = await careers.GetProfileAsync(cancellationToken)
            ?? throw new InvalidOperationException("An approved CareerProfile is required to draft an answer.");
        var draft = answerDrafting.Draft(question.Question, job.Posting, career, await profiles.GetAsync(cancellationToken));
        return await applications.SaveQuestionDraftAsync(applicationId, questionId, draft.Draft, cancellationToken);
    }

    public async Task<JobApplication> MarkSubmittedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var application = await applications.TransitionAsync(id, ApplicationWorkflowStatus.Submitted, timeProvider.GetUtcNow(),
            "Application was marked submitted by the user.", cancellationToken);
        await jobs.ChangeStatusAsync(application.JobPostingId, JobSearch.Domain.JobPostings.ApplicationStatus.Applied,
            "Marked submitted from the assisted application workflow.", timeProvider.GetUtcNow(), cancellationToken);
        return application;
    }

    public Task<JobApplication> WithdrawAsync(Guid id, CancellationToken cancellationToken = default) =>
        applications.TransitionAsync(id, ApplicationWorkflowStatus.Withdrawn, timeProvider.GetUtcNow(),
            "Application was withdrawn by the user.", cancellationToken);

    public static AutomationMode ResolveAutomationMode(AutomationMode requested, ApplicationPlatform platform, bool applicationApproved, AutomationPolicy policy) =>
        requested == AutomationMode.AutoSubmit && (!applicationApproved || !policy.AllowsAutoSubmit(platform))
            ? AutomationMode.ReviewBeforeSubmit
            : requested;

    private async Task<string> SelectResumeAsync(Guid jobId, string fallback, CancellationToken cancellationToken)
    {
        var tailored = await careers.GetResumeAsync(jobId, ResumeArtifactKind.Tailored, cancellationToken);
        if (tailored is not null && File.Exists(tailored.FilePath)) return tailored.FilePath;
        var master = await careers.GetResumeAsync(null, ResumeArtifactKind.Master, cancellationToken);
        return master is not null && File.Exists(master.FilePath) ? master.FilePath : fallback;
    }
}
