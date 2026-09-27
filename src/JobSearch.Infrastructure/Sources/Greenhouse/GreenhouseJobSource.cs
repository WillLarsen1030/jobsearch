using System.Globalization;
using System.Net.Http.Json;
using JobSearch.Application.Sources;
using JobSearch.Domain.JobPostings;

namespace JobSearch.Infrastructure.Sources.Greenhouse;

public sealed class GreenhouseJobSource(
    HttpClient httpClient,
    GreenhouseOptions options,
    EmployerBoardOptions board,
    TimeProvider timeProvider) : IJobSource
{
    public string Key => $"greenhouse:{board.Token.ToLowerInvariant()}";
    public string Name => "Greenhouse";
    public string Board => board.Company;
    public TimeSpan Cooldown => TimeSpan.FromMinutes(Math.Max(1, board.CooldownMinutes ?? options.CooldownMinutes));

    public async Task<JobSourceResult> FetchAsync(JobSourceRequest request, CancellationToken cancellationToken = default)
    {
        var url = $"{options.BaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(board.Token)}/jobs?content=true";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<GreenhouseResponse>(cancellationToken: cancellationToken)
            ?? new GreenhouseResponse();
        var foundAt = timeProvider.GetUtcNow();

        var jobs = payload.Jobs
            .Select(job => Normalize(job, foundAt))
            .Where(job => job is not null)
            .Select(job => job!)
            .OrderByDescending(job => Relevance(job, request))
            .ThenBy(job => job.Title, StringComparer.OrdinalIgnoreCase)
            .Take(request.MaximumResults)
            .ToArray();

        return new JobSourceResult(payload.Jobs.Count, jobs);
    }

    private JobPosting? Normalize(GreenhouseJobDto source, DateTimeOffset foundAt)
    {
        if (source.Id == 0 || string.IsNullOrWhiteSpace(source.Title) || !Uri.TryCreate(source.AbsoluteUrl, UriKind.Absolute, out var url))
        {
            return null;
        }

        var description = SourceNormalization.ToPlainText(source.Content);
        var searchable = string.Join(' ', source.Title, source.Location.Name, description);
        var compensation = SourceNormalization.DetectCompensation(description);
        return new JobPosting
        {
            Source = Name,
            SourceBoard = Board,
            SourceFeedKey = Key,
            SourceJobId = source.Id.ToString(CultureInfo.InvariantCulture),
            Title = source.Title.Trim(),
            Company = board.Company.Trim(),
            Url = url,
            Description = description,
            Location = source.Location.Name.Trim(),
            CountryCode = SourceNormalization.DetectCountryCode(null, source.Location.Name, description),
            WorkLocationType = SourceNormalization.DetectWorkLocation(source.Location.Name, description),
            EmploymentType = SourceNormalization.DetectEmploymentType(searchable),
            MinimumHourlyRate = compensation.Minimum,
            MaximumHourlyRate = compensation.Maximum,
            CompensationDescription = compensation.Description,
            EstimatedHoursPerWeek = SourceNormalization.DetectWeeklyHours(description),
            Skills = SourceNormalization.DetectSkills(searchable),
            RequiresSecurityClearance = SourceNormalization.RequiresClearance(description),
            IsLikelyStaffingAgency = SourceNormalization.IsLikelyAgency(description),
            DateFoundUtc = foundAt
        };
    }

    private static int Relevance(JobPosting posting, JobSourceRequest request)
    {
        var text = string.Join(' ', posting.Title, posting.Description);
        return request.SearchTerms.Count(term => text.Contains(term, StringComparison.OrdinalIgnoreCase)) * 2 +
            request.SkillKeywords.Count(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
