using JobSearch.Domain.JobPostings;

namespace JobSearch.Application.Matching;

public sealed class JobSearchPreferences
{
    public bool RequireRemote { get; init; } = true;
    public decimal MinimumHourlyRate { get; init; } = 50m;
    public decimal PreferredHourlyRate { get; init; } = 70m;
    public int MinimumHoursPerWeek { get; init; } = 10;
    public int MaximumHoursPerWeek { get; init; } = 25;
    public bool AvoidSecurityClearance { get; init; } = true;
    public bool PenalizeLikelyStaffingAgencies { get; init; } = true;

    public int MaximumResultsPerSource { get; init; } = 200;

    public IReadOnlyCollection<string> AllowedCountryCodes { get; init; } = ["US"];

    public IReadOnlyCollection<string> SearchTerms { get; init; } = [".NET", "C#", "ASP.NET Core"];

    public IReadOnlyCollection<EmploymentType> PreferredEmploymentTypes { get; init; } =
        [EmploymentType.Contract, EmploymentType.PartTime, EmploymentType.Fractional];

    public IReadOnlyCollection<string> PreferredKeywords { get; init; } =
        ["C#", ".NET", "ASP.NET Core", "REST API", "Azure", "SQL Server", "PostgreSQL", "React", "Angular", "Umbraco", "modernization", "production support"];

    public IReadOnlyCollection<string> SeniorityKeywords { get; init; } =
        ["senior", "lead", "staff", "principal", "architect"];

    public IReadOnlyCollection<string> ExcludedKeywords { get; init; } =
        ["junior", "entry level", "entry-level", "onsite only", "on-site only"];

    public IReadOnlyCollection<string> AsyncWorkKeywords { get; init; } =
        ["asynchronous", "async", "flexible schedule", "deliverables"];

    public void Validate()
    {
        if (MinimumHourlyRate < 0 || PreferredHourlyRate < MinimumHourlyRate)
        {
            throw new InvalidOperationException("Hourly-rate preferences are invalid.");
        }

        if (MinimumHoursPerWeek < 0 || MaximumHoursPerWeek < MinimumHoursPerWeek)
        {
            throw new InvalidOperationException("Weekly-hour preferences are invalid.");
        }

        if (MaximumResultsPerSource is < 1 or > 200)
        {
            throw new InvalidOperationException("Maximum results per source must be between 1 and 200.");
        }
    }
}
