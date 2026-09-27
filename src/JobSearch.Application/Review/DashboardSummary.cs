namespace JobSearch.Application.Review;

public sealed record DashboardSummary(
    int Active,
    int Unreviewed,
    int StrongMatches,
    int Interested,
    int Applied,
    int Interviews,
    int RejectedOrSkipped,
    int AddedLast24Hours,
    int AddedLast7Days);
