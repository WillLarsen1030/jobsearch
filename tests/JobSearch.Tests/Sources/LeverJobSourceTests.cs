using System.Net;
using System.Text;
using JobSearch.Application.Sources;
using JobSearch.Domain.JobPostings;
using JobSearch.Infrastructure.Sources;
using JobSearch.Infrastructure.Sources.Lever;

namespace JobSearch.Tests.Sources;

public sealed class LeverJobSourceTests
{
    [Fact]
    public async Task FetchAsync_NormalizesRemoteContractAndStructuredSalary()
    {
        const string fixture = """
            [{
              "id": "4fb4e69f-9d5d-40ed-8983-dc24334c3b22",
              "text": "Senior .NET AppDev Consultant",
              "hostedUrl": "https://jobs.lever.co/trility/4fb4e69f-9d5d-40ed-8983-dc24334c3b22",
              "descriptionPlain": "Build C# and ASP.NET Core APIs with Angular and PostgreSQL. Flexible schedule, 15-25 hours per week.",
              "country": "US",
              "workplaceType": "remote",
              "createdAt": 1790272800000,
              "categories": { "commitment": "Contract", "location": "United States", "allLocations": ["United States"] },
              "salaryRange": { "min": 75, "max": 90, "currency": "USD", "interval": "per-hour-salary" }
            }]
            """;
        using var client = new HttpClient(new FixtureHandler(fixture));
        var source = new LeverJobSource(
            client,
            new LeverOptions(),
            new EmployerBoardOptions { Token = "trility", Company = "Trility Consulting" },
            TimeProvider.System);

        var result = await source.FetchAsync(new JobSourceRequest([".NET"], ["C#", "Angular"], "US", 20));

        var job = Assert.Single(result.Jobs);
        Assert.Equal("Lever", job.Source);
        Assert.Equal("Trility Consulting", job.SourceBoard);
        Assert.Equal(EmploymentType.Contract, job.EmploymentType);
        Assert.Equal(WorkLocationType.Remote, job.WorkLocationType);
        Assert.Equal(20, job.EstimatedHoursPerWeek);
        Assert.Equal(75m, job.MinimumHourlyRate);
        Assert.Equal(90m, job.MaximumHourlyRate);
        Assert.Contains("C#", job.Skills);
        Assert.Contains("Angular", job.Skills);
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
