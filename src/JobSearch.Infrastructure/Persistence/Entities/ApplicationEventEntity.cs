namespace JobSearch.Infrastructure.Persistence.Entities;

internal sealed class ApplicationEventEntity
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public ApplicationEntity Application { get; set; } = null!;
    public int Type { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string Message { get; set; } = string.Empty;
}
