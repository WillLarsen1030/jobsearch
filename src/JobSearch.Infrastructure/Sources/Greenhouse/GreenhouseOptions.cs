using JobSearch.Infrastructure.Sources;

namespace JobSearch.Infrastructure.Sources.Greenhouse;

public sealed class GreenhouseOptions
{
    public bool Enabled { get; init; } = true;
    public string BaseUrl { get; init; } = "https://boards-api.greenhouse.io/v1/boards";
    public int CooldownMinutes { get; init; } = 60;
    public IReadOnlyCollection<EmployerBoardOptions> Boards { get; init; } = [];
}
