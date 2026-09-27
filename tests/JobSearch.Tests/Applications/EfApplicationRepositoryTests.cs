using JobSearch.Application.Applications;
using JobSearch.Application.Matching;
using JobSearch.Domain.JobPostings;
using JobSearch.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobSearch.Tests.Applications;

public sealed class EfApplicationRepositoryTests
{
    [Fact]
    public async Task NeedsInput_AnswerBank_ResumeAndEventHistoryArePersisted()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var context = new JobSearchDbContext(new DbContextOptionsBuilder<JobSearchDbContext>().UseSqlite(connection).Options);
        var jobs = new EfJobRepository(context, NullLogger<EfJobRepository>.Instance);
        await jobs.InitializeAsync();
        var posting = new JobPosting { Source = "Greenhouse", SourceJobId = "1", Title = "Senior .NET Engineer", Company = "Acme", Url = new Uri("https://job-boards.greenhouse.io/acme/jobs/1") };
        await jobs.UpsertAsync(posting, new JobScore(90, []), null, DateTimeOffset.UtcNow);
        var repository = new EfApplicationRepository(context);
        var now = DateTimeOffset.UtcNow;
        var application = await repository.CreateAsync(posting.Id, ApplicationPlatform.Greenhouse, posting.Url, "resume.pdf", AutomationMode.ReviewBeforeSubmit, now);
        application = await repository.TransitionAsync(application.Id, ApplicationWorkflowStatus.Prepared, now, "prepared");
        application = await repository.TransitionAsync(application.Id, ApplicationWorkflowStatus.Approved, now, "approved");
        application = await repository.TransitionAsync(application.Id, ApplicationWorkflowStatus.InProgress, now, "started");
        application = await repository.RecordAutomationResultAsync(application.Id, new AutomationResult("run-1", ApplicationWorkflowStatus.NeedsInput, ["email"], [], [new("Do you need sponsorship?", "select", ["Yes", "No"], true)], []), now);

        var question = Assert.Single(application.Questions);
        application = await repository.ResolveQuestionAsync(application.Id, question.Id, "No", now);
        var saved = await repository.SaveAnswerAsync(new(Guid.NewGuid(), question.Question, question.NormalizedQuestion, "No", AnswerType.YesNo, 1m, false, "confirmed", now));
        application = await repository.TransitionAsync(application.Id, ApplicationWorkflowStatus.InProgress, now, "resumed");
        application = await repository.RecordAutomationResultAsync(application.Id, new AutomationResult("run-2", ApplicationWorkflowStatus.ReadyToSubmit, ["email"], [question.Question], [], []), now);

        Assert.True(Assert.Single(application.Questions).IsResolved);
        Assert.Equal(ApplicationWorkflowStatus.ReadyToSubmit, application.Status);
        Assert.Equal("No", Assert.Single(await repository.GetAnswersAsync()).Answer);
        Assert.Equal(11, application.Events.Count);
        Assert.Equal(saved.NormalizedPattern, question.NormalizedQuestion);
    }
}
