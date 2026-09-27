namespace JobSearch.Infrastructure.Persistence.Entities;

internal sealed class ApplicationEntity
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public JobEntity Job { get; set; } = null!;
    public int Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? PreparedAtUtc { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? SubmittedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public int Platform { get; set; }
    public string ApplicationUrl { get; set; } = string.Empty;
    public string ResumePath { get; set; } = string.Empty;
    public int AutomationMode { get; set; }
    public bool AutoSubmitApproved { get; set; }
    public string FailureReason { get; set; } = string.Empty;
    public string BrowserRunId { get; set; } = string.Empty;
    public string FieldsFilledJson { get; set; } = "[]";
    public string WarningsJson { get; set; } = "[]";
    public ICollection<ApplicationEventEntity> Events { get; set; } = [];
    public ICollection<ApplicationQuestionEntity> Questions { get; set; } = [];
}
