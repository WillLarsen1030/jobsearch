using JobSearch.Domain.JobPostings;

namespace JobSearch.Application.Career;

public enum CareerProfileStatus
{
    Draft,
    Approved
}

public enum ResumeArtifactKind
{
    Master,
    Tailored
}

public enum ResumeImportStatus
{
    PendingReview,
    Approved,
    Rejected
}

public sealed class CareerProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Version { get; set; } = 1;
    public CareerProfileStatus Status { get; set; } = CareerProfileStatus.Draft;
    public string Headline { get; set; } = string.Empty;
    public string ProfessionalSummary { get; set; } = string.Empty;
    public decimal? TotalExperienceYears { get; set; }
    public List<CareerSkill> Skills { get; set; } = [];
    public List<CareerEvidence> GeneralEvidence { get; set; } = [];
    public List<EmploymentExperience> EmploymentHistory { get; set; } = [];
    public List<CareerEducation> Education { get; set; } = [];
    public List<ProfessionalLink> ProfessionalLinks { get; set; } = [];
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class CareerSkill
{
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public List<Guid> DemonstratedByPositionIds { get; set; } = [];
    public string MostRecentUse { get; set; } = string.Empty;
    public string EvidenceStrength { get; set; } = "Listed";
}

public sealed class EmploymentExperience
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Employer { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
    public List<CareerEvidence> Evidence { get; set; } = [];
    public List<string> Technologies { get; set; } = [];
    public string Source { get; set; } = string.Empty;
}

public sealed class CareerEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Statement { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}

public sealed class CareerEducation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Institution { get; set; } = string.Empty;
    public string Program { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public bool? CredentialEarned { get; set; }
    public string Source { get; set; } = string.Empty;
}

public sealed class ProfessionalLink
{
    public string Label { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public sealed record ImportFieldComparison(
    string Field,
    string ExtractedValue,
    string ExistingValue,
    string ProposedValue,
    bool HasConflict,
    string Note = "");

public sealed class ResumeImportReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SourcePath { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public ResumeImportStatus Status { get; set; } = ResumeImportStatus.PendingReview;
    public CareerProfile ProposedProfile { get; set; } = new();
    public List<ImportFieldComparison> Comparisons { get; set; } = [];
    public List<string> Ambiguities { get; set; } = [];
}

public sealed record JobRequirement(string Name, string NormalizedName, string EvidenceText);

public sealed record CareerEvidenceMatch(Guid EmploymentId, Guid EvidenceId, string Employer, string Title, string Statement);

public sealed class JobFitAnalysis
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobPostingId { get; set; }
    public int CareerProfileVersion { get; set; }
    public int OverallFit { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public List<JobRequirement> MatchedRequirements { get; set; } = [];
    public List<JobRequirement> PartiallyMatchedRequirements { get; set; } = [];
    public List<JobRequirement> NotFoundRequirements { get; set; } = [];
    public List<CareerEvidenceMatch> RelevantEvidence { get; set; } = [];
    public List<string> TechnologiesMatched { get; set; } = [];
    public List<string> Concerns { get; set; } = [];
    public List<string> ResumeEmphasisRecommendations { get; set; } = [];
}

public sealed class ResumeArtifact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? JobPostingId { get; set; }
    public ResumeArtifactKind Kind { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public int CareerProfileVersion { get; set; }
    public List<Guid> EvidenceIds { get; set; } = [];
}

public sealed class CareerProfileQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Question { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public Guid? EmploymentId { get; set; }
    public string Answer { get; set; } = string.Empty;
    public bool IsApproved { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? AnsweredAtUtc { get; set; }
}

public sealed record ApplicationAnswerDraft(string Question, string Draft, IReadOnlyList<Guid> EvidenceIds, bool IsApproved = false);

public interface ICareerProfileRepository
{
    Task<CareerProfile?> GetProfileAsync(CancellationToken cancellationToken = default);
    Task<CareerProfile> SaveProfileAsync(CareerProfile profile, CancellationToken cancellationToken = default);
    Task<ResumeImportReview?> GetLatestImportAsync(CancellationToken cancellationToken = default);
    Task<ResumeImportReview> SaveImportAsync(ResumeImportReview review, CancellationToken cancellationToken = default);
    Task<CareerProfile> ApproveImportAsync(Guid importId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<JobFitAnalysis?> GetFitAnalysisAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<JobFitAnalysis> SaveFitAnalysisAsync(JobFitAnalysis analysis, CancellationToken cancellationToken = default);
    Task<ResumeArtifact?> GetResumeAsync(Guid? jobId, ResumeArtifactKind kind, CancellationToken cancellationToken = default);
    Task<ResumeArtifact> SaveResumeAsync(ResumeArtifact artifact, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CareerProfileQuestion>> GetQuestionsAsync(CancellationToken cancellationToken = default);
    Task<CareerProfileQuestion> SaveQuestionAsync(CareerProfileQuestion question, CancellationToken cancellationToken = default);
}

public interface IResumeImportService
{
    Task<ResumeImportReview> CreateReviewAsync(string path, CareerProfile? existingProfile, Applications.ApplicantProfile applicantProfile, CancellationToken cancellationToken = default);
}

public interface ICareerAnalysisService
{
    JobFitAnalysis Analyze(JobPosting job, CareerProfile profile, DateTimeOffset now);
}

public interface IResumeTailoringService
{
    Task<ResumeArtifact> GenerateMasterAsync(CareerProfile profile, Applications.ApplicantProfile applicantProfile, string outputDirectory, CancellationToken cancellationToken = default);
    Task<ResumeArtifact> GenerateTailoredAsync(JobPosting job, CareerProfile profile, Applications.ApplicantProfile applicantProfile, JobFitAnalysis analysis, string outputDirectory, CancellationToken cancellationToken = default);
}

public interface IApplicationAnswerDraftingService
{
    ApplicationAnswerDraft Draft(string question, JobPosting job, CareerProfile profile, Applications.ApplicantProfile applicantProfile);
}
