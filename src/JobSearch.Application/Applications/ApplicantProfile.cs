namespace JobSearch.Application.Applications;

public sealed class ApplicantProfile
{
    public string PreferredName { get; set; } = string.Empty;
    public string LegalFirstName { get; set; } = string.Empty;
    public string LegalLastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string LinkedInUrl { get; set; } = string.Empty;
    public string GitHubUrl { get; set; } = string.Empty;
    public string PortfolioUrl { get; set; } = string.Empty;
    public string CurrentTitle { get; set; } = string.Empty;
    public int? YearsOfExperience { get; set; }
    public string WorkAuthorization { get; set; } = string.Empty;
    public string SponsorshipRequirement { get; set; } = string.Empty;
    public string RemotePreference { get; set; } = string.Empty;
    public string CompensationPreference { get; set; } = string.Empty;
    public string ResumePath { get; set; } = string.Empty;
    public List<string> TechnologySkills { get; set; } = [];
    public List<EducationEntry> Education { get; set; } = [];
    public List<EmploymentEntry> EmploymentHistory { get; set; } = [];
    public Dictionary<string, string> DemographicAnswers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class EducationEntry
{
    public string School { get; set; } = string.Empty;
    public string Degree { get; set; } = string.Empty;
    public string FieldOfStudy { get; set; } = string.Empty;
    public int? GraduationYear { get; set; }
}

public sealed class EmploymentEntry
{
    public string Employer { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}

public sealed record ProfileValidation(bool ResumeExists, IReadOnlyList<string> MissingRequiredFacts);

public interface IApplicantProfileStore
{
    Task<ApplicantProfile> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ApplicantProfile profile, CancellationToken cancellationToken = default);
    ProfileValidation Validate(ApplicantProfile profile);
}
