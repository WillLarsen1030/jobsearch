namespace JobSearch.Application.Persistence;

public sealed record JobUpsertResult(StoredJob Job, bool IsNewJob, bool AddedSource);
