using JobSearch.Infrastructure.Sources;

namespace JobSearch.Infrastructure.Sources.Lever;

public sealed class LeverOptions
{
    public bool Enabled { get; init; } = true;
    public string BaseUrl { get; init; } = "https://api.lever.co/v0/postings";
    public int CooldownMinutes { get; init; } = 60;
    public IReadOnlyCollection<EmployerBoardOptions> Boards { get; init; } = [];
}
