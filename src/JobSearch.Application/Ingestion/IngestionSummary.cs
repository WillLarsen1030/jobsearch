namespace JobSearch.Application.Ingestion;

public sealed record IngestionSummary(
    int Retrieved,
    int Normalized,
    int Duplicates,
    int NewJobs,
    int UpdatedJobs,
    int FailedSources);
