using System.Text.Json;
using JobSearch.Application.Career;
using JobSearch.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace JobSearch.Infrastructure.Persistence;

public sealed class EfCareerProfileRepository(JobSearchDbContext dbContext) : ICareerProfileRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CareerProfile?> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.CareerProfiles.AsNoTracking().OrderByDescending(value => value.Version).FirstOrDefaultAsync(cancellationToken);
        return entity is null ? null : Deserialize<CareerProfile>(entity.ProfileJson);
    }

    public async Task<CareerProfile> SaveProfileAsync(CareerProfile profile, CancellationToken cancellationToken = default)
    {
        var currentVersion = await dbContext.CareerProfiles.MaxAsync(value => (int?)value.Version, cancellationToken) ?? 0;
        profile.Id = Guid.NewGuid();
        profile.Version = Math.Max(profile.Version, currentVersion + 1);
        profile.UpdatedAtUtc = profile.UpdatedAtUtc == default ? DateTimeOffset.UtcNow : profile.UpdatedAtUtc;
        dbContext.CareerProfiles.Add(new CareerProfileEntity
        {
            Id = profile.Id,
            Version = profile.Version,
            Status = (int)profile.Status,
            ProfileJson = JsonSerializer.Serialize(profile, JsonOptions),
            UpdatedAtUtc = profile.UpdatedAtUtc
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return profile;
    }

    public async Task<ResumeImportReview?> GetLatestImportAsync(CancellationToken cancellationToken = default)
    {
        var entity = (await dbContext.ResumeImports.AsNoTracking().ToListAsync(cancellationToken)).MaxBy(value => value.CreatedAtUtc);
        return entity is null ? null : Deserialize<ResumeImportReview>(entity.ReviewJson);
    }

    public async Task<ResumeImportReview> SaveImportAsync(ResumeImportReview review, CancellationToken cancellationToken = default)
    {
        review.Id = review.Id == Guid.Empty ? Guid.NewGuid() : review.Id;
        var entity = await dbContext.ResumeImports.SingleOrDefaultAsync(value => value.Id == review.Id, cancellationToken);
        if (entity is null)
        {
            entity = new ResumeImportEntity { Id = review.Id };
            dbContext.ResumeImports.Add(entity);
        }
        entity.SourcePath = review.SourcePath;
        entity.CreatedAtUtc = review.CreatedAtUtc;
        entity.Status = (int)review.Status;
        entity.ReviewJson = JsonSerializer.Serialize(review, JsonOptions);
        await dbContext.SaveChangesAsync(cancellationToken);
        return review;
    }

    public async Task<CareerProfile> ApproveImportAsync(Guid importId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.ResumeImports.SingleOrDefaultAsync(value => value.Id == importId, cancellationToken)
            ?? throw new KeyNotFoundException($"Resume import '{importId}' was not found.");
        var review = Deserialize<ResumeImportReview>(entity.ReviewJson);
        review.Status = ResumeImportStatus.Approved;
        review.ProposedProfile.Status = CareerProfileStatus.Approved;
        review.ProposedProfile.UpdatedAtUtc = now;
        entity.Status = (int)review.Status;
        entity.ReviewJson = JsonSerializer.Serialize(review, JsonOptions);
        var profile = await SaveProfileAsync(review.ProposedProfile, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return profile;
    }

    public async Task<JobFitAnalysis?> GetFitAnalysisAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var entity = (await dbContext.JobFitAnalyses.AsNoTracking().Where(value => value.JobId == jobId).ToListAsync(cancellationToken))
            .MaxBy(value => value.CreatedAtUtc);
        return entity is null ? null : Deserialize<JobFitAnalysis>(entity.AnalysisJson);
    }

    public async Task<JobFitAnalysis> SaveFitAnalysisAsync(JobFitAnalysis analysis, CancellationToken cancellationToken = default)
    {
        dbContext.JobFitAnalyses.Add(new JobFitAnalysisEntity
        {
            Id = analysis.Id,
            JobId = analysis.JobPostingId,
            CareerProfileVersion = analysis.CareerProfileVersion,
            OverallFit = analysis.OverallFit,
            CreatedAtUtc = analysis.CreatedAtUtc,
            AnalysisJson = JsonSerializer.Serialize(analysis, JsonOptions)
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return analysis;
    }

    public async Task<ResumeArtifact?> GetResumeAsync(Guid? jobId, ResumeArtifactKind kind, CancellationToken cancellationToken = default)
    {
        var entity = (await dbContext.ResumeArtifacts.AsNoTracking()
            .Where(value => value.JobId == jobId && value.Kind == (int)kind).ToListAsync(cancellationToken))
            .MaxBy(value => value.GeneratedAtUtc);
        return entity is null ? null : Map(entity);
    }

    public async Task<ResumeArtifact> SaveResumeAsync(ResumeArtifact artifact, CancellationToken cancellationToken = default)
    {
        dbContext.ResumeArtifacts.Add(new ResumeArtifactEntity
        {
            Id = artifact.Id,
            JobId = artifact.JobPostingId,
            Kind = (int)artifact.Kind,
            FileName = artifact.FileName,
            FilePath = artifact.FilePath,
            GeneratedAtUtc = artifact.GeneratedAtUtc,
            CareerProfileVersion = artifact.CareerProfileVersion,
            EvidenceIdsJson = JsonSerializer.Serialize(artifact.EvidenceIds, JsonOptions)
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return artifact;
    }

    public async Task<IReadOnlyList<CareerProfileQuestion>> GetQuestionsAsync(CancellationToken cancellationToken = default) =>
        (await dbContext.CareerProfileQuestions.AsNoTracking().OrderBy(value => value.IsApproved)
            .Select(value => new CareerProfileQuestion
            {
                Id = value.Id,
                Question = value.Question,
                Category = value.Category,
                EmploymentId = value.EmploymentId,
                Answer = value.Answer,
                IsApproved = value.IsApproved,
                CreatedAtUtc = value.CreatedAtUtc,
                AnsweredAtUtc = value.AnsweredAtUtc
            }).ToListAsync(cancellationToken)).OrderBy(value => value.IsApproved).ThenBy(value => value.CreatedAtUtc).ToArray();

    public async Task<CareerProfileQuestion> SaveQuestionAsync(CareerProfileQuestion question, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.CareerProfileQuestions.SingleOrDefaultAsync(value => value.Id == question.Id, cancellationToken);
        if (entity is null)
        {
            entity = new CareerProfileQuestionEntity { Id = question.Id == Guid.Empty ? Guid.NewGuid() : question.Id };
            dbContext.CareerProfileQuestions.Add(entity);
        }
        entity.Question = question.Question; entity.Category = question.Category; entity.EmploymentId = question.EmploymentId;
        entity.Answer = question.Answer; entity.IsApproved = question.IsApproved; entity.CreatedAtUtc = question.CreatedAtUtc;
        entity.AnsweredAtUtc = question.AnsweredAtUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
        question.Id = entity.Id;
        return question;
    }

    private static T Deserialize<T>(string json) where T : new() => JsonSerializer.Deserialize<T>(json, JsonOptions) ?? new T();
    private static ResumeArtifact Map(ResumeArtifactEntity entity) => new()
    {
        Id = entity.Id,
        JobPostingId = entity.JobId,
        Kind = (ResumeArtifactKind)entity.Kind,
        FileName = entity.FileName,
        FilePath = entity.FilePath,
        GeneratedAtUtc = entity.GeneratedAtUtc,
        CareerProfileVersion = entity.CareerProfileVersion,
        EvidenceIds = JsonSerializer.Deserialize<List<Guid>>(entity.EvidenceIdsJson, JsonOptions) ?? []
    };
}
