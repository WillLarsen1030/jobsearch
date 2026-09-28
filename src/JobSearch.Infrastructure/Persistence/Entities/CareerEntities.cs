namespace JobSearch.Infrastructure.Persistence.Entities;

internal sealed class CareerProfileEntity
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public int Status { get; set; }
    public string ProfileJson { get; set; } = "{}";
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal sealed class ResumeImportEntity
{
    public Guid Id { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public int Status { get; set; }
    public string ReviewJson { get; set; } = "{}";
}

internal sealed class JobFitAnalysisEntity
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public int CareerProfileVersion { get; set; }
    public int OverallFit { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string AnalysisJson { get; set; } = "{}";
}

internal sealed class ResumeArtifactEntity
{
    public Guid Id { get; set; }
    public Guid? JobId { get; set; }
    public int Kind { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public int CareerProfileVersion { get; set; }
    public string EvidenceIdsJson { get; set; } = "[]";
}

internal sealed class CareerProfileQuestionEntity
{
    public Guid Id { get; set; }
    public string Question { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public Guid? EmploymentId { get; set; }
    public string Answer { get; set; } = string.Empty;
    public bool IsApproved { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? AnsweredAtUtc { get; set; }
}
