# JobSearch

JobSearch is a local-first personal job discovery and application-tracking system for finding senior C#/.NET contract, fractional, and flexible remote work. It is intended to reduce repetitive searching and preparation while keeping every application and factual claim under the user's control.

## Guardrails

- Browser assistance never activates a submit control in the current implementation.
- AutoSubmit requires both a platform allow-list and per-application approval; disabled is the checked-in default.
- Generated resumes and answers must not invent skills, employment, education, or accomplishments.
- Credentials and API keys belong in local configuration or a secrets provider, never source control.
- Scoring is explainable: each score includes the positive and negative reasons that produced it.

## Current architecture

The solution is a small modular monolith targeting .NET 10:

```text
JobSearch.slnx
|- src/
|  |- JobSearch.Domain/       Normalized job and application-tracking types
|  |- JobSearch.Application/  Ingestion, deduplication, preferences, and scoring
|  |- JobSearch.Infrastructure/ SQLite persistence and external source adapters
|  |- JobSearch.Cli/          fetch/list/show command-line interface
|  `- JobSearch.Web/          interactive Blazor review dashboard
`- tests/
   `- JobSearch.Tests/        Unit tests for matching and configuration rules
```

Dependencies point inward: the application layer references the domain, infrastructure implements application interfaces, and the CLI is the composition root. Source-specific DTOs remain internal to infrastructure. SQLite stores canonical jobs and separate provenance rows so duplicate listings retain every source ID and original URL.

The Blazor Web App uses the same repository, ingestion coordinator, deduplicator, and scorer as the CLI. It adds database-backed filtering and pagination, dashboard summaries, source health, notes, review status, status history, persisted per-feed cooldowns, and a human-approved application workflow. Playwright automation lives in Infrastructure behind application-layer interfaces; Blazor components do not directly control browsers.

## Assisted applications

The application workflow is `Draft -> Prepared -> Approved -> InProgress -> NeedsInput/ReadyToSubmit -> Submitted`. Every transition and automation action is recorded. Greenhouse, Lever, and a conservative generic handler fill only known profile fields or high-confidence approved answer-bank matches. Unknown required fields stop the run in `NeedsInput`; the answer can be supplied once or saved for future matching before resuming.

The default mode is `ReviewBeforeSubmit`. Browser sessions are headed by default and remain available for manual review when practical. CAPTCHA, authentication, anti-bot controls, and ambiguous or sensitive questions require manual intervention. The implementation does not click Submit.

Applicant facts and the master resume path are stored in ignored `data/applicant-profile.json`. The safe committed shape is [config/applicant-profile.example.json](config/applicant-profile.example.json). Résumés, browser data, logs, and failure screenshots are also ignored. Only PDF and DOCX résumé paths are accepted by the profile UI.

## Job sources

- [Jobicy's public API](https://jobicy.com/jobs-rss-feed) supplies broad remote-job discovery with geography, industry, and keyword filters.
- [Greenhouse's Job Board API](https://docs.greenhouse.io/job-board.html) exposes published employer boards through documented, unauthenticated GET endpoints.
- [Lever's Postings API](https://github.com/lever/postings-api) exposes published employer postings through its documented public JSON API.

Greenhouse and Lever were selected because employers intentionally expose these feeds for public career sites; this avoids brittle scraping of aggregators. Curated boards are configured in `JobSources:Greenhouse:Boards` and `JobSources:Lever:Boards`, so employers can be enabled, added, or removed without code changes. Each board has its own persisted cooldown and failure state.

The default curated set focuses on active U.S.-remote engineering employers and current .NET-adjacent boards: Livefront, Chainguard, JetBrains, Patriot Software, StackAdapt, Trility Consulting, Lone Wolf Technologies, Filevine, and Aerostrat. A board need not have a current .NET opening; deterministic scoring and eligibility analysis rank its published jobs.

Sources are not polled more than once per hour by the dashboard. Salary, hours, remote eligibility, and other optional fields may be absent; missing values remain explicitly unknown rather than being treated as ineligible.

## Configuration

Search preferences live in each executable's `appsettings.json`. The checked-in defaults reflect the initial search: U.S. remote senior .NET work, 10-25 hours per week, and at least $50/hour. The local SQLite database defaults to `data/jobsearch.db` and is ignored by Git.

To keep personal overrides out of source control, create `appsettings.Local.json` and pass its path to the CLI:

```powershell
dotnet run --project src/JobSearch.Cli -- fetch --config src/JobSearch.Cli/appsettings.Local.json
```

Do not place API keys in the checked-in `appsettings.json` file.

## Build and run

```powershell
dotnet restore
dotnet build JobSearch.slnx
& tests/JobSearch.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
dotnet test JobSearch.slnx
dotnet run --project src/JobSearch.Cli -- fetch
dotnet run --project src/JobSearch.Cli -- list 20
dotnet run --project src/JobSearch.Cli -- show <job-id-or-prefix>
dotnet run --project src/JobSearch.Web
```

The dashboard opens at the URL printed by ASP.NET Core. For an explicit loopback-only production start, use:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Production"
dotnet run --project src/JobSearch.Web --configuration Release --no-launch-profile --urls http://127.0.0.1:5088
```

Configure factual applicant data and a résumé under **Applicant profile**, maintain reusable answers under **Answer bank**, then prepare an application from a job detail page. Review and approve the package before selecting **Start Filling**.

## Private runtime data

The entire `data/` directory is local-only and ignored by Git. It contains SQLite databases and backups, the real applicant profile, résumé files, screenshots, and application artifacts. Browser profiles, cookies, Playwright traces, tunnel credentials, local settings, certificates, and key files are also ignored. The committed `config/applicant-profile.example.json` contains structure and non-sensitive placeholders only.

Never move live profile data, résumé files, Cloudflare credentials, or the SQLite database into a tracked directory. The application does not store passwords and Cloudflare Access, not the application database, is the remote authentication boundary.

## Database migrations

EF Core migrations live under `src/JobSearch.Infrastructure/Persistence/Migrations`. Existing Phase 2 databases are recognized and stamped at the initial migration before additive review-workflow migrations run. The upgrade preserves canonical jobs and provenance; a local `data/jobsearch.phase2.backup.db` backup was created during the Phase 3 upgrade and remains ignored by Git.

Check migration consistency with:

```powershell
dotnet ef migrations has-pending-model-changes --project src/JobSearch.Infrastructure --startup-project src/JobSearch.Infrastructure
```

## Private Cloudflare access

Cloudflare Pages cannot host this application: Cloudflare's current Blazor guidance states that Blazor Server is incompatible with its edge model and supports Blazor WebAssembly instead. Workers also do not provide the persistent Windows process, local filesystem, or headed Chrome environment this application requires. The supported design is:

```text
Remote browser
  -> Cloudflare Access authentication
  -> Cloudflare Tunnel
  -> http://127.0.0.1:5088 on this Windows PC
  -> local SQLite, profile, resumes, Playwright, and Chrome
```

The tunnel is outbound-only; do not forward port 5088 on the router. Blazor's SignalR/WebSocket connection passes through the tunnel, while the origin remains bound to loopback. Create the Access application **before** publishing the tunnel hostname, because a tunnel hostname without Access is reachable publicly.

Prerequisites are an active domain in Cloudflare and an Access policy that allows only your identity. Then:

1. Install the connector: `winget install --id Cloudflare.cloudflared --exact`.
2. In Cloudflare Zero Trust, create a self-hosted Access application for the intended hostname and an Allow policy restricted to your email or identity provider. Leave the deny-by-default behavior in place.
3. Authenticate locally: `cloudflared tunnel login`.
4. Create the tunnel: `cloudflared tunnel create jobsearch`.
5. Copy `deploy/cloudflare/config.example.yml` to `%USERPROFILE%\.cloudflared\config.yml`, replacing placeholders locally.
6. Validate it: `cloudflared tunnel ingress validate`.
7. Only after Access exists, create DNS routing: `cloudflared tunnel route dns jobsearch <protected-hostname>`.
8. Start the loopback-bound web app, then run `cloudflared tunnel run jobsearch` in a second terminal.

Stop remote access with `Ctrl+C` in the tunnel terminal; localhost remains available while the web process is running. Stop the app with `Ctrl+C` in its terminal. If the connector is later installed as a Windows service, use `Stop-Service cloudflared` and `Start-Service cloudflared` from an elevated PowerShell session. Tunnel `cert.pem`, UUID credential JSON, and the real `config.yml` remain under `%USERPROFILE%\.cloudflared`, outside this repository.

Official references: [Blazor on Pages](https://developers.cloudflare.com/pages/framework-guides/deploy-a-blazor-site/), [private web applications](https://developers.cloudflare.com/cloudflare-one/setup/secure-private-apps/private-web-app/), [self-hosted Access applications](https://developers.cloudflare.com/cloudflare-one/access-controls/applications/http-apps/self-hosted-public-app/), and [Cloudflare Tunnel on Windows](https://developers.cloudflare.com/tunnel/features/locally-managed-tunnels/as-a-service/windows/).

## Planned increments

1. Harden platform handlers against additional real-world Greenhouse and Lever form variants.
2. Add optional tailored résumé files without modifying the factual master profile.
3. Consider enabling guarded submission per platform only after extensive reviewed runs.
4. Add follow-up and contact workflows after the assisted-application path is proven stable.
