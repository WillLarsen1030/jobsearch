namespace JobSearch.Application.Ingestion;

public sealed record FetchRunResult(IReadOnlyList<SourceFetchResult> Sources)
{
    public int Retrieved => Sources.Sum(source => source.Retrieved);
    public int Normalized => Sources.Sum(source => source.Normalized);
    public int Duplicates => Sources.Sum(source => source.Duplicates);
    public int NewJobs => Sources.Sum(source => source.NewJobs);
    public int UpdatedJobs => Sources.Sum(source => source.UpdatedJobs);
    public int FailedSources => Sources.Count(source => source.Status == SourceFetchStatus.Failed);
    public int SkippedSources => Sources.Count(source => source.Status == SourceFetchStatus.Cooldown);
    public int SuccessfulSources => Sources.Count(source => source.Status == SourceFetchStatus.Succeeded);
}
