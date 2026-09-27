namespace JobSearch.Application.Deduplication;

public sealed record DuplicateMatch(Guid JobId, string Reason, double Confidence);
