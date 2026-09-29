using System.Text.Json;
using JobSearch.Application.Applications;
using JobSearch.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace JobSearch.Infrastructure.Persistence;

public sealed class EfApplicationRepository(JobSearchDbContext dbContext) : IApplicationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<JobApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await Query().SingleOrDefaultAsync(application => application.Id == id, cancellationToken);
        return entity is null ? null : Map(entity);
    }

    public async Task<JobApplication?> GetForJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var entity = await Query().SingleOrDefaultAsync(application => application.JobId == jobId, cancellationToken);
        return entity is null ? null : Map(entity);
    }

    public async Task<IReadOnlyList<ApplicationSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var values = await dbContext.Applications.AsNoTracking()
            .Select(application => new ApplicationSummary(
                application.Id,
                application.JobId,
                application.Job.Title,
                application.Job.Company,
                (ApplicationWorkflowStatus)application.Status,
                (ApplicationPlatform)application.Platform,
                (AutomationMode)application.AutomationMode,
                application.CreatedAtUtc,
                application.UpdatedAtUtc,
                application.Questions.Count(question => !question.IsResolved),
                application.Job.Score,
                string.IsNullOrWhiteSpace(application.Job.SourceBoard) ? application.Job.Source : application.Job.SourceBoard))
            .ToListAsync(cancellationToken);
        return values.OrderBy(application => application.Status == ApplicationWorkflowStatus.NeedsInput ? 0 :
                application.Status == ApplicationWorkflowStatus.ReadyToSubmit ? 1 : 2)
            .ThenByDescending(application => application.UpdatedAtUtc)
            .ToArray();
    }

    public async Task<JobApplication> CreateAsync(Guid jobId, ApplicationPlatform platform, Uri url, string resumePath, AutomationMode mode, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var existing = await GetForJobAsync(jobId, cancellationToken);
        if (existing is not null) return existing;
        if (!await dbContext.Jobs.AnyAsync(job => job.Id == jobId, cancellationToken))
            throw new KeyNotFoundException($"Job '{jobId}' was not found.");

        var entity = new ApplicationEntity
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            Status = (int)ApplicationWorkflowStatus.Draft,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Platform = (int)platform,
            ApplicationUrl = url.ToString(),
            ResumePath = resumePath,
            AutomationMode = (int)mode
        };
        AddEvent(entity, ApplicationEventType.Created, now, $"Application package created in {mode} mode.");
        dbContext.Applications.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        return (await GetAsync(entity.Id, cancellationToken))!;
    }

    public async Task<JobApplication> TransitionAsync(Guid id, ApplicationWorkflowStatus status, DateTimeOffset now, string message, CancellationToken cancellationToken = default)
    {
        var entity = await LoadAsync(id, cancellationToken);
        var current = (ApplicationWorkflowStatus)entity.Status;
        ApplicationWorkflow.EnsureTransition(current, status);
        entity.Status = (int)status;
        entity.UpdatedAtUtc = now;
        switch (status)
        {
            case ApplicationWorkflowStatus.Prepared: entity.PreparedAtUtc = now; break;
            case ApplicationWorkflowStatus.Approved: entity.ApprovedAtUtc = now; break;
            case ApplicationWorkflowStatus.InProgress: entity.StartedAtUtc ??= now; break;
            case ApplicationWorkflowStatus.Submitted: entity.SubmittedAtUtc = now; entity.CompletedAtUtc = now; break;
            case ApplicationWorkflowStatus.Failed:
            case ApplicationWorkflowStatus.Withdrawn: entity.CompletedAtUtc = now; break;
        }
        AddEvent(entity, EventFor(status), now, message);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<JobApplication> RecordAutomationResultAsync(Guid id, AutomationResult result, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var entity = await LoadAsync(id, cancellationToken);
        var current = (ApplicationWorkflowStatus)entity.Status;
        if (current != result.Status)
            ApplicationWorkflow.EnsureTransition(current, result.Status);

        entity.Status = (int)result.Status;
        entity.UpdatedAtUtc = now;
        entity.BrowserRunId = result.RunId;
        entity.FieldsFilledJson = JsonSerializer.Serialize(result.FieldsFilled, JsonOptions);
        entity.WarningsJson = JsonSerializer.Serialize(result.Warnings, JsonOptions);
        entity.FailureReason = result.FailureReason ?? string.Empty;
        if (result.Status == ApplicationWorkflowStatus.Failed) entity.CompletedAtUtc = now;
        if (result.Submitted) { entity.SubmittedAtUtc = now; entity.CompletedAtUtc = now; }

        foreach (var field in result.FieldsFilled)
            AddEvent(entity, ApplicationEventType.FieldFilled, now, $"Filled {field}.");
        foreach (var answer in result.AnswersUsed)
            AddEvent(entity, ApplicationEventType.AnswerUsed, now, $"Used an approved answer for {answer}.");
        foreach (var warning in result.Warnings)
            AddEvent(entity, ApplicationEventType.Warning, now, warning);
        foreach (var question in result.UnknownQuestions)
        {
            if (entity.Questions.Any(existing => !existing.IsResolved && existing.NormalizedQuestion == QuestionMatcher.Normalize(question.Question))) continue;
            var pendingQuestion = new ApplicationQuestionEntity
            {
                Id = Guid.NewGuid(),
                ApplicationId = entity.Id,
                Question = question.Question,
                NormalizedQuestion = QuestionMatcher.Normalize(question.Question),
                FieldType = question.FieldType,
                OptionsJson = JsonSerializer.Serialize(question.Options, JsonOptions),
                IsRequired = question.IsRequired
            };
            entity.Questions.Add(pendingQuestion);
            dbContext.ApplicationQuestions.Add(pendingQuestion);
        }
        AddEvent(entity, EventFor(result.Status), now, ResultMessage(result));
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<JobApplication> ResolveQuestionAsync(Guid applicationId, Guid questionId, string answer, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var entity = await LoadAsync(applicationId, cancellationToken);
        var question = entity.Questions.SingleOrDefault(value => value.Id == questionId)
            ?? throw new KeyNotFoundException($"Question '{questionId}' was not found.");
        question.Answer = answer.Trim();
        question.IsResolved = true;
        entity.UpdatedAtUtc = now;
        AddEvent(entity, ApplicationEventType.AnswerUsed, now, $"A human-approved answer was supplied for {question.Question}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<JobApplication> SaveQuestionDraftAsync(Guid applicationId, Guid questionId, string draft, CancellationToken cancellationToken = default)
    {
        var entity = await LoadAsync(applicationId, cancellationToken);
        var question = entity.Questions.SingleOrDefault(value => value.Id == questionId)
            ?? throw new KeyNotFoundException($"Question '{questionId}' was not found.");
        question.DraftAnswer = draft.Trim();
        question.IsResolved = false;
        question.Answer = null;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<JobApplication> UpdateResumeAsync(Guid id, string resumePath, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var entity = await LoadAsync(id, cancellationToken);
        if (entity.Status is not (int)ApplicationWorkflowStatus.Draft and not (int)ApplicationWorkflowStatus.Prepared)
            throw new InvalidOperationException("The resume can only be changed before an application is approved.");
        entity.ResumePath = resumePath;
        entity.UpdatedAtUtc = now;
        AddEvent(entity, ApplicationEventType.Prepared, now, $"Selected resume: {Path.GetFileName(resumePath)}");
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<JobApplication> SetAutoSubmitApprovalAsync(Guid id, bool approved, CancellationToken cancellationToken = default)
    {
        var entity = await LoadAsync(id, cancellationToken);
        entity.AutoSubmitApproved = approved;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<IReadOnlyList<ApplicationAnswer>> GetAnswersAsync(CancellationToken cancellationToken = default) =>
        await dbContext.ApplicationAnswers.AsNoTracking().OrderBy(answer => answer.Pattern)
            .Select(answer => new ApplicationAnswer(answer.Id, answer.Pattern, answer.NormalizedPattern, answer.Answer,
                (AnswerType)answer.AnswerType, answer.Confidence, answer.AlwaysRequireConfirmation, answer.Notes, answer.LastConfirmedAtUtc))
            .ToListAsync(cancellationToken);

    public async Task<ApplicationAnswer> SaveAnswerAsync(ApplicationAnswer answer, CancellationToken cancellationToken = default)
    {
        var normalized = QuestionMatcher.Normalize(answer.Pattern);
        var entity = await dbContext.ApplicationAnswers.SingleOrDefaultAsync(value => value.Id == answer.Id || value.NormalizedPattern == normalized, cancellationToken);
        if (entity is null)
        {
            entity = new ApplicationAnswerEntity { Id = answer.Id == Guid.Empty ? Guid.NewGuid() : answer.Id };
            dbContext.ApplicationAnswers.Add(entity);
        }
        entity.Pattern = answer.Pattern.Trim(); entity.NormalizedPattern = normalized; entity.Answer = answer.Answer.Trim();
        entity.AnswerType = (int)answer.AnswerType; entity.Confidence = Math.Clamp(answer.Confidence, 0m, 1m);
        entity.AlwaysRequireConfirmation = answer.AlwaysRequireConfirmation; entity.Notes = answer.Notes.Trim();
        entity.LastConfirmedAtUtc = answer.LastConfirmedAtUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ApplicationAnswer(entity.Id, entity.Pattern, entity.NormalizedPattern, entity.Answer,
            (AnswerType)entity.AnswerType, entity.Confidence, entity.AlwaysRequireConfirmation, entity.Notes, entity.LastConfirmedAtUtc);
    }

    public async Task DeleteAnswerAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.ApplicationAnswers.FindAsync([id], cancellationToken);
        if (entity is null) return;
        dbContext.ApplicationAnswers.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<ApplicationEntity> Query() => dbContext.Applications.AsNoTracking()
        .Include(application => application.Job).Include(application => application.Events)
        .Include(application => application.Questions).AsSplitQuery();

    private async Task<ApplicationEntity> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.Applications.Include(application => application.Job).Include(application => application.Events)
            .Include(application => application.Questions).AsSplitQuery()
            .SingleOrDefaultAsync(application => application.Id == id, cancellationToken)
        ?? throw new KeyNotFoundException($"Application '{id}' was not found.");

    private void AddEvent(ApplicationEntity entity, ApplicationEventType type, DateTimeOffset now, string message)
    {
        var applicationEvent = new ApplicationEventEntity { Id = Guid.NewGuid(), ApplicationId = entity.Id, Type = (int)type, OccurredAtUtc = now, Message = message };
        entity.Events.Add(applicationEvent);
        dbContext.ApplicationEvents.Add(applicationEvent);
    }

    private static ApplicationEventType EventFor(ApplicationWorkflowStatus status) => status switch
    {
        ApplicationWorkflowStatus.Prepared => ApplicationEventType.Prepared,
        ApplicationWorkflowStatus.Approved => ApplicationEventType.Approved,
        ApplicationWorkflowStatus.InProgress => ApplicationEventType.AutomationStarted,
        ApplicationWorkflowStatus.NeedsInput => ApplicationEventType.NeedsInput,
        ApplicationWorkflowStatus.ReadyToSubmit => ApplicationEventType.ReadyToSubmit,
        ApplicationWorkflowStatus.Submitted => ApplicationEventType.Submitted,
        ApplicationWorkflowStatus.Failed => ApplicationEventType.Failed,
        ApplicationWorkflowStatus.Withdrawn => ApplicationEventType.Withdrawn,
        _ => ApplicationEventType.Created
    };

    private static string ResultMessage(AutomationResult result) => result.Status switch
    {
        ApplicationWorkflowStatus.NeedsInput => $"Automation stopped for {result.UnknownQuestions.Count} unanswered required question(s).",
        ApplicationWorkflowStatus.ReadyToSubmit => "Required fields were filled; the application is waiting for manual review and submission.",
        ApplicationWorkflowStatus.Failed => $"Automation failed: {result.FailureReason}",
        _ => $"Automation finished with status {result.Status}."
    };

    private static JobApplication Map(ApplicationEntity entity) => new(
        entity.Id, entity.JobId, entity.Job.Title, entity.Job.Company, (ApplicationWorkflowStatus)entity.Status,
        entity.CreatedAtUtc, entity.PreparedAtUtc, entity.ApprovedAtUtc, entity.StartedAtUtc, entity.SubmittedAtUtc,
        entity.CompletedAtUtc, (ApplicationPlatform)entity.Platform, new Uri(entity.ApplicationUrl), entity.ResumePath,
        (AutomationMode)entity.AutomationMode, entity.AutoSubmitApproved, entity.FailureReason, entity.BrowserRunId,
        JsonSerializer.Deserialize<string[]>(entity.FieldsFilledJson, JsonOptions) ?? [],
        JsonSerializer.Deserialize<string[]>(entity.WarningsJson, JsonOptions) ?? [],
        entity.Questions.OrderBy(question => question.IsResolved).Select(question => new PendingApplicationQuestion(
            question.Id, question.Question, question.NormalizedQuestion, question.FieldType,
            JsonSerializer.Deserialize<string[]>(question.OptionsJson, JsonOptions) ?? [], question.IsRequired,
            question.Answer, question.IsResolved, question.DraftAnswer)).ToArray(),
        entity.Events.OrderByDescending(applicationEvent => applicationEvent.OccurredAtUtc).Select(applicationEvent =>
            new ApplicationEvent(applicationEvent.Id, (ApplicationEventType)applicationEvent.Type,
                applicationEvent.OccurredAtUtc, applicationEvent.Message)).ToArray());
}
