using JobSearch.Application.Matching;
using JobSearch.Infrastructure.Sources.Jobicy;
using JobSearch.Infrastructure.Sources.Greenhouse;
using JobSearch.Infrastructure.Sources.Lever;

namespace JobSearch.Infrastructure.Configuration;

public sealed class JobSearchAppSettings
{
    public JobSearchPreferences JobSearch { get; init; } = new();
    public DatabaseSettings Database { get; init; } = new();
    public JobSourceSettings JobSources { get; init; } = new();
    public ApplicantProfileSettings ApplicantProfile { get; init; } = new();
    public ApplicationAutomationSettings ApplicationAutomation { get; init; } = new();
}

public sealed class ApplicantProfileSettings
{
    public string Path { get; init; } = "data/applicant-profile.json";
}

public sealed class ApplicationAutomationSettings
{
    public bool Headless { get; init; }
    public string ArtifactDirectory { get; init; } = "data/application-artifacts";
    public IReadOnlyList<string> AutoSubmitEnabledPlatforms { get; init; } = [];
}

public sealed class DatabaseSettings
{
    public string Path { get; init; } = "data/jobsearch.db";
}

public sealed class JobSourceSettings
{
    public JobicyOptions Jobicy { get; init; } = new();
    public GreenhouseOptions Greenhouse { get; init; } = new();
    public LeverOptions Lever { get; init; } = new();
}
