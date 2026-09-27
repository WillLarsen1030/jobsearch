using JobSearch.Application.Persistence;
using JobSearch.Domain.JobPostings;

namespace JobSearch.Application.Deduplication;

public interface IJobDeduplicator
{
    DuplicateMatch? FindDuplicate(JobPosting posting, IEnumerable<StoredJob> candidates);
}
