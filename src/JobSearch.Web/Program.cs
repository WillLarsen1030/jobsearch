using System.Net.Http.Headers;
using System.Net;
using JobSearch.Application.Deduplication;
using JobSearch.Application.Applications;
using JobSearch.Application.Ingestion;
using JobSearch.Application.Matching;
using JobSearch.Application.Persistence;
using JobSearch.Application.Review;
using JobSearch.Application.Sources;
using JobSearch.Application.Career;
using JobSearch.Infrastructure.Configuration;
using JobSearch.Infrastructure.Applications;
using JobSearch.Infrastructure.Persistence;
using JobSearch.Infrastructure.Sources;
using JobSearch.Infrastructure.Career;
using JobSearch.Web.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);
var settings = await JobSearchSettingsLoader.LoadAsync(Path.Combine(builder.Environment.ContentRootPath, "appsettings.json"));
settings.JobSearch.Validate();
var databasePath = DatabasePathResolver.Resolve(settings.Database.Path, builder.Environment.ContentRootPath);
Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});
builder.Services.AddDbContext<JobSearchDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
builder.Services.AddScoped<EfJobRepository>();
builder.Services.AddScoped<IJobRepository>(services => services.GetRequiredService<EfJobRepository>());
builder.Services.AddScoped<IFetchStateStore>(services => services.GetRequiredService<EfJobRepository>());
builder.Services.AddScoped<IApplicationRepository, EfApplicationRepository>();
builder.Services.AddScoped<ICareerProfileRepository, EfCareerProfileRepository>();
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(settings.JobSearch);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IJobScorer, DefaultJobScorer>();
builder.Services.AddSingleton<IJobDeduplicator, DefaultJobDeduplicator>();
builder.Services.AddSingleton<JobReviewQueryService>();
builder.Services.AddSingleton<DashboardSummaryService>();
var profilePath = DatabasePathResolver.Resolve(settings.ApplicantProfile.Path, builder.Environment.ContentRootPath);
var artifactPath = DatabasePathResolver.Resolve(settings.ApplicationAutomation.ArtifactDirectory, builder.Environment.ContentRootPath);
builder.Services.AddSingleton<IApplicantProfileStore>(new LocalApplicantProfileStore(profilePath));
builder.Services.AddSingleton(new BrowserAutomationOptions(settings.ApplicationAutomation.Headless, artifactPath));
builder.Services.AddSingleton<IApplicationPlatformHandler, GreenhouseApplicationHandler>();
builder.Services.AddSingleton<IApplicationPlatformHandler, LeverApplicationHandler>();
builder.Services.AddSingleton<IApplicationPlatformHandler, GenericApplicationHandler>();
builder.Services.AddSingleton<IApplicationAutomator, PlaywrightApplicationAutomator>();
builder.Services.AddSingleton<IResumeImportService, DocxResumeImportService>();
builder.Services.AddSingleton<ICareerAnalysisService, DeterministicCareerAnalysisService>();
builder.Services.AddSingleton<IResumeTailoringService, OpenXmlResumeTailoringService>();
builder.Services.AddSingleton<IApplicationAnswerDraftingService, DeterministicApplicationAnswerDraftingService>();
builder.Services.AddSingleton(new CareerOutputOptions(DatabasePathResolver.Resolve(settings.Career.ResumeOutputDirectory, builder.Environment.ContentRootPath)));
builder.Services.AddSingleton(new AutomationPolicy(settings.ApplicationAutomation.AutoSubmitEnabledPlatforms
    .Select(value => Enum.TryParse<ApplicationPlatform>(value, true, out var platform) ? platform : ApplicationPlatform.Generic)
    .Where(platform => platform != ApplicationPlatform.Generic)
    .ToHashSet()));
builder.Services.AddScoped<ApplicationService>();
builder.Services.AddScoped<CareerService>();
foreach (var clientName in new[] { "Jobicy", "Greenhouse", "Lever" })
{
    builder.Services.AddHttpClient(clientName, client =>
    {
        client.Timeout = TimeSpan.FromSeconds(45);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("JobSearch", "1.0"));
    });
}
builder.Services.AddScoped(services => new JobSourceCatalog(JobSourceFactory.Create(
    settings,
    name => services.GetRequiredService<IHttpClientFactory>().CreateClient(name),
    services.GetRequiredService<TimeProvider>(),
    services.GetRequiredService<ILoggerFactory>())));
builder.Services.AddScoped<JobIngestionService>();
builder.Services.AddScoped<JobFetchCoordinator>();

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<IJobRepository>().InitializeAsync();
}

app.Run();
