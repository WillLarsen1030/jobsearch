namespace JobSearch.Domain.JobPostings;

public sealed record JobPosting
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Source { get; init; }

    public string SourceBoard { get; init; } = string.Empty;

    public string SourceFeedKey { get; init; } = string.Empty;

    public required string SourceJobId { get; init; }

    public required string Title { get; init; }

    public required string Company { get; init; }

    public required Uri Url { get; init; }

    public string Description { get; init; } = string.Empty;

    public string Location { get; init; } = string.Empty;

    public string CountryCode { get; init; } = string.Empty;

    public WorkLocationType WorkLocationType { get; init; }

    public EmploymentType EmploymentType { get; init; }

    public decimal? MinimumHourlyRate { get; init; }

    public decimal? MaximumHourlyRate { get; init; }

    public string CompensationDescription { get; init; } = string.Empty;

    public string Currency { get; init; } = "USD";

    public int? EstimatedHoursPerWeek { get; init; }

    public IReadOnlyCollection<string> Skills { get; init; } = [];

    public bool RequiresSecurityClearance { get; init; }

    public bool IsLikelyStaffingAgency { get; init; }

    public DateTimeOffset DateFoundUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? DatePostedUtc { get; init; }

    public DateTimeOffset? DateAppliedUtc { get; init; }

    public DateOnly? FollowUpDate { get; init; }

    public ApplicationStatus Status { get; init; } = ApplicationStatus.Unreviewed;
}
