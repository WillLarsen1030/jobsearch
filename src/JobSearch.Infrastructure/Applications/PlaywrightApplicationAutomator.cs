using System.Collections.Concurrent;
using JobSearch.Application.Applications;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace JobSearch.Infrastructure.Applications;

public sealed class PlaywrightApplicationAutomator(
    IEnumerable<IApplicationPlatformHandler> handlers,
    BrowserAutomationOptions options,
    ILogger<PlaywrightApplicationAutomator> logger) : IApplicationAutomator, IAsyncDisposable
{
    private readonly IReadOnlyList<IApplicationPlatformHandler> handlers = handlers.ToArray();
    private readonly ConcurrentDictionary<Guid, BrowserSession> sessions = new();
    private IPlaywright? playwright;
    private readonly SemaphoreSlim initializationLock = new(1, 1);

    public async Task<AutomationResult> RunAsync(ApplicationAutomationRequest request, CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid().ToString("N");
        var artifactDirectory = Path.Combine(options.ArtifactDirectory, request.ApplicationId.ToString("N"), runId);
        Directory.CreateDirectory(artifactDirectory);
        IPage? page = null;
        try
        {
            var session = await GetOrCreateSessionAsync(request.ApplicationId, cancellationToken);
            page = session.Page;
            if (!page.Url.Equals(request.Url.ToString(), StringComparison.OrdinalIgnoreCase))
                await page.GotoAsync(request.Url.ToString(), new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45_000 });
            var handler = handlers.FirstOrDefault(value => value.Platform == request.Platform)
                ?? handlers.First(value => value.Platform == ApplicationPlatform.Generic);
            logger.LogInformation("Starting headed application fill {RunId} with {Handler} for application {ApplicationId}", runId, handler.GetType().Name, request.ApplicationId);
            return await handler.FillAsync(new ApplicationAutomationContext(request.ApplicationId, request.Url, request.Mode,
                request.AutoSubmitApproved, request.ResumePath, request.Profile, request.Answers, page, runId, artifactDirectory), cancellationToken);
        }
        catch (Exception exception)
        {
            var screenshotPath = string.Empty;
            try
            {
                if (page is not null)
                {
                    screenshotPath = Path.Combine(artifactDirectory, "failure.png");
                    await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
                }
            }
            catch (Exception screenshotException) { logger.LogWarning(screenshotException, "Could not capture failure screenshot for {RunId}", runId); screenshotPath = string.Empty; }
            logger.LogError(exception, "Application automation {RunId} failed for {ApplicationId}", runId, request.ApplicationId);
            return new AutomationResult(runId, ApplicationWorkflowStatus.Failed, [], [], [],
                string.IsNullOrWhiteSpace(screenshotPath) ? [] : [$"Failure screenshot: {screenshotPath}"], exception.Message,
                string.IsNullOrWhiteSpace(screenshotPath) ? null : screenshotPath);
        }
    }

    public async Task<bool> FocusExistingSessionAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!sessions.TryGetValue(applicationId, out var session) || session.Page.IsClosed) return false;
        await session.Page.BringToFrontAsync();
        return true;
    }

    private async Task<BrowserSession> GetOrCreateSessionAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        if (sessions.TryGetValue(applicationId, out var existing) && !existing.Page.IsClosed) return existing;
        await initializationLock.WaitAsync(cancellationToken);
        try
        {
            playwright ??= await Playwright.CreateAsync();
            IBrowser browser;
            try
            {
                browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = options.Headless });
            }
            catch (PlaywrightException exception) when (!options.Headless && FindInstalledChromium() is { } installedBrowser)
            {
                logger.LogWarning(exception, "Bundled headed Chromium could not start; retrying with {BrowserPath}", installedBrowser);
                browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = false, ExecutablePath = installedBrowser });
            }
            var context = await browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1440, Height = 1000 } });
            var page = await context.NewPageAsync();
            var session = new BrowserSession(browser, context, page);
            sessions[applicationId] = session;
            return session;
        }
        finally { initializationLock.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in sessions.Values)
            await session.Browser.CloseAsync();
        playwright?.Dispose();
        initializationLock.Dispose();
    }

    private sealed record BrowserSession(IBrowser Browser, IBrowserContext Context, IPage Page);

    private static string? FindInstalledChromium()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
