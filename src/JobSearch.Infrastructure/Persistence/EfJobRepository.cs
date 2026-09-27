using System.Text.Json;
using System.Data;
using JobSearch.Application.Deduplication;
using JobSearch.Application.Matching;
using JobSearch.Application.Persistence;
using JobSearch.Application.Review;
using JobSearch.Domain.JobPostings;
using JobSearch.Infrastructure.Persistence.Entities;
using JobSearch.Infrastructure.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobSearch.Infrastructure.Persistence;

public sealed class EfJobRepository(
    JobSearchDbContext dbContext,
    ILogger<EfJobRepository> logger) : IJobRepository, IFetchStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string InitialMigrationId = "20260925185406_InitialPersistence";
    private bool initialized;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (initialized)
        {
            return;
        }

        await BaselineExistingDatabaseAsync(cancellationToken);
        await dbContext.Database.MigrateAsync(cancellationToken);
        await NormalizeLegacyDescriptionsAsync(cancellationToken);
        initialized = true;
    }

    public async Task<IReadOnlyList<StoredJob>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entities = await dbContext.Jobs
            .AsNoTracking()
            .Include(job => job.Sources)
            .Include(job => job.StatusHistory)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return entities
            .OrderByDescending(job => job.Score)
            .ThenByDescending(job => job.LastSeenUtc)
            .Select(ToStoredJob)
            .ToArray();
    }

    public async Task<PagedResult<StoredJob>> SearchAsync(
        JobListFilter filter,
        JobSort sort,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.Jobs.AsNoTracking().AsQueryable();
        query = query.Where(job => job.Score >= filter.MinimumScore);
        if (filter.Status is not null) query = query.Where(job => job.Status == (int)filter.Status);
        if (filter.WorkLocation is not null) query = query.Where(job => job.WorkLocationType == (int)filter.WorkLocation);
        if (filter.EmploymentType is not null) query = query.Where(job => job.EmploymentType == (int)filter.EmploymentType);
        if (filter.HasCompensation is true)
        {
            query = query.Where(job => job.MinimumHourlyRate != null || job.MaximumHourlyRate != null || job.CompensationDescription != "");
        }
        else if (filter.HasCompensation is false)
        {
            query = query.Where(job => job.MinimumHourlyRate == null && job.MaximumHourlyRate == null && job.CompensationDescription == "");
        }

        if (filter.DiscoveredAfterUtc is not null)
        {
            var discoveredAfter = filter.DiscoveredAfterUtc.Value.ToUnixTimeSeconds();
            query = query.Where(job => job.FirstSeenUnixSeconds >= discoveredAfter);
        }
        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var keyword = filter.Keyword.Trim().ToLower();
            query = query.Where(job =>
                job.Title.ToLower().Contains(keyword) ||
                job.Company.ToLower().Contains(keyword) ||
                job.Description.ToLower().Contains(keyword) ||
                job.SkillsJson.ToLower().Contains(keyword));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        query = sort switch
        {
            JobSort.Newest => query.OrderByDescending(job => job.FirstSeenUnixSeconds).ThenByDescending(job => job.Score),
            JobSort.Compensation => query.OrderByDescending(job => job.MaximumHourlyRate ?? job.MinimumHourlyRate).ThenByDescending(job => job.Score),
            JobSort.Company => query.OrderBy(job => job.Company).ThenByDescending(job => job.Score),
            JobSort.Title => query.OrderBy(job => job.Title).ThenByDescending(job => job.Score),
            _ => query.OrderByDescending(job => job.Score).ThenByDescending(job => job.FirstSeenUnixSeconds)
        };

        var entities = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(job => job.Sources)
            .Include(job => job.StatusHistory)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        return new PagedResult<StoredJob>(entities.Select(ToStoredJob).ToArray(), totalCount, page, pageSize);
    }

    public async Task<DashboardSummary> GetDashboardSummaryAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var last24Hours = nowUtc.AddHours(-24).ToUnixTimeSeconds();
        var last7Days = nowUtc.AddDays(-7).ToUnixTimeSeconds();
        return new DashboardSummary(
            await dbContext.Jobs.CountAsync(job => job.Status != (int)ApplicationStatus.Rejected && job.Status != (int)ApplicationStatus.Skipped && job.Status != (int)ApplicationStatus.Expired, cancellationToken),
            await dbContext.Jobs.CountAsync(job => job.Status == (int)ApplicationStatus.Unreviewed, cancellationToken),
            await dbContext.Jobs.CountAsync(job => job.Score >= 75 && job.IsEligible, cancellationToken),
            await dbContext.Jobs.CountAsync(job => job.Status == (int)ApplicationStatus.Interested, cancellationToken),
            await dbContext.Jobs.CountAsync(job => job.Status == (int)ApplicationStatus.Applied, cancellationToken),
            await dbContext.Jobs.CountAsync(job => job.Status == (int)ApplicationStatus.Interview, cancellationToken),
            await dbContext.Jobs.CountAsync(job => job.Status == (int)ApplicationStatus.Rejected || job.Status == (int)ApplicationStatus.Skipped, cancellationToken),
            await dbContext.Jobs.CountAsync(job => job.FirstSeenUnixSeconds >= last24Hours, cancellationToken),
            await dbContext.Jobs.CountAsync(job => job.FirstSeenUnixSeconds >= last7Days, cancellationToken));
    }

    public async Task<IReadOnlyList<SourceAnalytics>> GetSourceAnalyticsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var last24Hours = nowUtc.AddHours(-24).ToUnixTimeSeconds();
        var last7Days = nowUtc.AddDays(-7).ToUnixTimeSeconds();
        var jobStats = await dbContext.JobSources
            .AsNoTracking()
            .GroupBy(source => new { source.FeedKey, source.Source, source.Board })
            .Select(group => new
            {
                group.Key.FeedKey,
                group.Key.Source,
                group.Key.Board,
                Stored = group.Select(source => source.JobId).Distinct().Count(),
                Day = group.Sum(source => source.FirstSeenUnixSeconds >= last24Hours ? 1 : 0),
                Week = group.Sum(source => source.FirstSeenUnixSeconds >= last7Days ? 1 : 0),
                Average = group.Average(source => source.Job.Score),
                Strong = group.Sum(source => source.Job.Score >= 75 && source.Job.IsEligible ? 1 : 0)
            })
            .ToListAsync(cancellationToken);
        var states = await dbContext.FetchStates.AsNoTracking().ToDictionaryAsync(state => state.Source, cancellationToken);

        var analytics = jobStats.Select(stat =>
        {
            states.TryGetValue(stat.FeedKey, out var state);
            return new SourceAnalytics(
                stat.FeedKey,
                stat.Source,
                stat.Board,
                stat.Stored,
                stat.Day,
                stat.Week,
                stat.Average,
                stat.Strong,
                state?.LastSuccessfulFetchUtc,
                state?.LastFailedFetchUtc,
                state?.LastFailure ?? string.Empty);
        }).ToList();
        analytics.AddRange(states.Values
            .Where(state => analytics.All(value => value.SourceKey != state.Source))
            .Select(state => new SourceAnalytics(
                state.Source,
                state.SourceName,
                state.Board,
                0,
                0,
                0,
                0,
                0,
                state.LastSuccessfulFetchUtc,
                state.LastFailedFetchUtc,
                state.LastFailure)));
        return analytics.OrderBy(value => value.Source).ThenBy(value => value.Board).ToArray();
    }

    public async Task<StoredJob?> FindByIdPrefixAsync(string idPrefix, CancellationToken cancellationToken = default)
    {
        var entities = await dbContext.Jobs
            .AsNoTracking()
            .Include(job => job.Sources)
            .Include(job => job.StatusHistory)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        var matches = entities
            .Where(job => job.Id.ToString().StartsWith(idPrefix, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();

        return matches.Length switch
        {
            0 => null,
            1 => ToStoredJob(matches[0]),
            _ => throw new InvalidOperationException($"Job ID prefix '{idPrefix}' is ambiguous.")
        };
    }

    public async Task<StoredJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Jobs
            .AsNoTracking()
            .Include(job => job.Sources)
            .Include(job => job.StatusHistory)
            .AsSplitQuery()
            .SingleOrDefaultAsync(job => job.Id == id, cancellationToken);
        return entity is null ? null : ToStoredJob(entity);
    }

    public async Task<JobUpsertResult> UpsertAsync(
        JobPosting posting,
        JobScore score,
        Guid? existingJobId,
        DateTimeOffset seenAtUtc,
        CancellationToken cancellationToken = default)
    {
        var entity = existingJobId is null
            ? null
            : await dbContext.Jobs
                .Include(job => job.Sources)
                .SingleOrDefaultAsync(job => job.Id == existingJobId, cancellationToken);
        var isNew = entity is null;

        if (entity is null)
        {
            entity = new JobEntity
            {
                Id = posting.Id,
                FirstSeenUtc = seenAtUtc,
                FirstSeenUnixSeconds = seenAtUtc.ToUnixTimeSeconds(),
                Status = (int)posting.Status
            };
            dbContext.Jobs.Add(entity);
        }

        UpdateEntity(entity, posting, score, seenAtUtc, preserveTrackingState: !isNew);
        var addedSource = AddOrUpdateSource(entity, posting, seenAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogDebug(
            "{Action} job {JobId} from {Source}; provenance added: {AddedSource}",
            isNew ? "Created" : "Updated",
            entity.Id,
            posting.Source,
            addedSource);

        return new JobUpsertResult(ToStoredJob(entity), isNew, addedSource);
    }

    public async Task UpdateScoreAsync(Guid jobId, JobScore score, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Jobs.SingleAsync(job => job.Id == jobId, cancellationToken);
        entity.Score = score.Value;
        entity.ScoreReasonsJson = JsonSerializer.Serialize(score.Reasons, JsonOptions);
        entity.IsEligible = score.EligibilityAssessment.IsEligible;
        entity.EligibilityProblemsJson = JsonSerializer.Serialize(score.EligibilityAssessment.Problems, JsonOptions);
        entity.EligibilityUnknownsJson = JsonSerializer.Serialize(score.EligibilityAssessment.Unknowns, JsonOptions);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<StoredJob> ChangeStatusAsync(
        Guid jobId,
        ApplicationStatus newStatus,
        string? note,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Jobs
            .Include(job => job.Sources)
            .Include(job => job.StatusHistory)
            .AsSplitQuery()
            .SingleOrDefaultAsync(job => job.Id == jobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Job '{jobId}' was not found.");
        var oldStatus = (ApplicationStatus)entity.Status;
        if (oldStatus == newStatus)
        {
            return ToStoredJob(entity);
        }

        entity.Status = (int)newStatus;
        if (newStatus == ApplicationStatus.Applied && entity.DateAppliedUtc is null)
        {
            entity.DateAppliedUtc = changedAtUtc;
        }

        var history = new JobStatusHistoryEntity
        {
            Id = Guid.NewGuid(),
            JobId = entity.Id,
            Job = entity,
            OldStatus = (int)oldStatus,
            NewStatus = (int)newStatus,
            ChangedAtUtc = changedAtUtc,
            Note = note?.Trim() ?? string.Empty
        };
        entity.StatusHistory.Add(history);
        dbContext.JobStatusHistory.Add(history);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToStoredJob(entity);
    }

    public async Task<StoredJob> UpdateNotesAsync(
        Guid jobId,
        string notes,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Jobs
            .Include(job => job.Sources)
            .Include(job => job.StatusHistory)
            .AsSplitQuery()
            .SingleOrDefaultAsync(job => job.Id == jobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Job '{jobId}' was not found.");
        entity.Notes = notes.Trim();
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToStoredJob(entity);
    }

    public async Task<DateTimeOffset?> GetLastSuccessfulFetchAsync(
        string source,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.FetchStates
            .Where(state => state.Source == source)
            .Select(state => (DateTimeOffset?)state.LastSuccessfulFetchUtc)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task RecordSuccessfulFetchAsync(
        string sourceKey,
        string source,
        string board,
        DateTimeOffset fetchedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var state = await dbContext.FetchStates.SingleOrDefaultAsync(value => value.Source == sourceKey, cancellationToken);
        if (state is null)
        {
            state = new FetchStateEntity
            {
                Source = sourceKey
            };
            dbContext.FetchStates.Add(state);
        }

        state.SourceName = source;
        state.Board = board;
        state.LastSuccessfulFetchUtc = fetchedAtUtc;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordFailedFetchAsync(
        string sourceKey,
        string source,
        string board,
        DateTimeOffset failedAtUtc,
        string error,
        CancellationToken cancellationToken = default)
    {
        var state = await dbContext.FetchStates.SingleOrDefaultAsync(value => value.Source == sourceKey, cancellationToken);
        if (state is null)
        {
            state = new FetchStateEntity { Source = sourceKey };
            dbContext.FetchStates.Add(state);
        }

        state.SourceName = source;
        state.Board = board;
        state.LastFailedFetchUtc = failedAtUtc;
        state.LastFailure = error.Length <= 2000 ? error : error[..2000];

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void UpdateEntity(
        JobEntity entity,
        JobPosting posting,
        JobScore score,
        DateTimeOffset seenAtUtc,
        bool preserveTrackingState)
    {
        var existingStatus = entity.Status;
        var existingDateApplied = entity.DateAppliedUtc;
        var existingFollowUp = entity.FollowUpDate;

        entity.Source = posting.Source;
        entity.SourceBoard = posting.SourceBoard;
        entity.SourceFeedKey = posting.SourceFeedKey;
        entity.SourceJobId = posting.SourceJobId;
        entity.Title = posting.Title;
        entity.Company = posting.Company;
        entity.Url = posting.Url.ToString();
        entity.Description = posting.Description;
        entity.Location = posting.Location;
        entity.CountryCode = posting.CountryCode;
        entity.WorkLocationType = (int)posting.WorkLocationType;
        entity.EmploymentType = (int)posting.EmploymentType;
        entity.MinimumHourlyRate = posting.MinimumHourlyRate;
        entity.MaximumHourlyRate = posting.MaximumHourlyRate;
        entity.CompensationDescription = posting.CompensationDescription;
        entity.Currency = posting.Currency;
        entity.EstimatedHoursPerWeek = posting.EstimatedHoursPerWeek;
        entity.SkillsJson = JsonSerializer.Serialize(posting.Skills, JsonOptions);
        entity.RequiresSecurityClearance = posting.RequiresSecurityClearance;
        entity.IsLikelyStaffingAgency = posting.IsLikelyStaffingAgency;
        entity.DateFoundUtc = posting.DateFoundUtc;
        entity.DatePostedUtc = posting.DatePostedUtc;
        entity.NormalizedCompany = JobTextNormalizer.NormalizeCompany(posting.Company);
        entity.NormalizedTitle = JobTextNormalizer.NormalizeTitle(posting.Title);
        entity.NormalizedLocation = JobTextNormalizer.NormalizeLocation(posting.Location);
        entity.CanonicalUrl = JobTextNormalizer.CanonicalizeUrl(posting.Url);
        entity.LastSeenUtc = seenAtUtc;
        entity.Score = score.Value;
        entity.ScoreReasonsJson = JsonSerializer.Serialize(score.Reasons, JsonOptions);
        entity.IsEligible = score.EligibilityAssessment.IsEligible;
        entity.EligibilityProblemsJson = JsonSerializer.Serialize(score.EligibilityAssessment.Problems, JsonOptions);
        entity.EligibilityUnknownsJson = JsonSerializer.Serialize(score.EligibilityAssessment.Unknowns, JsonOptions);

        if (preserveTrackingState)
        {
            entity.Status = existingStatus;
            entity.DateAppliedUtc = existingDateApplied;
            entity.FollowUpDate = existingFollowUp;
        }
        else
        {
            entity.DateAppliedUtc = posting.DateAppliedUtc;
            entity.FollowUpDate = posting.FollowUpDate;
        }
    }

    private bool AddOrUpdateSource(JobEntity entity, JobPosting posting, DateTimeOffset seenAtUtc)
    {
        var sourceKey = string.IsNullOrWhiteSpace(posting.SourceJobId)
            ? JobTextNormalizer.CanonicalizeUrl(posting.Url)
            : posting.SourceJobId.Trim().ToLowerInvariant();
        var existing = entity.Sources.FirstOrDefault(source =>
            source.Source.Equals(posting.Source, StringComparison.OrdinalIgnoreCase) &&
            source.Board.Equals(posting.SourceBoard, StringComparison.OrdinalIgnoreCase) &&
            source.SourceKey == sourceKey);

        if (existing is not null)
        {
            existing.LastSeenUtc = seenAtUtc;
            existing.OriginalUrl = posting.Url.ToString();
            return false;
        }

        var source = new JobSourceEntity
        {
            Id = Guid.NewGuid(),
            JobId = entity.Id,
            Job = entity,
            Source = posting.Source,
            Board = posting.SourceBoard,
            FeedKey = posting.SourceFeedKey,
            SourceKey = sourceKey,
            SourceJobId = posting.SourceJobId,
            OriginalUrl = posting.Url.ToString(),
            FirstSeenUtc = seenAtUtc,
            FirstSeenUnixSeconds = seenAtUtc.ToUnixTimeSeconds(),
            LastSeenUtc = seenAtUtc
        };
        entity.Sources.Add(source);
        dbContext.JobSources.Add(source);
        return true;
    }

    private static StoredJob ToStoredJob(JobEntity entity)
    {
        var posting = new JobPosting
        {
            Id = entity.Id,
            Source = entity.Source,
            SourceBoard = entity.SourceBoard,
            SourceFeedKey = entity.SourceFeedKey,
            SourceJobId = entity.SourceJobId,
            Title = entity.Title,
            Company = entity.Company,
            Url = new Uri(entity.Url),
            Description = entity.Description,
            Location = entity.Location,
            CountryCode = entity.CountryCode,
            WorkLocationType = (WorkLocationType)entity.WorkLocationType,
            EmploymentType = (EmploymentType)entity.EmploymentType,
            MinimumHourlyRate = entity.MinimumHourlyRate,
            MaximumHourlyRate = entity.MaximumHourlyRate,
            CompensationDescription = entity.CompensationDescription,
            Currency = entity.Currency,
            EstimatedHoursPerWeek = entity.EstimatedHoursPerWeek,
            Skills = Deserialize<string[]>(entity.SkillsJson) ?? [],
            RequiresSecurityClearance = entity.RequiresSecurityClearance,
            IsLikelyStaffingAgency = entity.IsLikelyStaffingAgency,
            DateFoundUtc = entity.DateFoundUtc,
            DatePostedUtc = entity.DatePostedUtc,
            DateAppliedUtc = entity.DateAppliedUtc,
            FollowUpDate = entity.FollowUpDate,
            Status = (ApplicationStatus)entity.Status
        };
        var reasons = Deserialize<ScoreReason[]>(entity.ScoreReasonsJson) ?? [];
        var sources = entity.Sources.Select(source => new JobSourceReference(
            source.Source,
            source.Board,
            source.FeedKey,
            source.SourceJobId,
            new Uri(source.OriginalUrl),
            source.FirstSeenUtc,
            source.LastSeenUtc)).ToArray();

        var history = entity.StatusHistory
            .OrderByDescending(change => change.ChangedAtUtc)
            .Select(change => new JobStatusChange(
                change.Id,
                (ApplicationStatus)change.OldStatus,
                (ApplicationStatus)change.NewStatus,
                change.ChangedAtUtc,
                change.Note))
            .ToArray();

        return new StoredJob(
            posting,
            entity.Score,
            reasons,
            entity.FirstSeenUtc,
            entity.LastSeenUtc,
            sources,
            entity.Notes,
            history,
            new JobEligibility(
                entity.IsEligible,
                Deserialize<string[]>(entity.EligibilityProblemsJson) ?? [],
                Deserialize<string[]>(entity.EligibilityUnknownsJson) ?? []));
    }

    private static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOptions);

    private async Task NormalizeLegacyDescriptionsAsync(CancellationToken cancellationToken)
    {
        var entities = await dbContext.Jobs
            .Where(job => job.Description.Contains("<") || job.Description.Contains("&lt;") || job.CountryCode == "")
            .ToListAsync(cancellationToken);
        var changed = 0;
        foreach (var entity in entities)
        {
            var description = SourceNormalization.ToPlainText(entity.Description);
            if (description != entity.Description)
            {
                entity.Description = description;
                changed++;
            }

            if (string.IsNullOrWhiteSpace(entity.CountryCode))
            {
                var countryCode = SourceNormalization.DetectCountryCode(null, entity.Location, entity.Description);
                if (!string.IsNullOrWhiteSpace(countryCode))
                {
                    entity.CountryCode = countryCode;
                    changed++;
                }
            }

            if (entity.EmploymentType == (int)EmploymentType.Contract &&
                JobSignalDetector.ExplicitlyExcludesContract(entity.Description))
            {
                entity.EmploymentType = (int)EmploymentType.FullTime;
                changed++;
            }
        }

        if (changed > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Applied {ChangeCount} normalization corrections to previously stored jobs", changed);
        }
    }

    private async Task BaselineExistingDatabaseAsync(CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var tableCommand = connection.CreateCommand();
            tableCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Jobs';";
            var jobsTableExists = Convert.ToInt64(await tableCommand.ExecuteScalarAsync(cancellationToken)) > 0;
            if (!jobsTableExists)
            {
                return;
            }

            await using var createHistoryCommand = connection.CreateCommand();
            createHistoryCommand.CommandText = """
                CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                    "ProductVersion" TEXT NOT NULL
                );
                """;
            await createHistoryCommand.ExecuteNonQueryAsync(cancellationToken);

            await using var baselineCommand = connection.CreateCommand();
            baselineCommand.CommandText = """
                INSERT OR IGNORE INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ('20260925185406_InitialPersistence', '10.0.12');
                """;
            var inserted = await baselineCommand.ExecuteNonQueryAsync(cancellationToken);
            if (inserted > 0)
            {
                logger.LogInformation("Existing Phase 2 database recognized at migration baseline {MigrationId}", InitialMigrationId);
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }
}
