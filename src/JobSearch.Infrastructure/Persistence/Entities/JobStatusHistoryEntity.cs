namespace JobSearch.Infrastructure.Persistence.Entities;

internal sealed class JobStatusHistoryEntity
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public JobEntity Job { get; set; } = null!;
    public int OldStatus { get; set; }
    public int NewStatus { get; set; }
    public DateTimeOffset ChangedAtUtc { get; set; }
    public string Note { get; set; } = string.Empty;
}
