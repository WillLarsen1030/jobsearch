using JobSearch.Application.Applications;
using Microsoft.Playwright;

namespace JobSearch.Infrastructure.Applications;

internal sealed class DeterministicFormFiller
{
    public async Task<AutomationResult> FillAsync(ApplicationAutomationContext context, string platformName)
    {
        var page = (IPage)context.Page;
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        var fieldsFilled = new List<string>();
        var answersUsed = new List<string>();
        var unknown = new List<DetectedQuestion>();
        var warnings = new List<string>();
        var controls = page.Locator("input:not([type=hidden]):not([type=submit]):not([type=button]), textarea, select");
        var count = await controls.CountAsync();

        for (var index = 0; index < count; index++)
        {
            var control = controls.Nth(index);
            if (!await control.IsVisibleAsync()) continue;
            var descriptor = await DescribeAsync(control);
            if (descriptor.Type is "file")
            {
                if (IsResume(descriptor.Label) && File.Exists(context.ResumePath))
                {
                    await control.SetInputFilesAsync(context.ResumePath);
                    fieldsFilled.Add("resume");
                }
                else if (descriptor.Required) unknown.Add(ToQuestion(descriptor));
                continue;
            }

            var profileValue = ProfileValue(descriptor.Label, context.Profile);
            if (!string.IsNullOrWhiteSpace(profileValue))
            {
                if (await SetValueAsync(control, descriptor, profileValue)) fieldsFilled.Add(FieldName(descriptor.Label));
                continue;
            }

            if (IsKnownProfileField(descriptor.Label))
            {
                if (descriptor.Required) unknown.Add(ToQuestion(descriptor));
                continue;
            }

            var match = QuestionMatcher.Match(descriptor.Label, context.Answers);
            if (match.IsTrusted() && await SetValueAsync(control, descriptor, match.Answer!.Answer))
            {
                answersUsed.Add(descriptor.Label);
                continue;
            }

            if (descriptor.Required) unknown.Add(ToQuestion(descriptor));
        }

        unknown = unknown.DistinctBy(question => QuestionMatcher.Normalize(question.Question)).ToList();
        if (context.Mode == AutomationMode.AutoSubmit)
            warnings.Add("Auto-submit execution is intentionally disabled; the completed form requires manual review.");
        warnings.Add($"{platformName} submit controls were not activated.");
        return new AutomationResult(context.RunId,
            unknown.Count > 0 ? ApplicationWorkflowStatus.NeedsInput : ApplicationWorkflowStatus.ReadyToSubmit,
            fieldsFilled.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), answersUsed.ToArray(), unknown, warnings);
    }

    private static async Task<ControlDescriptor> DescribeAsync(ILocator control)
    {
        var type = (await control.GetAttributeAsync("type") ?? await control.EvaluateAsync<string>("element => element.tagName.toLowerCase()") ?? "text").ToLowerInvariant();
        var id = await control.GetAttributeAsync("id") ?? string.Empty;
        var label = await control.GetAttributeAsync("aria-label") ?? await control.GetAttributeAsync("placeholder") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(id))
        {
            var explicitLabel = control.Page.Locator($"label[for='{EscapeCss(id)}']").First;
            if (await explicitLabel.CountAsync() > 0) label = (await explicitLabel.InnerTextAsync()).Trim();
        }
        if (string.IsNullOrWhiteSpace(label))
            label = (await control.EvaluateAsync<string>("element => element.closest('label')?.innerText || element.name || element.id || ''")).Trim();
        if (string.IsNullOrWhiteSpace(label) && type == "file") label = "resume";
        if (string.IsNullOrWhiteSpace(label)) label = "Unnamed required field";
        var required = await control.GetAttributeAsync("required") is not null ||
            string.Equals(await control.GetAttributeAsync("aria-required"), "true", StringComparison.OrdinalIgnoreCase) || label.Contains('*');
        var options = type == "select" || await control.EvaluateAsync<string>("element => element.tagName.toLowerCase()") == "select"
            ? await control.Locator("option").AllTextContentsAsync() : [];
        return new ControlDescriptor(label.Trim().TrimEnd('*').Trim(), type, required, options.Select(value => value.Trim()).Where(value => value.Length > 0).ToArray());
    }

    private static async Task<bool> SetValueAsync(ILocator control, ControlDescriptor descriptor, string value)
    {
        var tag = await control.EvaluateAsync<string>("element => element.tagName.toLowerCase()");
        if (tag == "select")
        {
            var option = descriptor.Options.FirstOrDefault(item => item.Equals(value, StringComparison.OrdinalIgnoreCase))
                ?? descriptor.Options.FirstOrDefault(item => item.Contains(value, StringComparison.OrdinalIgnoreCase));
            if (option is null) return false;
            await control.SelectOptionAsync(new SelectOptionValue { Label = option });
            return true;
        }
        if (descriptor.Type is "checkbox" or "radio")
        {
            if (value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase)) await control.CheckAsync();
            else if (value.Equals("no", StringComparison.OrdinalIgnoreCase) || value.Equals("false", StringComparison.OrdinalIgnoreCase)) { if (await control.IsCheckedAsync()) await control.UncheckAsync(); }
            else return false;
            return true;
        }
        await control.FillAsync(value);
        return true;
    }

    private static string ProfileValue(string label, ApplicantProfile profile)
    {
        var value = QuestionMatcher.Normalize(label);
        if (value.Contains("first name")) return profile.LegalFirstName;
        if (value.Contains("last name") || value.Contains("surname")) return profile.LegalLastName;
        if (value is "name" or "full name") return $"{profile.LegalFirstName} {profile.LegalLastName}".Trim();
        if (value.Contains("email")) return profile.Email;
        if (value.Contains("phone")) return profile.Phone;
        if (value.Contains("linkedin")) return profile.LinkedInUrl;
        if (value.Contains("github")) return profile.GitHubUrl;
        if (value.Contains("portfolio") || value.Contains("website")) return profile.PortfolioUrl;
        if (value.Contains("city")) return profile.City;
        if (value is "state" or "state province") return profile.State;
        if (value.Contains("country")) return profile.Country;
        return string.Empty;
    }

    private static bool IsKnownProfileField(string label)
    {
        var normalized = QuestionMatcher.Normalize(label);
        return new[] { "name", "email", "phone", "linkedin", "github", "portfolio", "website", "city", "state", "country", "resume", "cv" }
            .Any(normalized.Contains);
    }

    private static bool IsResume(string label) => label.Contains("resume", StringComparison.OrdinalIgnoreCase) || label.Contains("cv", StringComparison.OrdinalIgnoreCase);
    private static string FieldName(string label) => QuestionMatcher.Normalize(label) switch { var value when value.Length > 60 => value[..60], var value => value };
    private static DetectedQuestion ToQuestion(ControlDescriptor value) => new(value.Label, value.Type, value.Options, value.Required);
    private static string EscapeCss(string value) => value.Replace("'", "\\'", StringComparison.Ordinal);
    private sealed record ControlDescriptor(string Label, string Type, bool Required, IReadOnlyList<string> Options);
}
