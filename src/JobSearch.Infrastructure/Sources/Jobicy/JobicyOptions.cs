namespace JobSearch.Infrastructure.Sources.Jobicy;

public sealed class JobicyOptions
{
    public bool Enabled { get; init; } = true;
    public string BaseUrl { get; init; } = "https://jobicy.com/api/v2/remote-jobs";
    public string Geography { get; init; } = "usa";
    public string Industry { get; init; } = "engineering";
    public int CooldownMinutes { get; init; } = 60;
}
