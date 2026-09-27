using System.Text.Json.Serialization;

namespace JobSearch.Infrastructure.Sources.Lever;

internal sealed class LeverJobDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("hostedUrl")]
    public string HostedUrl { get; init; } = string.Empty;

    [JsonPropertyName("descriptionPlain")]
    public string DescriptionPlain { get; init; } = string.Empty;

    [JsonPropertyName("descriptionBodyPlain")]
    public string DescriptionBodyPlain { get; init; } = string.Empty;

    [JsonPropertyName("openingPlain")]
    public string OpeningPlain { get; init; } = string.Empty;

    [JsonPropertyName("additionalPlain")]
    public string AdditionalPlain { get; init; } = string.Empty;

    [JsonPropertyName("country")]
    public string Country { get; init; } = string.Empty;

    [JsonPropertyName("workplaceType")]
    public string WorkplaceType { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public long CreatedAt { get; init; }

    [JsonPropertyName("categories")]
    public LeverCategoriesDto Categories { get; init; } = new();

    [JsonPropertyName("salaryRange")]
    public LeverSalaryRangeDto? SalaryRange { get; init; }
}

internal sealed class LeverCategoriesDto
{
    [JsonPropertyName("commitment")]
    public string Commitment { get; init; } = string.Empty;

    [JsonPropertyName("location")]
    public string Location { get; init; } = string.Empty;

    [JsonPropertyName("allLocations")]
    public IReadOnlyCollection<string> AllLocations { get; init; } = [];
}

internal sealed class LeverSalaryRangeDto
{
    [JsonPropertyName("min")]
    public decimal? Minimum { get; init; }

    [JsonPropertyName("max")]
    public decimal? Maximum { get; init; }

    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    [JsonPropertyName("interval")]
    public string Interval { get; init; } = string.Empty;
}
