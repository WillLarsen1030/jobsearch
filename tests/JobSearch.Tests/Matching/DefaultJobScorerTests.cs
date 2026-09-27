using JobSearch.Application.Matching;
using JobSearch.Domain.JobPostings;

namespace JobSearch.Tests.Matching;

public sealed class DefaultJobScorerTests
{
    private readonly DefaultJobScorer _scorer = new();
    private readonly JobSearchPreferences _preferences = new();

    [Fact]
    public void Score_StrongContractMatch_ReturnsHighScoreWithReasons()
    {
        var result = _scorer.Score(CreatePosting(), _preferences);

        Assert.Equal(100, result.Value);
        Assert.True(result.IsStrongMatch);
        Assert.Contains(result.Reasons, reason => reason.Category == "Core stack" && reason.Points > 0);
        Assert.Contains(result.Reasons, reason => reason.Category == "Compensation" && reason.Points > 0);
    }

    [Fact]
    public void Score_JuniorOnSiteClearanceRole_ReturnsLowScore()
    {
        var posting = CreatePosting() with
        {
            Title = "Junior .NET Developer",
            WorkLocationType = WorkLocationType.OnSite,
            EmploymentType = EmploymentType.FullTime,
            RequiresSecurityClearance = true,
            MaximumHourlyRate = 40m,
            EstimatedHoursPerWeek = 40
        };

        var result = _scorer.Score(posting, _preferences);

        Assert.Equal(0, result.Value);
        Assert.False(result.IsStrongMatch);
        Assert.False(result.EligibilityAssessment.IsEligible);
        Assert.Contains(result.Reasons, reason => reason.Category == "Clearance" && reason.Points < 0);
    }

    [Fact]
    public void Score_UnknownCompensation_ExplainsMissingRate()
    {
        var posting = CreatePosting() with
        {
            MinimumHourlyRate = null,
            MaximumHourlyRate = null
        };

        var result = _scorer.Score(posting, _preferences);

        Assert.Contains(result.Reasons, reason => reason.Category == "Compensation" && reason.Points == 0);
    }

    [Fact]
    public void Score_UnknownHours_DoesNotRejectPosting()
    {
        var posting = CreatePosting() with { EstimatedHoursPerWeek = null };

        var result = _scorer.Score(posting, _preferences);

        Assert.True(result.IsStrongMatch);
        Assert.DoesNotContain(result.Reasons, reason => reason.Category == "Hours" && reason.Points < 0);
    }

    [Fact]
    public void Score_RemoteRoleOutsideAllowedCountry_AppliesCountryPenalty()
    {
        var posting = CreatePosting() with { CountryCode = "CA" };

        var result = _scorer.Score(posting, _preferences);

        Assert.Contains(result.Reasons, reason => reason.Category == "Country" && reason.Points == -35);
        Assert.False(result.EligibilityAssessment.IsEligible);

    }

    [Fact]
    public void Score_IncidentalDotNetDomain_DoesNotCountAsTechnology()
    {
        var posting = CreatePosting() with
        {
            Title = "Senior Product Marketing Manager",
            Description = "Manage campaigns for example.net and partner programs.",
            Skills = []
        };

        var result = _scorer.Score(posting, _preferences);

        Assert.DoesNotContain(result.Reasons, reason => reason.Category == "Core stack" && reason.Points > 0);
        Assert.True(result.Value < 50);
    }

    [Fact]
    public void Score_UnknownLocationAndCountry_RemainsEligibleWithUnknowns()
    {
        var posting = CreatePosting() with { WorkLocationType = WorkLocationType.Unknown, CountryCode = string.Empty };

        var result = _scorer.Score(posting, _preferences);

        Assert.True(result.EligibilityAssessment.IsEligible);
        Assert.Contains(result.EligibilityAssessment.Unknowns, value => value.Contains("Remote status", StringComparison.Ordinal));
    }

    [Fact]
    public void Score_CompetingStackTitle_DoesNotRewardIncidentalDotNetList()
    {
        var posting = CreatePosting() with
        {
            Title = "Java Engineer",
            Description = "Primary Java and Spring role. Our consultancy also works with C#, .NET, and Angular.",
            Skills = []
        };

        var result = _scorer.Score(posting, _preferences);

        Assert.Contains(result.Reasons, reason => reason.Category == "Stack mismatch" && reason.Points < 0);
        Assert.DoesNotContain(result.Reasons, reason => reason.Category == "Core stack" && reason.Points > 0);
    }

    [Fact]
    public void Validate_PreferredRateBelowMinimum_Throws()
    {
        var preferences = new JobSearchPreferences
        {
            MinimumHourlyRate = 80m,
            PreferredHourlyRate = 60m
        };

        Assert.Throws<InvalidOperationException>(preferences.Validate);
    }

    private static JobPosting CreatePosting() => new()
    {
        Source = "test",
        SourceJobId = "job-123",
        Title = "Senior ASP.NET Core Developer",
        Company = "Acme",
        Url = new Uri("https://example.com/jobs/123"),
        Description = "Remote async contract work delivering REST APIs on Azure with PostgreSQL.",
        CountryCode = "US",
        WorkLocationType = WorkLocationType.Remote,
        EmploymentType = EmploymentType.Contract,
        MinimumHourlyRate = 65m,
        MaximumHourlyRate = 80m,
        EstimatedHoursPerWeek = 20,
        Skills = ["C#", ".NET", "ASP.NET Core", "Azure", "PostgreSQL"]
    };
}
