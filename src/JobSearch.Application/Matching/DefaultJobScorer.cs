using JobSearch.Domain.JobPostings;

namespace JobSearch.Application.Matching;

public sealed class DefaultJobScorer : IJobScorer
{
    public JobScore Score(JobPosting posting, JobSearchPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(posting);
        ArgumentNullException.ThrowIfNull(preferences);
        preferences.Validate();

        var reasons = new List<ScoreReason>();
        var problems = new List<string>();
        var unknowns = new List<string>();
        var score = 20;
        var text = string.Join(' ', posting.Title, posting.Description, string.Join(' ', posting.Skills));

        AddLocation(posting, preferences, reasons, problems, unknowns, ref score);
        AddSeniority(posting, reasons, problems, ref score);
        AddTechnology(posting, text, reasons, ref score);
        AddRoleProfile(posting, text, reasons, ref score);
        AddEngagement(posting, text, preferences, reasons, unknowns, ref score);
        AddCompensation(posting, preferences, reasons, unknowns, ref score);
        AddHours(posting, preferences, reasons, unknowns, ref score);
        AddRisk(posting, preferences, reasons, problems, ref score);
        AddWorkStyle(posting, preferences, reasons, ref score);

        var eligibility = new JobEligibility(problems.Count == 0, problems, unknowns);
        if (!eligibility.IsEligible)
        {
            reasons.Add(new ScoreReason("Eligibility", 0, $"Deprioritized: {string.Join("; ", problems)}"));
        }

        return new JobScore(Math.Clamp(score, 0, 100), reasons.AsReadOnly(), eligibility);
    }

    private static void AddLocation(
        JobPosting posting,
        JobSearchPreferences preferences,
        ICollection<ScoreReason> reasons,
        ICollection<string> problems,
        ICollection<string> unknowns,
        ref int score)
    {
        switch (posting.WorkLocationType)
        {
            case WorkLocationType.Remote:
                AddReason(reasons, ref score, "Location", 18, "Remote work is explicitly supported.");
                break;
            case WorkLocationType.Hybrid when preferences.RequireRemote:
                problems.Add("The role is explicitly hybrid.");
                AddReason(reasons, ref score, "Location", -30, "Hybrid work conflicts with the remote-only preference.");
                break;
            case WorkLocationType.OnSite when preferences.RequireRemote:
                problems.Add("The role is explicitly onsite.");
                AddReason(reasons, ref score, "Location", -45, "Onsite work conflicts with the remote-only preference.");
                break;
            case WorkLocationType.Unknown when preferences.RequireRemote:
                unknowns.Add("Remote status is not stated clearly.");
                AddReason(reasons, ref score, "Location", -3, "Remote status is unknown, not assumed ineligible.");
                break;
        }

        if (string.IsNullOrWhiteSpace(posting.CountryCode))
        {
            unknowns.Add("Country eligibility is not stated clearly.");
            AddReason(reasons, ref score, "Country", 0, "Country eligibility is unknown.");
        }
        else if (preferences.AllowedCountryCodes.Count > 0 &&
            !preferences.AllowedCountryCodes.Contains(posting.CountryCode, StringComparer.OrdinalIgnoreCase))
        {
            problems.Add($"The role is limited to {posting.CountryCode}.");
            AddReason(reasons, ref score, "Country", -35, $"The role is outside the configured countries ({string.Join(", ", preferences.AllowedCountryCodes)}).");
        }
        else
        {
            AddReason(reasons, ref score, "Country", 5, $"The role is available in {posting.CountryCode}.");
        }
    }

    private static void AddSeniority(
        JobPosting posting,
        ICollection<ScoreReason> reasons,
        ICollection<string> problems,
        ref int score)
    {
        if (JobSignalDetector.IsJuniorRole(posting.Title))
        {
            problems.Add("The title indicates a junior, entry-level, or internship role.");
            AddReason(reasons, ref score, "Seniority", -45, "Junior-level terminology conflicts with the target experience level.");
        }
        else if (JobSignalDetector.IsSeniorRole(posting.Title))
        {
            AddReason(reasons, ref score, "Seniority", 12, "The title indicates senior, lead, staff, principal, or architect scope.");
        }
        else
        {
            AddReason(reasons, ref score, "Seniority", 0, "Seniority is not explicit in the title.");
        }
    }

    private static void AddTechnology(
        JobPosting posting,
        string text,
        ICollection<ScoreReason> reasons,
        ref int score)
    {
        var technologies = JobSignalDetector.DetectTechnologies(text);
        var titleTechnologies = JobSignalDetector.DetectTechnologies(posting.Title);
        var core = technologies.Where(value => value is "C#" or ".NET" or "ASP.NET" or "ASP.NET Core").ToArray();
        var supporting = technologies.Except(core, StringComparer.OrdinalIgnoreCase).ToArray();

        if ((JobSignalDetector.HasCompetingStackTitle(posting.Title) || JobSignalDetector.HasNonTargetSpecialization(posting.Title)) &&
            !titleTechnologies.Any(value => value is "C#" or ".NET" or "ASP.NET" or "ASP.NET Core"))
        {
            AddReason(reasons, ref score, "Stack mismatch", -25, "The title targets a different stack or specialization; incidental .NET mentions were not rewarded.");
        }
        else if (core.Length > 0)
        {
            AddReason(reasons, ref score, "Core stack", Math.Min(24, 10 + (core.Length * 5)), $"Matched core technologies: {string.Join(", ", core)}.");
        }
        else if (JobSignalDetector.HasRelevantEngineeringRole(posting.Title))
        {
            AddReason(reasons, ref score, "Core stack", -8, "The role is technical, but no meaningful C#/.NET signal was detected.");
        }
        else
        {
            AddReason(reasons, ref score, "Role relevance", -45, "The title does not appear to describe a software engineering role.");
        }

        if (supporting.Length > 0)
        {
            AddReason(reasons, ref score, "Supporting stack", Math.Min(18, supporting.Length * 3), $"Matched supporting technologies: {string.Join(", ", supporting)}.");
        }
    }

    private static void AddRoleProfile(JobPosting posting, string text, ICollection<ScoreReason> reasons, ref int score)
    {
        if (JobSignalDetector.IsBackendOrFullStackRole(posting.Title, text))
        {
            AddReason(reasons, ref score, "Role focus", 6, "The role is backend or full-stack oriented.");
        }
    }

    private static void AddEngagement(
        JobPosting posting,
        string text,
        JobSearchPreferences preferences,
        ICollection<ScoreReason> reasons,
        ICollection<string> unknowns,
        ref int score)
    {
        if (JobSignalDetector.ExplicitlyExcludesContract(text))
        {
            AddReason(reasons, ref score, "Engagement", -8, "The posting explicitly says this is not a contract role.");
        }
        else if (preferences.PreferredEmploymentTypes.Contains(posting.EmploymentType))
        {
            var points = posting.EmploymentType == EmploymentType.Fractional ? 15 : 12;
            AddReason(reasons, ref score, "Engagement", points, $"{posting.EmploymentType} is a preferred engagement type.");
        }
        else if (JobSignalDetector.HasPreferredEngagementLanguage(text))
        {
            AddReason(reasons, ref score, "Engagement", 8, "The description signals contract, consulting, part-time, or flexible work.");
        }
        else if (posting.EmploymentType == EmploymentType.FullTime)
        {
            AddReason(reasons, ref score, "Engagement", -8, "Full-time work is less compatible with a second role.");
        }
        else
        {
            unknowns.Add("Engagement type is unknown.");
            AddReason(reasons, ref score, "Engagement", 0, "Engagement type was not provided.");
        }
    }

    private static void AddCompensation(
        JobPosting posting,
        JobSearchPreferences preferences,
        ICollection<ScoreReason> reasons,
        ICollection<string> unknowns,
        ref int score)
    {
        var bestKnownRate = posting.MaximumHourlyRate ?? posting.MinimumHourlyRate;
        if (bestKnownRate is null)
        {
            unknowns.Add("Hourly compensation is not provided.");
            AddReason(reasons, ref score, "Compensation", 0, "Compensation is unknown, not treated as a rejection.");
        }
        else if (bestKnownRate < preferences.MinimumHourlyRate)
        {
            AddReason(reasons, ref score, "Compensation", -20, $"The listed rate is below ${preferences.MinimumHourlyRate:0}/hour.");
        }
        else if (bestKnownRate >= preferences.PreferredHourlyRate)
        {
            AddReason(reasons, ref score, "Compensation", 12, $"The listed rate reaches the preferred ${preferences.PreferredHourlyRate:0}/hour target.");
        }
        else
        {
            AddReason(reasons, ref score, "Compensation", 7, $"The listed rate meets the ${preferences.MinimumHourlyRate:0}/hour minimum.");
        }
    }

    private static void AddHours(
        JobPosting posting,
        JobSearchPreferences preferences,
        ICollection<ScoreReason> reasons,
        ICollection<string> unknowns,
        ref int score)
    {
        if (posting.EstimatedHoursPerWeek is not int hours)
        {
            unknowns.Add("Weekly hours are not provided.");
            AddReason(reasons, ref score, "Hours", 0, "Weekly hours are unknown.");
        }
        else if (hours >= preferences.MinimumHoursPerWeek && hours <= preferences.MaximumHoursPerWeek)
        {
            AddReason(reasons, ref score, "Hours", 10, $"{hours} hours/week is within the preferred range.");
        }
        else if (hours > preferences.MaximumHoursPerWeek)
        {
            AddReason(reasons, ref score, "Hours", -12, $"{hours} hours/week exceeds the preferred maximum.");
        }
    }

    private static void AddRisk(
        JobPosting posting,
        JobSearchPreferences preferences,
        ICollection<ScoreReason> reasons,
        ICollection<string> problems,
        ref int score)
    {
        if (posting.RequiresSecurityClearance && preferences.AvoidSecurityClearance)
        {
            problems.Add("An active security clearance is mandatory.");
            AddReason(reasons, ref score, "Clearance", -40, "Mandatory security clearance conflicts with the search criteria.");
        }

        if (posting.IsLikelyStaffingAgency && preferences.PenalizeLikelyStaffingAgencies)
        {
            AddReason(reasons, ref score, "Listing quality", -12, "The listing appears to be from an agency or undisclosed client.");
        }
    }

    private static void AddWorkStyle(JobPosting posting, JobSearchPreferences preferences, ICollection<ScoreReason> reasons, ref int score)
    {
        if (preferences.AsyncWorkKeywords.Any(keyword => posting.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            AddReason(reasons, ref score, "Work style", 5, "The description signals flexible or asynchronous work.");
        }
    }

    private static void AddReason(ICollection<ScoreReason> reasons, ref int score, string category, int points, string explanation)
    {
        score += points;
        reasons.Add(new ScoreReason(category, points, explanation));
    }
}
