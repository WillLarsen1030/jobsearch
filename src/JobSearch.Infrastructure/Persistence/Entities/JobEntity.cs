namespace JobSearch.Infrastructure.Persistence.Entities;

internal sealed class JobEntity
{
    public Guid Id { get; set; }
    public string Source { get; set; } = string.Empty;
    public string SourceBoard { get; set; } = string.Empty;
    public string SourceFeedKey { get; set; } = string.Empty;
    public string SourceJobId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public int WorkLocationType { get; set; }
    public int EmploymentType { get; set; }
    public decimal? MinimumHourlyRate { get; set; }
    public decimal? MaximumHourlyRate { get; set; }
    public string CompensationDescription { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public int? EstimatedHoursPerWeek { get; set; }
    public string SkillsJson { get; set; } = "[]";
    public bool RequiresSecurityClearance { get; set; }
    public bool IsLikelyStaffingAgency { get; set; }
    public DateTimeOffset DateFoundUtc { get; set; }
    public DateTimeOffset? DatePostedUtc { get; set; }
    public DateTimeOffset? DateAppliedUtc { get; set; }
    public DateOnly? FollowUpDate { get; set; }
    public int Status { get; set; }
    public string NormalizedCompany { get; set; } = string.Empty;
    public string NormalizedTitle { get; set; } = string.Empty;
    public string NormalizedLocation { get; set; } = string.Empty;
    public string CanonicalUrl { get; set; } = string.Empty;
    public DateTimeOffset FirstSeenUtc { get; set; }
    public long FirstSeenUnixSeconds { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
    public int Score { get; set; }
    public string ScoreReasonsJson { get; set; } = "[]";
    public string Notes { get; set; } = string.Empty;
    public bool IsEligible { get; set; } = true;
    public string EligibilityProblemsJson { get; set; } = "[]";
    public string EligibilityUnknownsJson { get; set; } = "[]";
    public ICollection<JobSourceEntity> Sources { get; set; } = [];
    public ICollection<JobStatusHistoryEntity> StatusHistory { get; set; } = [];
    public ICollection<ApplicationEntity> Applications { get; set; } = [];
}
