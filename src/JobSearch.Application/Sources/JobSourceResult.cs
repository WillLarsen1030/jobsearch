using JobSearch.Domain.JobPostings;

namespace JobSearch.Application.Sources;

public sealed record JobSourceResult(int RetrievedCount, IReadOnlyCollection<JobPosting> Jobs);
