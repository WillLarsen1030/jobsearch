using System.Globalization;
using System.Net.Http.Json;
using JobSearch.Application.Sources;
using JobSearch.Domain.JobPostings;

namespace JobSearch.Infrastructure.Sources.Lever;

public sealed class LeverJobSource(
    HttpClient httpClient,
    LeverOptions options,
    EmployerBoardOptions board,
    TimeProvider timeProvider) : IJobSource
{
    public string Key => $"lever:{board.Token.ToLowerInvariant()}";
    public string Name => "Lever";
    public string Board => board.Company;
    public TimeSpan Cooldown => TimeSpan.FromMinutes(Math.Max(1, board.CooldownMinutes ?? options.CooldownMinutes));

    public async Task<JobSourceResult> FetchAsync(JobSourceRequest request, CancellationToken cancellationToken = default)
    {
        var url = $"{options.BaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(board.Token)}?mode=json";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<LeverJobDto>>(cancellationToken: cancellationToken) ?? [];
        var foundAt = timeProvider.GetUtcNow();
        var jobs = payload
            .Select(job => Normalize(job, foundAt))
            .Where(job => job is not null)
            .Select(job => job!)
            .OrderByDescending(job => Relevance(job, request))
            .ThenBy(job => job.Title, StringComparer.OrdinalIgnoreCase)
            .Take(request.MaximumResults)
            .ToArray();
        return new JobSourceResult(payload.Count, jobs);
    }

    private JobPosting? Normalize(LeverJobDto source, DateTimeOffset foundAt)
    {
        if (string.IsNullOrWhiteSpace(source.Id) || string.IsNullOrWhiteSpace(source.Text) || !Uri.TryCreate(source.HostedUrl, UriKind.Absolute, out var url))
        {
            return null;
        }

        var description = SourceNormalization.ToPlainText(
            source.OpeningPlain,
            source.DescriptionPlain,
            source.DescriptionBodyPlain,
            source.AdditionalPlain);
        var location = source.Categories.AllLocations.Count > 0
            ? string.Join("; ", source.Categories.AllLocations)
            : source.Categories.Location;
        var searchable = string.Join(' ', source.Text, source.Categories.Commitment, location, description);
        var salary = source.SalaryRange;
        var minimumHourly = SourceNormalization.ConvertToHourly(salary?.Minimum, salary?.Interval);
        var maximumHourly = SourceNormalization.ConvertToHourly(salary?.Maximum, salary?.Interval);

        return new JobPosting
        {
            Source = Name,
            SourceBoard = Board,
            SourceFeedKey = Key,
            SourceJobId = source.Id,
            Title = source.Text.Trim(),
            Company = board.Company.Trim(),
            Url = url,
            Description = description,
            Location = location.Trim(),
            CountryCode = SourceNormalization.DetectCountryCode(source.Country, location, description),
            WorkLocationType = SourceNormalization.DetectWorkLocation(location, description, source.WorkplaceType),
            EmploymentType = SourceNormalization.DetectEmploymentType(string.Join(' ', source.Categories.Commitment, description)),
            MinimumHourlyRate = minimumHourly,
            MaximumHourlyRate = maximumHourly,
            CompensationDescription = FormatCompensation(salary),
            Currency = string.IsNullOrWhiteSpace(salary?.Currency) ? "USD" : salary.Currency.ToUpperInvariant(),
            EstimatedHoursPerWeek = SourceNormalization.DetectWeeklyHours(description),
            Skills = SourceNormalization.DetectSkills(searchable),
            RequiresSecurityClearance = SourceNormalization.RequiresClearance(description),
            IsLikelyStaffingAgency = SourceNormalization.IsLikelyAgency(description),
            DateFoundUtc = foundAt,
            DatePostedUtc = source.CreatedAt > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(source.CreatedAt) : null
        };
    }

    private static string FormatCompensation(LeverSalaryRangeDto? salary)
    {
        if (salary?.Minimum is null && salary?.Maximum is null) return string.Empty;
        var currency = string.IsNullOrWhiteSpace(salary.Currency) ? "USD" : salary.Currency.ToUpperInvariant();
        var range = salary.Minimum == salary.Maximum || salary.Maximum is null
            ? salary.Minimum?.ToString("0.##", CultureInfo.InvariantCulture)
            : $"{salary.Minimum:0.##}-{salary.Maximum:0.##}";
        return $"{currency} {range} {salary.Interval}".Trim();
    }

    private static int Relevance(JobPosting posting, JobSourceRequest request)
    {
        var text = string.Join(' ', posting.Title, posting.Description);
        return request.SearchTerms.Count(term => text.Contains(term, StringComparison.OrdinalIgnoreCase)) * 2 +
            request.SkillKeywords.Count(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
