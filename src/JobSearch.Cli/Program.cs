using System.Net.Http.Headers;
using JobSearch.Application.Deduplication;
using JobSearch.Application.Applications;
using JobSearch.Application.Ingestion;
using JobSearch.Application.Matching;
using JobSearch.Application.Persistence;
using JobSearch.Application.Sources;
using JobSearch.Infrastructure.Configuration;
using JobSearch.Infrastructure.Applications;
using JobSearch.Infrastructure.Persistence;
using JobSearch.Infrastructure.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

var (command, commandArguments, configurationPath) = ParseArguments(args);
var settings = await LoadSettingsAsync(configurationPath);
settings.JobSearch.Validate();

using var loggerFactory = LoggerFactory.Create(builder => builder
    .SetMinimumLevel(LogLevel.Information)
    .AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    }));

var databasePath = ResolveDatabasePath(settings.Database.Path);
Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
var dbOptions = new DbContextOptionsBuilder<JobSearchDbContext>()
    .UseSqlite($"Data Source={databasePath}")
    .Options;
await using var dbContext = new JobSearchDbContext(dbOptions);
var repository = new EfJobRepository(dbContext, loggerFactory.CreateLogger<EfJobRepository>());
await repository.InitializeAsync();

switch (command)
{
    case "fetch":
        await FetchAsync(settings, repository, loggerFactory);
        await ListAsync(repository, ParseLimit(commandArguments, 10));
        break;
    case "list":
        await ListAsync(repository, ParseLimit(commandArguments, 20));
        break;
    case "show":
        await ShowAsync(repository, commandArguments);
        break;
    case "probe":
        await ProbeApplicationAsync(repository, settings, commandArguments, loggerFactory);
        break;
    case "help":
    case "--help":
    case "-h":
        PrintHelp();
        break;
    default:
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintHelp();
        Environment.ExitCode = 1;
        break;
}

static async Task FetchAsync(
    JobSearchAppSettings settings,
    EfJobRepository repository,
    ILoggerFactory loggerFactory)
{
    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("JobSearch", "1.0"));

    var sources = JobSourceFactory.Create(settings, _ => httpClient, TimeProvider.System, loggerFactory);
    var service = new JobIngestionService(
        repository,
        new DefaultJobDeduplicator(),
        new DefaultJobScorer(),
        TimeProvider.System,
        loggerFactory.CreateLogger<JobIngestionService>());
    var countryCode = settings.JobSearch.AllowedCountryCodes.FirstOrDefault() ?? string.Empty;
    var request = new JobSourceRequest(
        settings.JobSearch.SearchTerms,
        settings.JobSearch.PreferredKeywords,
        countryCode,
        settings.JobSearch.MaximumResultsPerSource);

    var coordinator = new JobFetchCoordinator(
        new JobSourceCatalog(sources),
        service,
        repository,
        TimeProvider.System,
        loggerFactory.CreateLogger<JobFetchCoordinator>());
    var summary = await coordinator.FetchAsync(request, settings.JobSearch);
    Console.WriteLine();
    Console.WriteLine(
        $"Fetch complete: {summary.Retrieved} retrieved, {summary.Normalized} normalized, " +
        $"{summary.Duplicates} duplicates, {summary.NewJobs} new, {summary.UpdatedJobs} updated, " +
        $"{summary.FailedSources} source failures across {summary.Sources.Count} sources.");
    foreach (var source in summary.Sources)
    {
        Console.WriteLine($"  {source.Source} / {source.Board}: {source.Status}, {source.Retrieved} retrieved, {source.NewJobs} new, {source.UpdatedJobs} updated");
    }
    Console.WriteLine();
}

static async Task ListAsync(IJobRepository repository, int limit)
{
    var jobs = (await repository.GetAllAsync()).Take(limit).ToArray();
    if (jobs.Length == 0)
    {
        Console.WriteLine("No jobs are stored. Run 'jobsearch fetch' first.");
        return;
    }

    Console.WriteLine($"{"ID",-8} {"Score",5}  {"Company",-24} {"Title",-42} {"Compensation",-24} {"Type",-10} Source");
    Console.WriteLine(new string('-', 134));
    foreach (var job in jobs)
    {
        var sources = string.Join(',', job.Sources.Select(source => source.Source).Distinct(StringComparer.OrdinalIgnoreCase));
        Console.WriteLine(
            $"{job.Posting.Id.ToString()[..8],-8} {job.Score,5}  " +
            $"{Truncate(job.Posting.Company, 24),-24} " +
            $"{Truncate(job.Posting.Title, 42),-42} " +
            $"{Truncate(FormatCompensation(job), 24),-24} " +
            $"{job.Posting.EmploymentType,-10} {sources}");
    }
}

static async Task ShowAsync(IJobRepository repository, IReadOnlyList<string> arguments)
{
    if (arguments.Count == 0)
    {
        Console.Error.WriteLine("Usage: jobsearch show <job-id-or-prefix>");
        Environment.ExitCode = 1;
        return;
    }

    var job = await repository.FindByIdPrefixAsync(arguments[0]);
    if (job is null)
    {
        Console.Error.WriteLine($"No job found with ID prefix '{arguments[0]}'.");
        Environment.ExitCode = 1;
        return;
    }

    Console.WriteLine($"ID:            {job.Posting.Id}");
    Console.WriteLine($"Score:         {job.Score}/100");
    Console.WriteLine($"Status:        {job.Posting.Status}");
    Console.WriteLine($"Company:       {job.Posting.Company}");
    Console.WriteLine($"Title:         {job.Posting.Title}");
    Console.WriteLine($"Location:      {job.Posting.Location} ({job.Posting.WorkLocationType})");
    Console.WriteLine($"Country:       {ValueOrUnknown(job.Posting.CountryCode)}");
    Console.WriteLine($"Engagement:    {job.Posting.EmploymentType}");
    Console.WriteLine($"Compensation:  {FormatCompensation(job)}");
    Console.WriteLine($"Hours/week:    {job.Posting.EstimatedHoursPerWeek?.ToString() ?? "unknown"}");
    Console.WriteLine($"Posted:        {FormatDate(job.Posting.DatePostedUtc)}");
    Console.WriteLine($"First seen:    {job.FirstSeenUtc:u}");
    Console.WriteLine($"Last seen:     {job.LastSeenUtc:u}");
    Console.WriteLine($"Skills:        {(job.Posting.Skills.Count == 0 ? "none detected" : string.Join(", ", job.Posting.Skills))}");
    Console.WriteLine($"Clearance:     {(job.Posting.RequiresSecurityClearance ? "required" : "not detected")}");
    Console.WriteLine();
    Console.WriteLine("Score explanation:");
    foreach (var reason in job.ScoreReasons)
    {
        Console.WriteLine($"  {reason.Points,3:+#;-#;0}  {reason.Category}: {reason.Explanation}");
    }

    Console.WriteLine();
    Console.WriteLine("Sources:");
    foreach (var source in job.Sources)
    {
        Console.WriteLine($"  {source.Source} / {source.Board} [{source.SourceJobId}] {source.Url}");
    }

    Console.WriteLine();
    Console.WriteLine("Description:");
    Console.WriteLine(job.Posting.Description);
}

static async Task ProbeApplicationAsync(IJobRepository repository, JobSearchAppSettings settings, IReadOnlyList<string> arguments, ILoggerFactory loggerFactory)
{
    if (arguments.Count == 0)
    {
        Console.Error.WriteLine("Usage: jobsearch probe <job-id-or-prefix>");
        Environment.ExitCode = 1;
        return;
    }
    var job = await repository.FindByIdPrefixAsync(arguments[0]);
    if (job is null) { Console.Error.WriteLine($"No job found with ID prefix '{arguments[0]}'."); Environment.ExitCode = 1; return; }
    var profilePath = DatabasePathResolver.Resolve(settings.ApplicantProfile.Path, Directory.GetCurrentDirectory());
    var artifactPath = DatabasePathResolver.Resolve(settings.ApplicationAutomation.ArtifactDirectory, Directory.GetCurrentDirectory());
    var profileStore = new LocalApplicantProfileStore(profilePath);
    var profile = await profileStore.GetAsync();
    await using var automator = new PlaywrightApplicationAutomator(
        [new GreenhouseApplicationHandler(), new LeverApplicationHandler(), new GenericApplicationHandler()],
        new BrowserAutomationOptions(false, artifactPath),
        loggerFactory.CreateLogger<PlaywrightApplicationAutomator>());
    var platform = ApplicationPlatformDetector.Detect(job.Posting.Url);
    var result = await automator.RunAsync(new ApplicationAutomationRequest(Guid.NewGuid(), job.Posting.Url, platform,
        AutomationMode.ReviewBeforeSubmit, false, profile.ResumePath, profile, []));
    Console.WriteLine($"Live non-submitting probe: {job.Posting.Company} - {job.Posting.Title}");
    Console.WriteLine($"Platform: {platform}; result: {result.Status}; run: {result.RunId}");
    Console.WriteLine($"Fields filled: {(result.FieldsFilled.Count == 0 ? "none" : string.Join(", ", result.FieldsFilled))}");
    Console.WriteLine($"Unknown required fields: {(result.UnknownQuestions.Count == 0 ? "none" : string.Join(" | ", result.UnknownQuestions.Select(value => value.Question)))}");
    foreach (var warning in result.Warnings) Console.WriteLine($"Warning: {warning}");
    if (!string.IsNullOrWhiteSpace(result.FailureReason)) Console.WriteLine($"Failure: {result.FailureReason}");
}

static Task<JobSearchAppSettings> LoadSettingsAsync(string? requestedPath) =>
    JobSearchSettingsLoader.LoadAsync(requestedPath is null
        ? Path.Combine(AppContext.BaseDirectory, "appsettings.json")
        : Path.GetFullPath(requestedPath));

static (string Command, IReadOnlyList<string> Arguments, string? ConfigurationPath) ParseArguments(string[] args)
{
    var values = args.ToList();
    string? configurationPath = null;
    var configIndex = values.FindIndex(value => value.Equals("--config", StringComparison.OrdinalIgnoreCase));
    if (configIndex >= 0)
    {
        if (configIndex + 1 >= values.Count)
        {
            throw new ArgumentException("--config requires a file path.");
        }

        configurationPath = values[configIndex + 1];
        values.RemoveRange(configIndex, 2);
    }

    return (values.FirstOrDefault()?.ToLowerInvariant() ?? "help", values.Skip(1).ToArray(), configurationPath);
}

static string ResolveDatabasePath(string configuredPath) =>
    DatabasePathResolver.Resolve(configuredPath, Directory.GetCurrentDirectory());

static int ParseLimit(IReadOnlyList<string> arguments, int defaultValue) =>
    arguments.Count > 0 && int.TryParse(arguments[0], out var value) ? Math.Clamp(value, 1, 200) : defaultValue;

static string FormatCompensation(StoredJob job)
{
    if (!string.IsNullOrWhiteSpace(job.Posting.CompensationDescription))
    {
        return job.Posting.CompensationDescription;
    }

    return (job.Posting.MinimumHourlyRate, job.Posting.MaximumHourlyRate) switch
    {
        (null, null) => "unknown",
        (var minimum, null) => $"{job.Posting.Currency} {minimum:0.##}/hr",
        (null, var maximum) => $"{job.Posting.Currency} up to {maximum:0.##}/hr",
        (var minimum, var maximum) => $"{job.Posting.Currency} {minimum:0.##}-{maximum:0.##}/hr"
    };
}

static string FormatDate(DateTimeOffset? value) => value?.ToString("u") ?? "unknown";

static string ValueOrUnknown(string value) => string.IsNullOrWhiteSpace(value) ? "unknown" : value;

static string Truncate(string value, int maximumLength) =>
    value.Length <= maximumLength ? value : string.Concat(value.AsSpan(0, maximumLength - 3), "...");

static void PrintHelp()
{
    Console.WriteLine("JobSearch - local job discovery and tracking");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  jobsearch fetch [limit]       Retrieve, score, deduplicate, and save jobs");
    Console.WriteLine("  jobsearch list [limit]        List strongest matches first");
    Console.WriteLine("  jobsearch show <id-prefix>    Show a normalized posting and score details");
    Console.WriteLine("  jobsearch probe <id-prefix>   Headed, non-submitting application form probe");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --config <path>               Use an alternate JSON configuration file");
}
