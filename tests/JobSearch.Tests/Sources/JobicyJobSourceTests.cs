using System.Net;
using System.Text;
using JobSearch.Application.Sources;
using JobSearch.Domain.JobPostings;
using JobSearch.Infrastructure.Sources.Jobicy;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobSearch.Tests.Sources;

public sealed class JobicyJobSourceTests
{
    [Fact]
    public async Task FetchAsync_NormalizesRemoteJobWithoutInventingMissingValues()
    {
        using var httpClient = new HttpClient(new FixtureHandler(JobicyFixture));
        var source = new JobicyJobSource(
            httpClient,
            new JobicyOptions(),
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<JobicyJobSource>.Instance);
        var request = new JobSourceRequest(
            [".NET"],
            ["C#", ".NET", "ASP.NET Core", "Azure"],
            "US",
            25);

        var result = await source.FetchAsync(request);

        var job = Assert.Single(result.Jobs);
        Assert.Equal(1, result.RetrievedCount);
        Assert.Equal("Jobicy", job.Source);
        Assert.Equal("98765", job.SourceJobId);
        Assert.Equal(WorkLocationType.Remote, job.WorkLocationType);
        Assert.Equal(EmploymentType.Contract, job.EmploymentType);
        Assert.Equal("US", job.CountryCode);
        Assert.Equal(20, job.EstimatedHoursPerWeek);
        Assert.Null(job.MinimumHourlyRate);
        Assert.Null(job.MaximumHourlyRate);
        Assert.Contains("ASP.NET Core", job.Skills);
        Assert.DoesNotContain('<', job.Description);
    }

    [Fact]
    public async Task FetchAsync_ConvertsYearlySalaryToHourlyEstimate()
    {
        const string response = """
            {
              "jobs": [{
                "id": 42,
                "url": "https://jobicy.com/jobs/senior-dotnet",
                "jobTitle": "Senior .NET Engineer",
                "companyName": "Example",
                "jobType": ["full-time"],
                "jobGeo": "USA",
                "jobDescription": "<p>Remote C# work.</p>",
                "salaryMin": 104000,
                "salaryMax": 166400,
                "salaryCurrency": "USD",
                "salaryPeriod": "yearly"
              }]
            }
            """;
        using var httpClient = new HttpClient(new FixtureHandler(response));
        var source = new JobicyJobSource(
            httpClient,
            new JobicyOptions(),
            TimeProvider.System,
            NullLogger<JobicyJobSource>.Instance);

        var result = await source.FetchAsync(new JobSourceRequest([".NET"], ["C#", ".NET"], "US", 25));

        var job = Assert.Single(result.Jobs);
        Assert.Equal(50m, job.MinimumHourlyRate);
        Assert.Equal(80m, job.MaximumHourlyRate);
        Assert.Equal("USD 104000-166400 yearly", job.CompensationDescription);
    }

    [Fact]
    public async Task FetchAsync_IncidentalHybridMention_DoesNotOverrideRemoteClassification()
    {
        const string response = """
            {
              "jobs": [{
                "id": 43,
                "url": "https://jobicy.com/jobs/remote-dotnet",
                "jobTitle": "Remote .NET Engineer",
                "companyName": "Example",
                "jobType": ["full-time"],
                "jobGeo": "USA",
                "jobDescription": "<p>This is a remote role. Some sales roles use hybrid work.</p>"
              }]
            }
            """;
        using var httpClient = new HttpClient(new FixtureHandler(response));
        var source = new JobicyJobSource(
            httpClient,
            new JobicyOptions(),
            TimeProvider.System,
            NullLogger<JobicyJobSource>.Instance);

        var result = await source.FetchAsync(new JobSourceRequest([".NET"], [".NET"], "US", 25));

        Assert.Equal(WorkLocationType.Remote, Assert.Single(result.Jobs).WorkLocationType);
    }

    private const string JobicyFixture = """
        {
          "jobs": [{
            "id": 98765,
            "url": "https://jobicy.com/jobs/senior-csharp-contractor?ref=api",
            "jobTitle": "Senior C# / ASP.NET Core Contractor",
            "companyName": "Northwind Labs",
            "jobType": ["contract"],
            "jobGeo": "USA",
            "jobLevel": "Senior",
            "jobExcerpt": "Build APIs remotely.",
            "jobDescription": "<p>Build <strong>ASP.NET Core</strong> services on Azure.</p><p>15-25 hours per week.</p>",
            "pubDate": "2026-09-24T12:00:00Z"
          }]
        }
        """;

    private sealed class FixtureHandler(string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
