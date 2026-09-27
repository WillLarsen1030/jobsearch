namespace JobSearch.Infrastructure.Persistence.Entities;

internal sealed class ApplicationAnswerEntity
{
    public Guid Id { get; set; }
    public string Pattern { get; set; } = string.Empty;
    public string NormalizedPattern { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public int AnswerType { get; set; }
    public decimal Confidence { get; set; }
    public bool AlwaysRequireConfirmation { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DateTimeOffset LastConfirmedAtUtc { get; set; }
}
