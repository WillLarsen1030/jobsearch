using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using JobSearch.Application.Sources;
using JobSearch.Application.Matching;
using JobSearch.Domain.JobPostings;

namespace JobSearch.Infrastructure.Sources.Jobicy;

internal static partial class JobicyNormalizer
{
    public static JobPosting? Normalize(JobicyJobDto source, JobSourceRequest request, DateTimeOffset seenAtUtc)
    {
        if (source.Id == 0 ||
            string.IsNullOrWhiteSpace(source.JobTitle) ||
            string.IsNullOrWhiteSpace(source.CompanyName) ||
            !Uri.TryCreate(source.Url, UriKind.Absolute, out var url))
        {
            return null;
        }

        var description = ToPlainText(source.JobDescription);
        var searchableText = string.Join(' ', source.JobTitle, source.JobLevel, source.JobExcerpt, description);
        var employmentType = DetectEmploymentType(source.JobType);
        var workLocationType = DetectWorkLocationType(searchableText);
        var hours = DetectWeeklyHours(searchableText);
        var skills = request.SkillKeywords
            .Where(keyword => searchableText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .Concat(JobSignalDetector.DetectTechnologies(searchableText))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new JobPosting
        {
            Source = "Jobicy",
            SourceBoard = "Remote Jobs API",
            SourceFeedKey = "jobicy",
            SourceJobId = source.Id.ToString(CultureInfo.InvariantCulture),
            Title = source.JobTitle.Trim(),
            Company = source.CompanyName.Trim(),
            Url = url,
            Description = description,
            Location = source.JobGeo.Trim(),
            CountryCode = DetectCountryCode(source.JobGeo, request.CountryCode),
            WorkLocationType = workLocationType,
            EmploymentType = employmentType,
            MinimumHourlyRate = ConvertToHourly(source.SalaryMinimum, source.SalaryPeriod),
            MaximumHourlyRate = ConvertToHourly(source.SalaryMaximum, source.SalaryPeriod),
            CompensationDescription = FormatCompensation(source),
            Currency = string.IsNullOrWhiteSpace(source.SalaryCurrency) ? "USD" : source.SalaryCurrency,
            EstimatedHoursPerWeek = hours,
            Skills = skills,
            RequiresSecurityClearance = ContainsAny(searchableText, "security clearance", "active clearance", "secret clearance", "top secret", "ts/sci"),
            IsLikelyStaffingAgency = ContainsAny(searchableText, "staffing agency", "recruiting agency", "our client is seeking"),
            DateFoundUtc = seenAtUtc,
            DatePostedUtc = source.PublicationDate
        };
    }

    private static string ToPlainText(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var decoded = HtmlEntity.DeEntitize(document.DocumentNode.InnerText);
        return string.Join(' ', decoded.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static EmploymentType DetectEmploymentType(IEnumerable<string> values)
    {
        var text = string.Join(' ', values);
        if (ContainsAny(text, "contract", "freelance"))
        {
            return EmploymentType.Contract;
        }

        if (text.Contains("part-time", StringComparison.OrdinalIgnoreCase) || text.Contains("part time", StringComparison.OrdinalIgnoreCase))
        {
            return EmploymentType.PartTime;
        }

        if (text.Contains("temporary", StringComparison.OrdinalIgnoreCase))
        {
            return EmploymentType.Temporary;
        }

        return text.Contains("full-time", StringComparison.OrdinalIgnoreCase) || text.Contains("full time", StringComparison.OrdinalIgnoreCase)
            ? EmploymentType.FullTime
            : EmploymentType.Unknown;
    }

    private static WorkLocationType DetectWorkLocationType(string text)
    {
        if (ContainsAny(
            text,
            "this role is hybrid",
            "this position is hybrid",
            "hybrid role",
            "hybrid position",
            "hybrid work environment",
            "days per week in the office"))
        {
            return WorkLocationType.Hybrid;
        }

        if (ContainsAny(text, "on-site only", "onsite only"))
        {
            return WorkLocationType.OnSite;
        }

        return WorkLocationType.Remote;
    }

    private static string DetectCountryCode(string geography, string requestedCountryCode)
    {
        if (ContainsAny(geography, "usa", "united states", "u.s."))
        {
            return "US";
        }

        return geography.Contains("anywhere", StringComparison.OrdinalIgnoreCase) ? string.Empty : requestedCountryCode;
    }

    private static int? DetectWeeklyHours(string text)
    {
        var range = HoursRangeRegex().Match(text);
        if (range.Success &&
            int.TryParse(range.Groups[1].Value, CultureInfo.InvariantCulture, out var minimum) &&
            int.TryParse(range.Groups[2].Value, CultureInfo.InvariantCulture, out var maximum) &&
            maximum is > 0 and <= 80)
        {
            return (minimum + maximum) / 2;
        }

        var single = HoursSingleRegex().Match(text);
        return single.Success &&
            int.TryParse(single.Groups[1].Value, CultureInfo.InvariantCulture, out var hours) &&
            hours is > 0 and <= 80
                ? hours
                : null;
    }

    private static decimal? ConvertToHourly(decimal? amount, string period) => period.ToLowerInvariant() switch
    {
        "hourly" => amount,
        "daily" => amount / 8m,
        "weekly" => amount / 40m,
        "monthly" => amount / 173.333m,
        "yearly" => amount / 2080m,
        _ => null
    };

    private static string FormatCompensation(JobicyJobDto source)
    {
        if (source.SalaryMinimum is null && source.SalaryMaximum is null)
        {
            return string.Empty;
        }

        var currency = string.IsNullOrWhiteSpace(source.SalaryCurrency) ? "USD" : source.SalaryCurrency;
        var range = source.SalaryMinimum == source.SalaryMaximum || source.SalaryMaximum is null
            ? source.SalaryMinimum?.ToString("0.##", CultureInfo.InvariantCulture)
            : $"{source.SalaryMinimum:0.##}-{source.SalaryMaximum:0.##}";
        return $"{currency} {range} {source.SalaryPeriod}".Trim();
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"\b(\d{1,2})\s*(?:-|to|–)\s*(\d{1,2})\s*hours?(?:\s+per\s+week|\s*/\s*week)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex HoursRangeRegex();

    [GeneratedRegex(@"\b(\d{1,2})\s*hours?(?:\s+per\s+week|\s*/\s*week)\b", RegexOptions.IgnoreCase)]
    private static partial Regex HoursSingleRegex();
}
