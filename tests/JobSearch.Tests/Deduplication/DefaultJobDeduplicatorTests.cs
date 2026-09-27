using JobSearch.Application.Deduplication;
using JobSearch.Application.Persistence;
using JobSearch.Domain.JobPostings;

namespace JobSearch.Tests.Deduplication;

public sealed class DefaultJobDeduplicatorTests
{
    private readonly DefaultJobDeduplicator _deduplicator = new();

    [Fact]
    public void FindDuplicate_SameSourceId_MatchesExactly()
    {
        var existing = CreateStoredJob(CreatePosting());
        var incoming = CreatePosting() with
        {
            Id = Guid.NewGuid(),
            Url = new Uri("https://jobicy.com/jobs/changed-url")
        };

        var match = _deduplicator.FindDuplicate(incoming, [existing]);

        Assert.NotNull(match);
        Assert.Equal(existing.Posting.Id, match.JobId);
        Assert.Equal("source job ID", match.Reason);
    }

    [Fact]
    public void FindDuplicate_TrackingParametersDiffer_MatchesCanonicalUrl()
    {
        var existing = CreateStoredJob(CreatePosting());
        var incoming = CreatePosting() with
        {
            Id = Guid.NewGuid(),
            Source = "Other",
            SourceJobId = "other-1",
            Url = new Uri("https://jobicy.com/jobs/123?utm_source=other#apply")
        };

        var match = _deduplicator.FindDuplicate(incoming, [existing]);

        Assert.NotNull(match);
        Assert.Equal("canonical URL", match.Reason);
    }

    [Fact]
    public void FindDuplicate_SimilarCompanyTitleAndLocation_MatchesFuzzily()
    {
        var existing = CreateStoredJob(CreatePosting());
        var incoming = CreatePosting() with
        {
            Id = Guid.NewGuid(),
            Source = "Lever",
            SourceJobId = "lever-9",
            Url = new Uri("https://jobs.lever.co/acme/9"),
            Company = "Acme, Inc.",
            Title = "Sr. .NET Platform Engineer"
        };

        var match = _deduplicator.FindDuplicate(incoming, [existing]);

        Assert.NotNull(match);
        Assert.Equal("similar company, title, and location", match.Reason);
    }

    private static StoredJob CreateStoredJob(JobPosting posting) => new(
        posting,
        80,
        [],
        posting.DateFoundUtc,
        posting.DateFoundUtc,
        [new JobSourceReference(posting.Source, posting.SourceBoard, posting.SourceFeedKey, posting.SourceJobId, posting.Url, posting.DateFoundUtc, posting.DateFoundUtc)]);

    private static JobPosting CreatePosting() => new()
    {
        Source = "Jobicy",
        SourceJobId = "123",
        Title = "Senior .NET Platform Engineer",
        Company = "Acme Corporation",
        Url = new Uri("https://jobicy.com/jobs/123?source=feed"),
        Location = "United States",
        CountryCode = "US",
        WorkLocationType = WorkLocationType.Remote
    };
}
