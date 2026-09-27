using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using JobSearch.Application.Matching;
using JobSearch.Domain.JobPostings;

namespace JobSearch.Infrastructure.Sources;

internal static partial class SourceNormalization
{
    private static readonly string[] UnitedStatesLocations =
    [
        "united states", "usa", "u.s.", "remote (us)", "remote, us", "us remote"
    ];

    private static readonly (string Name, string Code)[] CountryLocations =
    [
        ("canada", "CA"), ("united kingdom", "GB"), ("uk", "GB"), ("germany", "DE"),
        ("netherlands", "NL"), ("india", "IN"), ("australia", "AU"), ("brazil", "BR"),
        ("colombia", "CO"), ("peru", "PE"), ("mexico", "MX"), ("spain", "ES"),
        ("france", "FR"), ("italy", "IT"), ("poland", "PL"), ("romania", "RO"),
        ("ireland", "IE"), ("switzerland", "CH"), ("portugal", "PT")
    ];

    public static string ToPlainText(params string?[] values)
    {
        var decodedHtml = HtmlEntity.DeEntitize(HtmlEntity.DeEntitize(
            string.Join(' ', values.Where(value => !string.IsNullOrWhiteSpace(value)))));
        var document = new HtmlDocument();
        document.LoadHtml(decodedHtml);
        var decoded = HtmlEntity.DeEntitize(document.DocumentNode.InnerText);
        return string.Join(' ', decoded.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public static WorkLocationType DetectWorkLocation(string location, string text, string? explicitType = null)
    {
        var value = string.Join(' ', explicitType, location, text);
        if (ContainsAny(value, "hybrid", "days per week in the office", "days a week in the office"))
        {
            return WorkLocationType.Hybrid;
        }

        if (ContainsAny(explicitType ?? string.Empty, "onsite", "on-site") ||
            ContainsAny(value, "onsite only", "on-site only", "must work from our office", "in-office position"))
        {
            return WorkLocationType.OnSite;
        }

        return ContainsAny(value, "remote", "work from home", "distributed team")
            ? WorkLocationType.Remote
            : WorkLocationType.Unknown;
    }

    public static EmploymentType DetectEmploymentType(string value)
    {
        if (ContainsAny(value, "fractional")) return EmploymentType.Fractional;
        if (!ContainsAny(value, "not a contract role", "not a contract position") &&
            ContainsAny(value, "contract", "contractor", "freelance", "1099")) return EmploymentType.Contract;
        if (ContainsAny(value, "part-time", "part time")) return EmploymentType.PartTime;
        if (ContainsAny(value, "temporary", "temp role")) return EmploymentType.Temporary;
        if (ContainsAny(value, "full-time", "full time", "fte")) return EmploymentType.FullTime;
        return EmploymentType.Unknown;
    }

    public static string DetectCountryCode(string? explicitCountry, string location, string text)
    {
        if (string.Equals(explicitCountry, "US", StringComparison.OrdinalIgnoreCase) ||
            UnitedStatesLocations.Any(term => location.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
            ContainsAny(text, "must reside in the united states", "authorized to work in the united states", "remote usa", "remote (usa)"))
        {
            return "US";
        }

        var country = explicitCountry?.Trim();
        if (country?.Length == 2) return country.ToUpperInvariant();
        return CountryLocations.FirstOrDefault(item =>
            location.Contains(item.Name, StringComparison.OrdinalIgnoreCase)).Code ?? string.Empty;
    }

    public static int? DetectWeeklyHours(string text)
    {
        var range = HoursRangeRegex().Match(text);
        if (range.Success && int.TryParse(range.Groups[1].Value, out var minimum) && int.TryParse(range.Groups[2].Value, out var maximum) && maximum <= 80)
        {
            return (minimum + maximum) / 2;
        }

        var single = HoursSingleRegex().Match(text);
        return single.Success && int.TryParse(single.Groups[1].Value, out var hours) && hours <= 80 ? hours : null;
    }

    public static (decimal? Minimum, decimal? Maximum, string Description) DetectCompensation(string text)
    {
        var match = CompensationRegex().Match(text);
        if (!match.Success) return (null, null, string.Empty);

        var minimum = ParseAmount(match.Groups[1].Value, match.Groups[2].Value);
        var maximum = match.Groups[3].Success ? ParseAmount(match.Groups[3].Value, match.Groups[4].Value) : minimum;
        var period = match.Groups[5].Value;
        var annual = minimum > 1000 || period.Contains("year", StringComparison.OrdinalIgnoreCase) || period.Contains("annual", StringComparison.OrdinalIgnoreCase);
        var hourlyMinimum = annual ? minimum / 2080m : minimum;
        var hourlyMaximum = annual ? maximum / 2080m : maximum;
        return (hourlyMinimum, hourlyMaximum, match.Value.Trim());
    }

    public static decimal? ConvertToHourly(decimal? amount, string? interval) => interval?.ToLowerInvariant() switch
    {
        "per-hour-salary" or "hour" or "hourly" => amount,
        "per-day-salary" or "day" or "daily" => amount / 8m,
        "per-week-salary" or "week" or "weekly" => amount / 40m,
        "per-month-salary" or "month" or "monthly" => amount / 173.333m,
        "per-year-salary" or "year" or "yearly" or "annual" => amount / 2080m,
        _ => amount > 1000 ? amount / 2080m : amount
    };

    public static IReadOnlyCollection<string> DetectSkills(string text) => JobSignalDetector.DetectTechnologies(text);

    public static bool RequiresClearance(string text) => ContainsAny(
        text,
        "active security clearance required",
        "must possess a security clearance",
        "requires security clearance",
        "active secret clearance",
        "active top secret",
        "active ts/sci");

    public static bool IsLikelyAgency(string text) => ContainsAny(
        text,
        "our client is seeking",
        "on behalf of our client",
        "on behalf of a partner company",
        "staffing agency",
        "recruiting agency");

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static decimal ParseAmount(string number, string suffix)
    {
        var value = decimal.Parse(number.Replace(",", string.Empty), CultureInfo.InvariantCulture);
        return suffix.Equals("k", StringComparison.OrdinalIgnoreCase) ? value * 1000 : value;
    }

    [GeneratedRegex(@"\b(\d{1,2})\s*(?:-|to|–)\s*(\d{1,2})\s*hours?(?:\s+per\s+week|\s*/\s*week)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex HoursRangeRegex();

    [GeneratedRegex(@"\b(\d{1,2})\s*hours?(?:\s+per\s+week|\s*/\s*week)\b", RegexOptions.IgnoreCase)]
    private static partial Regex HoursSingleRegex();

    [GeneratedRegex(@"\$\s*([\d,]+(?:\.\d+)?)\s*([kK]?)\s*(?:-|to|–)?\s*\$?\s*([\d,]+(?:\.\d+)?)?\s*([kK]?)\s*(?:per\s+)?(hour|hourly|year|yearly|annual)?", RegexOptions.IgnoreCase)]
    private static partial Regex CompensationRegex();
}
