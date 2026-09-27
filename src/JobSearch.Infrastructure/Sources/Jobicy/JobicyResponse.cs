using System.Text.Json.Serialization;

namespace JobSearch.Infrastructure.Sources.Jobicy;

internal sealed class JobicyResponse
{
    [JsonPropertyName("jobs")]
    public IReadOnlyCollection<JobicyJobDto> Jobs { get; init; } = [];
}

internal sealed class JobicyJobDto
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("jobTitle")]
    public string JobTitle { get; init; } = string.Empty;

    [JsonPropertyName("companyName")]
    public string CompanyName { get; init; } = string.Empty;

    [JsonPropertyName("jobType")]
    public IReadOnlyCollection<string> JobType { get; init; } = [];

    [JsonPropertyName("jobGeo")]
    public string JobGeo { get; init; } = string.Empty;

    [JsonPropertyName("jobLevel")]
    public string JobLevel { get; init; } = string.Empty;

    [JsonPropertyName("jobExcerpt")]
    public string JobExcerpt { get; init; } = string.Empty;

    [JsonPropertyName("jobDescription")]
    public string JobDescription { get; init; } = string.Empty;

    [JsonPropertyName("pubDate")]
    public DateTimeOffset? PublicationDate { get; init; }

    [JsonPropertyName("salaryMin")]
    public decimal? SalaryMinimum { get; init; }

    [JsonPropertyName("salaryMax")]
    public decimal? SalaryMaximum { get; init; }

    [JsonPropertyName("salaryCurrency")]
    public string SalaryCurrency { get; init; } = string.Empty;

    [JsonPropertyName("salaryPeriod")]
    public string SalaryPeriod { get; init; } = string.Empty;
}
