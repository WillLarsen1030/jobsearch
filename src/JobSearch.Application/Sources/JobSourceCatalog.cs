namespace JobSearch.Application.Sources;

public sealed record JobSourceCatalog(IReadOnlyList<IJobSource> Sources);
