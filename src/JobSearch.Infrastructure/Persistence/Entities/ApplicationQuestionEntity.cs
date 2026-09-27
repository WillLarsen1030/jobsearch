namespace JobSearch.Infrastructure.Persistence.Entities;

internal sealed class ApplicationQuestionEntity
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public ApplicationEntity Application { get; set; } = null!;
    public string Question { get; set; } = string.Empty;
    public string NormalizedQuestion { get; set; } = string.Empty;
    public string FieldType { get; set; } = string.Empty;
    public string OptionsJson { get; set; } = "[]";
    public bool IsRequired { get; set; }
    public string? Answer { get; set; }
    public bool IsResolved { get; set; }
}
