using JobSearch.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace JobSearch.Infrastructure.Persistence;

public sealed class JobSearchDbContext(DbContextOptions<JobSearchDbContext> options) : DbContext(options)
{
    internal DbSet<JobEntity> Jobs => Set<JobEntity>();
    internal DbSet<JobSourceEntity> JobSources => Set<JobSourceEntity>();
    internal DbSet<JobStatusHistoryEntity> JobStatusHistory => Set<JobStatusHistoryEntity>();
    internal DbSet<FetchStateEntity> FetchStates => Set<FetchStateEntity>();
    internal DbSet<ApplicationEntity> Applications => Set<ApplicationEntity>();
    internal DbSet<ApplicationEventEntity> ApplicationEvents => Set<ApplicationEventEntity>();
    internal DbSet<ApplicationQuestionEntity> ApplicationQuestions => Set<ApplicationQuestionEntity>();
    internal DbSet<ApplicationAnswerEntity> ApplicationAnswers => Set<ApplicationAnswerEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var jobs = modelBuilder.Entity<JobEntity>();
        jobs.ToTable("Jobs");
        jobs.HasKey(job => job.Id);
        jobs.HasIndex(job => job.Score);
        jobs.HasIndex(job => job.FirstSeenUnixSeconds);
        jobs.HasIndex(job => new { job.NormalizedCompany, job.NormalizedTitle });
        jobs.Property(job => job.Title).HasMaxLength(500);
        jobs.Property(job => job.Company).HasMaxLength(300);
        jobs.Property(job => job.SourceBoard).HasMaxLength(300);
        jobs.Property(job => job.SourceFeedKey).HasMaxLength(200);
        jobs.Property(job => job.Url).HasMaxLength(2000);
        jobs.Property(job => job.CanonicalUrl).HasMaxLength(2000);
        jobs.Property(job => job.IsEligible).HasDefaultValue(true);
        jobs.Property(job => job.EligibilityProblemsJson).HasDefaultValue("[]");
        jobs.Property(job => job.EligibilityUnknownsJson).HasDefaultValue("[]");

        var sources = modelBuilder.Entity<JobSourceEntity>();
        sources.ToTable("JobSources");
        sources.HasKey(source => source.Id);
        sources.HasIndex(source => new { source.Source, source.Board, source.SourceKey }).IsUnique();
        sources.HasIndex(source => new { source.FeedKey, source.FirstSeenUnixSeconds });
        sources.Property(source => source.Source).HasMaxLength(100);
        sources.Property(source => source.Board).HasMaxLength(300);
        sources.Property(source => source.FeedKey).HasMaxLength(200);
        sources.Property(source => source.SourceKey).HasMaxLength(2000);
        sources.Property(source => source.OriginalUrl).HasMaxLength(2000);
        sources.HasOne(source => source.Job)
            .WithMany(job => job.Sources)
            .HasForeignKey(source => source.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        var history = modelBuilder.Entity<JobStatusHistoryEntity>();
        history.ToTable("JobStatusHistory");
        history.HasKey(change => change.Id);
        history.HasIndex(change => new { change.JobId, change.ChangedAtUtc });
        history.Property(change => change.Note).HasMaxLength(2000);
        history.HasOne(change => change.Job)
            .WithMany(job => job.StatusHistory)
            .HasForeignKey(change => change.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        var fetchStates = modelBuilder.Entity<FetchStateEntity>();
        fetchStates.ToTable("FetchStates");
        fetchStates.HasKey(state => state.Source);
        fetchStates.Property(state => state.Source).HasMaxLength(100);
        fetchStates.Property(state => state.SourceName).HasMaxLength(100);
        fetchStates.Property(state => state.Board).HasMaxLength(300);
        fetchStates.Property(state => state.LastFailure).HasMaxLength(2000);

        var applications = modelBuilder.Entity<ApplicationEntity>();
        applications.ToTable("Applications");
        applications.HasKey(application => application.Id);
        applications.HasIndex(application => application.JobId).IsUnique();
        applications.HasIndex(application => new { application.Status, application.UpdatedAtUtc });
        applications.Property(application => application.ApplicationUrl).HasMaxLength(2000);
        applications.Property(application => application.ResumePath).HasMaxLength(2000);
        applications.Property(application => application.FailureReason).HasMaxLength(2000);
        applications.Property(application => application.BrowserRunId).HasMaxLength(100);
        applications.HasOne(application => application.Job)
            .WithMany(job => job.Applications)
            .HasForeignKey(application => application.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        var events = modelBuilder.Entity<ApplicationEventEntity>();
        events.ToTable("ApplicationEvents");
        events.HasKey(applicationEvent => applicationEvent.Id);
        events.HasIndex(applicationEvent => new { applicationEvent.ApplicationId, applicationEvent.OccurredAtUtc });
        events.Property(applicationEvent => applicationEvent.Message).HasMaxLength(2000);
        events.HasOne(applicationEvent => applicationEvent.Application)
            .WithMany(application => application.Events)
            .HasForeignKey(applicationEvent => applicationEvent.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        var questions = modelBuilder.Entity<ApplicationQuestionEntity>();
        questions.ToTable("ApplicationQuestions");
        questions.HasKey(question => question.Id);
        questions.HasIndex(question => new { question.ApplicationId, question.IsResolved });
        questions.Property(question => question.Question).HasMaxLength(2000);
        questions.Property(question => question.NormalizedQuestion).HasMaxLength(2000);
        questions.Property(question => question.FieldType).HasMaxLength(100);
        questions.Property(question => question.Answer).HasMaxLength(4000);
        questions.HasOne(question => question.Application)
            .WithMany(application => application.Questions)
            .HasForeignKey(question => question.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        var answers = modelBuilder.Entity<ApplicationAnswerEntity>();
        answers.ToTable("ApplicationAnswers");
        answers.HasKey(answer => answer.Id);
        answers.HasIndex(answer => answer.NormalizedPattern).IsUnique();
        answers.Property(answer => answer.Pattern).HasMaxLength(1000);
        answers.Property(answer => answer.NormalizedPattern).HasMaxLength(1000);
        answers.Property(answer => answer.Answer).HasMaxLength(4000);
        answers.Property(answer => answer.Notes).HasMaxLength(2000);
    }
}
