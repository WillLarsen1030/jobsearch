namespace JobSearch.Application.Sources;

public interface IJobSource
{
    string Key => Name.ToLowerInvariant();

    string Name { get; }

    string Board => Name;

    TimeSpan Cooldown => TimeSpan.FromHours(1);

    Task<JobSourceResult> FetchAsync(JobSourceRequest request, CancellationToken cancellationToken = default);
}
