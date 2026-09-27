using JobSearch.Domain.JobPostings;

namespace JobSearch.Application.Matching;

public interface IJobScorer
{
    JobScore Score(JobPosting posting, JobSearchPreferences preferences);
}
