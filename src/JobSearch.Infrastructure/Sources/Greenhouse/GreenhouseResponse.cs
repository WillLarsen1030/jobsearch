using System.Text.Json.Serialization;

namespace JobSearch.Infrastructure.Sources.Greenhouse;

internal sealed class GreenhouseResponse
{
    [JsonPropertyName("jobs")]
    public IReadOnlyCollection<GreenhouseJobDto> Jobs { get; init; } = [];
}

internal sealed class GreenhouseJobDto
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("absolute_url")]
    public string AbsoluteUrl { get; init; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;

    [JsonPropertyName("location")]
    public GreenhouseLocationDto Location { get; init; } = new();
}

internal sealed class GreenhouseLocationDto
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}
