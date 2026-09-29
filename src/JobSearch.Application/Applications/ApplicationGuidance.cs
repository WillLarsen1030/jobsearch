namespace JobSearch.Application.Applications;

public enum ApplicationActionTarget
{
    None,
    Prepare,
    Approve,
    RunAutomation,
    AnswerQuestions,
    FocusBrowser,
    ConfirmSubmitted
}

public enum ApplicationWorkflowStage
{
    Job,
    Prepare,
    Review,
    Fill,
    Ready,
    Submitted
}

public sealed record ApplicationNextAction(
    string Title,
    string Explanation,
    string ButtonLabel,
    ApplicationActionTarget Target,
    bool RequiresAttention,
    bool StartsAutomation,
    bool IsInformationalOnly);

public sealed record ApplicationStageState(
    ApplicationWorkflowStage Stage,
    string Label,
    bool IsComplete,
    bool IsCurrent,
    bool RequiresAttention);

public sealed record ApplicationDashboard(
    int NeedsReview,
    int NeedsInput,
    int ReadyToSubmit,
    int SubmittedThisWeek,
    ApplicationSummary? NextApplication);

public sealed record ApplicationContactSummary(string Name, string Email, string Phone, string Location, bool LinkedInAvailable);

public sealed record ReadyToSubmitReview(
    string Company,
    string JobTitle,
    string Source,
    string ResumeFileName,
    bool ResumeUploaded,
    ApplicationContactSummary Contact,
    IReadOnlyList<PendingApplicationQuestion> Answers,
    IReadOnlyList<string> OptionalFieldsLeftBlank,
    IReadOnlyList<string> Warnings);

public sealed class ApplicationGuidanceService
{
    private static readonly ApplicationWorkflowStage[] Stages = Enum.GetValues<ApplicationWorkflowStage>();

    public ApplicationNextAction Resolve(ApplicationWorkflowStatus status, int unansweredQuestions = 0) => status switch
    {
        ApplicationWorkflowStatus.Draft => new("Finish preparing", "Verify the application package and selected resume before review.", "Finish Preparing", ApplicationActionTarget.Prepare, true, false, false),
        ApplicationWorkflowStatus.Prepared => new("Review application", "Review the prepared package and approve it before browser filling begins.", "Review Application", ApplicationActionTarget.Approve, true, false, false),
        ApplicationWorkflowStatus.Approved => new("Fill application", "The package is approved and ready for guided browser filling.", "Fill Application", ApplicationActionTarget.RunAutomation, true, true, false),
        ApplicationWorkflowStatus.InProgress => new("Continue application", "Browser filling is currently in progress. Keep this page open while it completes.", "Continue Application", ApplicationActionTarget.None, false, false, true),
        ApplicationWorkflowStatus.NeedsInput => new("Answer required questions", $"Answer {Math.Max(1, unansweredQuestions)} employer question{(unansweredQuestions == 1 ? string.Empty : "s")} before filling can continue.", "Answer Required Questions", ApplicationActionTarget.AnswerQuestions, true, false, false),
        ApplicationWorkflowStatus.ReadyToSubmit => new("Review and submit", "Automation finished filling this application. Review the employer form before submitting it yourself.", "Open Application for Final Review", ApplicationActionTarget.FocusBrowser, true, false, false),
        ApplicationWorkflowStatus.Submitted => new("Submitted", "You confirmed that this application was submitted on the employer's website.", "Submitted", ApplicationActionTarget.None, false, false, true),
        ApplicationWorkflowStatus.Failed => new("Review automation issue", "Browser filling stopped because of an error. Review the details before trying again.", "Review Issue", ApplicationActionTarget.None, true, false, true),
        ApplicationWorkflowStatus.Withdrawn => new("No action required", "This application was removed from the active workflow.", "No Action Required", ApplicationActionTarget.None, false, false, true),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    public IReadOnlyList<ApplicationStageState> Progress(ApplicationWorkflowStatus status)
    {
        var current = CurrentStage(status);
        return Stages.Select(stage => new ApplicationStageState(
            stage,
            stage.ToString(),
            stage < current || status == ApplicationWorkflowStatus.Submitted && stage == current,
            stage == current,
            status == ApplicationWorkflowStatus.NeedsInput && stage == ApplicationWorkflowStage.Fill)).ToArray();
    }

    public ApplicationDashboard Dashboard(IReadOnlyList<ApplicationSummary> applications, DateTimeOffset now)
    {
        var start = now.AddDays(-7);
        var next = applications
            .Where(value => AttentionRank(value.Status) < int.MaxValue)
            .OrderBy(value => AttentionRank(value.Status))
            .ThenByDescending(value => value.UpdatedAtUtc ?? value.CreatedAtUtc)
            .FirstOrDefault();
        return new ApplicationDashboard(
            applications.Count(value => value.Status == ApplicationWorkflowStatus.Prepared),
            applications.Count(value => value.Status == ApplicationWorkflowStatus.NeedsInput),
            applications.Count(value => value.Status == ApplicationWorkflowStatus.ReadyToSubmit),
            applications.Count(value => value.Status == ApplicationWorkflowStatus.Submitted && (value.UpdatedAtUtc ?? value.CreatedAtUtc) >= start),
            next);
    }

    public ReadyToSubmitReview ReadyReview(JobApplication application, ApplicantProfile profile)
    {
        var fileName = Path.GetFileName(application.ResumePath);
        var uploaded = application.FieldsFilled.Any(value => value.StartsWith("resume:", StringComparison.OrdinalIgnoreCase) && value.Contains(fileName, StringComparison.Ordinal));
        var optional = new List<string>
        {
            "Unapproved optional written responses were left blank.",
            "Demographic and EEO responses were left unanswered."
        };
        if (string.IsNullOrWhiteSpace(profile.GitHubUrl)) optional.Add("GitHub was not supplied.");

        return new ReadyToSubmitReview(
            application.Company,
            application.JobTitle,
            application.Platform.ToString(),
            fileName,
            uploaded,
            new ApplicationContactSummary(
                $"{profile.LegalFirstName} {profile.LegalLastName}".Trim(),
                MaskEmail(profile.Email),
                MaskPhone(profile.Phone),
                string.Join(", ", new[] { profile.City, profile.State, profile.Country }.Where(value => !string.IsNullOrWhiteSpace(value))),
                !string.IsNullOrWhiteSpace(profile.LinkedInUrl)),
            application.Questions.Where(value => value.IsResolved && !string.IsNullOrWhiteSpace(value.Answer)).ToArray(),
            optional,
            application.Warnings.Where(IsMeaningfulWarning).ToArray());
    }

    public string StatusLabel(ApplicationWorkflowStatus status) => status switch
    {
        ApplicationWorkflowStatus.Draft => "Preparation incomplete",
        ApplicationWorkflowStatus.Prepared => "Ready for review",
        ApplicationWorkflowStatus.Approved => "Ready to fill",
        ApplicationWorkflowStatus.InProgress => "Filling application",
        ApplicationWorkflowStatus.NeedsInput => "Needs your answers",
        ApplicationWorkflowStatus.ReadyToSubmit => "Ready for final review",
        ApplicationWorkflowStatus.Submitted => "Application submitted",
        ApplicationWorkflowStatus.Failed => "Filling needs attention",
        ApplicationWorkflowStatus.Withdrawn => "No longer active",
        _ => status.ToString()
    };

    private static ApplicationWorkflowStage CurrentStage(ApplicationWorkflowStatus status) => status switch
    {
        ApplicationWorkflowStatus.Draft => ApplicationWorkflowStage.Prepare,
        ApplicationWorkflowStatus.Prepared => ApplicationWorkflowStage.Review,
        ApplicationWorkflowStatus.Approved or ApplicationWorkflowStatus.InProgress or ApplicationWorkflowStatus.NeedsInput or ApplicationWorkflowStatus.Failed => ApplicationWorkflowStage.Fill,
        ApplicationWorkflowStatus.ReadyToSubmit => ApplicationWorkflowStage.Ready,
        ApplicationWorkflowStatus.Submitted => ApplicationWorkflowStage.Submitted,
        ApplicationWorkflowStatus.Withdrawn => ApplicationWorkflowStage.Fill,
        _ => ApplicationWorkflowStage.Job
    };

    private static int AttentionRank(ApplicationWorkflowStatus status) => status switch
    {
        ApplicationWorkflowStatus.NeedsInput => 0,
        ApplicationWorkflowStatus.ReadyToSubmit => 1,
        ApplicationWorkflowStatus.Prepared => 2,
        ApplicationWorkflowStatus.Approved => 3,
        ApplicationWorkflowStatus.InProgress => 4,
        ApplicationWorkflowStatus.Draft => 5,
        ApplicationWorkflowStatus.Failed => 6,
        _ => int.MaxValue
    };

    private static bool IsMeaningfulWarning(string warning) =>
        !warning.Contains("submit controls were not activated", StringComparison.OrdinalIgnoreCase);

    private static string MaskEmail(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Contains('@')) return "Not provided";
        var parts = value.Split('@', 2);
        return $"{parts[0][0]}***@{parts[1]}";
    }

    private static string MaskPhone(string value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? $"Ending in {digits[^4..]}" : "Not provided";
    }
}
