using JobSearch.Application.Persistence;
using JobSearch.Domain.JobPostings;

namespace JobSearch.Application.Deduplication;

public sealed class DefaultJobDeduplicator : IJobDeduplicator
{
    public DuplicateMatch? FindDuplicate(JobPosting posting, IEnumerable<StoredJob> candidates)
    {
        ArgumentNullException.ThrowIfNull(posting);
        ArgumentNullException.ThrowIfNull(candidates);

        var candidateList = candidates.ToArray();
        var sourceMatch = FindSourceMatch(posting, candidateList);
        if (sourceMatch is not null)
        {
            return sourceMatch;
        }

        var canonicalUrl = JobTextNormalizer.CanonicalizeUrl(posting.Url);
        var urlMatch = candidateList.FirstOrDefault(candidate => candidate.Sources.Any(source =>
            JobTextNormalizer.CanonicalizeUrl(source.Url) == canonicalUrl));
        if (urlMatch is not null)
        {
            return new DuplicateMatch(urlMatch.Posting.Id, "canonical URL", 1);
        }

        var company = JobTextNormalizer.NormalizeCompany(posting.Company);
        var title = JobTextNormalizer.NormalizeTitle(posting.Title);

        foreach (var candidate in candidateList)
        {
            var companySimilarity = JobTextNormalizer.Similarity(
                company,
                JobTextNormalizer.NormalizeCompany(candidate.Posting.Company));
            var titleSimilarity = JobTextNormalizer.Similarity(
                title,
                JobTextNormalizer.NormalizeTitle(candidate.Posting.Title));

            if (companySimilarity >= 0.9 && titleSimilarity >= 0.8 && LocationsAreCompatible(posting, candidate.Posting))
            {
                return new DuplicateMatch(
                    candidate.Posting.Id,
                    "similar company, title, and location",
                    Math.Round((companySimilarity + titleSimilarity) / 2, 2));
            }
        }

        return null;
    }

    private static DuplicateMatch? FindSourceMatch(JobPosting posting, IEnumerable<StoredJob> candidates)
    {
        if (string.IsNullOrWhiteSpace(posting.SourceJobId))
        {
            return null;
        }

        var match = candidates.FirstOrDefault(candidate => candidate.Sources.Any(source =>
            source.Source.Equals(posting.Source, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(posting.SourceFeedKey) || source.FeedKey.Equals(posting.SourceFeedKey, StringComparison.OrdinalIgnoreCase)) &&
            source.SourceJobId.Equals(posting.SourceJobId, StringComparison.OrdinalIgnoreCase)));

        return match is null ? null : new DuplicateMatch(match.Posting.Id, "source job ID", 1);
    }

    private static bool LocationsAreCompatible(JobPosting left, JobPosting right)
    {
        if (left.WorkLocationType != right.WorkLocationType)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(left.CountryCode) && !string.IsNullOrWhiteSpace(right.CountryCode))
        {
            return left.CountryCode.Equals(right.CountryCode, StringComparison.OrdinalIgnoreCase);
        }

        var leftLocation = JobTextNormalizer.NormalizeLocation(left.Location);
        var rightLocation = JobTextNormalizer.NormalizeLocation(right.Location);
        return leftLocation.Length == 0 || rightLocation.Length == 0 || leftLocation == rightLocation;
    }
}
