using System.Net;
using System.Text;
using JobSearch.Application.Sources;
using JobSearch.Domain.JobPostings;
using JobSearch.Infrastructure.Sources;
using JobSearch.Infrastructure.Sources.Greenhouse;

namespace JobSearch.Tests.Sources;

public sealed class GreenhouseJobSourceTests
{
    [Fact]
    public async Task FetchAsync_NormalizesBoardJobAndDetectsTechnologySignals()
    {
        const string fixture = """
            {
              "jobs": [{
                "id": 4377523009,
                "title": ".NET/C# Engineer",
                "absolute_url": "https://job-boards.greenhouse.io/livefront/jobs/4377523009",
                "location": { "name": "Remote (USA)" },
                "content": "&lt;p&gt;Full-time direct hire; this is not a contract role. Build ASP.NET Core REST APIs using Azure, SQL Server, Angular, and microservices. Salary $145,000-$175,000 yearly.&lt;/p&gt;"
              }]
            }
            """;
        using var client = new HttpClient(new FixtureHandler(fixture));
        var source = new GreenhouseJobSource(
            client,
            new GreenhouseOptions(),
            new EmployerBoardOptions { Token = "livefront", Company = "Livefront" },
            TimeProvider.System);

        var result = await source.FetchAsync(new JobSourceRequest([".NET"], ["C#", "Azure"], "US", 20));

        var job = Assert.Single(result.Jobs);
        Assert.Equal("Greenhouse", job.Source);
        Assert.Equal("Livefront", job.SourceBoard);
        Assert.Equal("greenhouse:livefront", job.SourceFeedKey);
        Assert.Equal("US", job.CountryCode);
        Assert.Equal(WorkLocationType.Remote, job.WorkLocationType);
        Assert.Equal(EmploymentType.FullTime, job.EmploymentType);
        Assert.Contains("ASP.NET Core", job.Skills);
        Assert.Contains("Microservices", job.Skills);
        Assert.InRange(job.MinimumHourlyRate!.Value, 69m, 70m);
        Assert.DoesNotContain("&lt;", job.Description);
    }

    private sealed class FixtureHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }
}
