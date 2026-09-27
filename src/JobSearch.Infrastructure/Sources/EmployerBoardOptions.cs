namespace JobSearch.Infrastructure.Sources;

public sealed class EmployerBoardOptions
{
    public bool Enabled { get; init; } = true;
    public required string Token { get; init; }
    public required string Company { get; init; }
    public int? CooldownMinutes { get; init; }
}
