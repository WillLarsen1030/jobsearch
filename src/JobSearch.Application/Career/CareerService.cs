using JobSearch.Application.Applications;
using JobSearch.Application.Persistence;

namespace JobSearch.Application.Career;

public sealed class CareerService(
    ICareerProfileRepository careers,
    IResumeImportService importer,
    ICareerAnalysisService analyzer,
    IResumeTailoringService resumes,
    IJobRepository jobs,
    IApplicantProfileStore applicants,
    TimeProvider timeProvider,
    CareerOutputOptions outputOptions)
{
    public async Task<ResumeImportReview> ImportAsync(string path, CancellationToken cancellationToken = default)
    {
        var review = await importer.CreateReviewAsync(path, await careers.GetProfileAsync(cancellationToken), await applicants.GetAsync(cancellationToken), cancellationToken);
        return await careers.SaveImportAsync(review, cancellationToken);
    }

    public async Task<CareerProfile> ApproveImportAsync(Guid importId, CancellationToken cancellationToken = default)
    {
        var profile = await careers.ApproveImportAsync(importId, timeProvider.GetUtcNow(), cancellationToken);
        await SeedQuestionsAsync(profile, cancellationToken);
        return profile;
    }

    public async Task<ResumeArtifact> GenerateMasterAsync(CancellationToken cancellationToken = default)
    {
        var profile = await RequireProfileAsync(cancellationToken);
        var artifact = await resumes.GenerateMasterAsync(profile, await applicants.GetAsync(cancellationToken), outputOptions.Directory, cancellationToken);
        return await careers.SaveResumeAsync(artifact, cancellationToken);
    }

    public async Task<JobFitAnalysis> AnalyzeAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var profile = await RequireProfileAsync(cancellationToken);
        var job = await jobs.GetByIdAsync(jobId, cancellationToken) ?? throw new KeyNotFoundException($"Job '{jobId}' was not found.");
        return await careers.SaveFitAnalysisAsync(analyzer.Analyze(job.Posting, profile, timeProvider.GetUtcNow()), cancellationToken);
    }

    public async Task<ResumeArtifact> GenerateTailoredAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var profile = await RequireProfileAsync(cancellationToken);
        var job = await jobs.GetByIdAsync(jobId, cancellationToken) ?? throw new KeyNotFoundException($"Job '{jobId}' was not found.");
        var analysis = await careers.GetFitAnalysisAsync(jobId, cancellationToken) ?? await AnalyzeAsync(jobId, cancellationToken);
        var artifact = await resumes.GenerateTailoredAsync(job.Posting, profile, await applicants.GetAsync(cancellationToken), analysis, outputOptions.Directory, cancellationToken);
        return await careers.SaveResumeAsync(artifact, cancellationToken);
    }

    public async Task<CareerProfileQuestion> AnswerQuestionAsync(CareerProfileQuestion question, CancellationToken cancellationToken = default)
    {
        if (question.IsApproved && string.IsNullOrWhiteSpace(question.Answer))
            throw new InvalidOperationException("An approved Career Profile answer cannot be blank.");
        question.AnsweredAtUtc = string.IsNullOrWhiteSpace(question.Answer) ? null : timeProvider.GetUtcNow();
        return await careers.SaveQuestionAsync(question, cancellationToken);
    }

    private async Task<CareerProfile> RequireProfileAsync(CancellationToken cancellationToken) =>
        await careers.GetProfileAsync(cancellationToken) is { Status: CareerProfileStatus.Approved } profile
            ? profile
            : throw new InvalidOperationException("Review and approve the resume import before using CareerProfile evidence.");

    private async Task SeedQuestionsAsync(CareerProfile profile, CancellationToken cancellationToken)
    {
        if ((await careers.GetQuestionsAsync(cancellationToken)).Count > 0) return;
        var questions = new[]
        {
            new CareerProfileQuestion { Question = "Which projects had a measurable performance improvement, and what was the before/after result?", Category = "Performance", CreatedAtUtc = timeProvider.GetUtcNow() },
            new CareerProfileQuestion { Question = "What team sizes, mentoring, or technical leadership responsibilities can you verify?", Category = "Leadership", CreatedAtUtc = timeProvider.GetUtcNow() },
            new CareerProfileQuestion { Question = "Which systems did you own from architecture through deployment and production support?", Category = "Ownership", CreatedAtUtc = timeProvider.GetUtcNow() },
            new CareerProfileQuestion { Question = "What CI/CD pipelines or deployment processes did you personally build or own?", Category = "Delivery", CreatedAtUtc = timeProvider.GetUtcNow() }
        };
        foreach (var question in questions) await careers.SaveQuestionAsync(question, cancellationToken);
    }
}

public sealed record CareerOutputOptions(string Directory);
