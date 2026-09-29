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
        var processedControls = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var controls = page.Locator("input:not([type=submit]):not([type=button]), textarea, select");
            var count = await controls.CountAsync();
            ILocator? control = null;
            ControlDescriptor? descriptor = null;
            for (var index = 0; index < count; index++)
            {
                var candidate = controls.Nth(index);
                var candidateDescriptor = await DescribeAsync(candidate);
                if (!processedControls.Add(candidateDescriptor.Identity)) continue;
                control = candidate;
                descriptor = candidateDescriptor;
                break;
            }

            if (control is null || descriptor is null) break;
            if (descriptor.Type == "file")
            {
                if (IsResume(descriptor.Label, descriptor.Name))
                    await UploadResumeAsync(control, descriptor, context.ResumePath, fieldsFilled, unknown, warnings);
                continue;
            }

            if (descriptor.IsImplementationDetail) continue;

            if (!await control.IsVisibleAsync()) continue;

            var profileValue = ProfileValue(descriptor.Label, context.Profile);
            if (!string.IsNullOrWhiteSpace(profileValue))
            {
                if (await SetValueAsync(control, descriptor, profileValue))
                    fieldsFilled.Add(FieldName(descriptor.Label));
                else if (descriptor.Required)
                    unknown.Add(await ToQuestionAsync(control, descriptor));
                continue;
            }

            if (IsKnownProfileField(descriptor.Label))
            {
                if (descriptor.Required) unknown.Add(await ToQuestionAsync(control, descriptor));
                continue;
            }

            var match = QuestionMatcher.Match(descriptor.Label, context.Answers);
            if (match.IsTrusted() && await SetValueAsync(control, descriptor, match.Answer!.Answer))
            {
                answersUsed.Add(descriptor.Label);
                continue;
            }

            if (match.IsTrusted())
                warnings.Add($"Could not select and verify the approved answer for {descriptor.Label}.");

            if (descriptor.Required) unknown.Add(await ToQuestionAsync(control, descriptor));
        }

        unknown = unknown.DistinctBy(question => QuestionMatcher.Normalize(question.Question)).ToList();
        if (context.Mode == AutomationMode.AutoSubmit)
            warnings.Add("Auto-submit execution is intentionally disabled; the completed form requires manual review.");
        warnings.Add($"{platformName} submit controls were not activated.");
        return new AutomationResult(context.RunId,
            unknown.Count > 0 ? ApplicationWorkflowStatus.NeedsInput : ApplicationWorkflowStatus.ReadyToSubmit,
            fieldsFilled.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), answersUsed.ToArray(), unknown, warnings);
    }

    private static async Task UploadResumeAsync(
        ILocator control,
        ControlDescriptor descriptor,
        string selectedResumePath,
        ICollection<string> fieldsFilled,
        ICollection<DetectedQuestion> unknown,
        ICollection<string> warnings)
    {
        var resolvedPath = string.IsNullOrWhiteSpace(selectedResumePath) ? string.Empty : Path.GetFullPath(selectedResumePath);
        if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
        {
            warnings.Add($"Resume upload failed: selected file does not exist ({selectedResumePath}).");
            if (descriptor.Required) unknown.Add(ToQuestion(descriptor));
            return;
        }

        var extension = Path.GetExtension(resolvedPath);
        if (!AllowsExtension(descriptor.Accept, extension))
        {
            warnings.Add($"Resume upload failed: {extension} is not allowed by the form ({descriptor.Accept}).");
            if (descriptor.Required) unknown.Add(ToQuestion(descriptor));
            return;
        }

        var uploadedInput = await control.ElementHandleAsync();
        if (uploadedInput is null)
        {
            warnings.Add($"Resume upload failed: the file input was no longer available for {selectedResumePath}.");
            if (descriptor.Required) unknown.Add(ToQuestion(descriptor));
            return;
        }

        await control.SetInputFilesAsync(resolvedPath);
        var selectedName = Path.GetFileName(resolvedPath);
        var uploadedName = await uploadedInput.EvaluateAsync<string>("element => element.files?.[0]?.name || ''");
        var reflectedName = control.Page.GetByText(selectedName, new PageGetByTextOptions { Exact = true });
        var reflected = await reflectedName.CountAsync() > 0 && await reflectedName.First.IsVisibleAsync();
        if (!string.Equals(uploadedName, selectedName, StringComparison.Ordinal) && !reflected)
        {
            warnings.Add($"Resume upload could not be verified for {selectedName}.");
            if (descriptor.Required) unknown.Add(ToQuestion(descriptor));
            return;
        }

        fieldsFilled.Add($"resume: {selectedName}");
    }

    private static bool AllowsExtension(string accept, string extension)
    {
        if (string.IsNullOrWhiteSpace(accept)) return true;
        var values = accept.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return values.Any(value => value == "*/*" || value.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<ControlDescriptor> DescribeAsync(ILocator control)
    {
        var tag = (await control.EvaluateAsync<string>("element => element.tagName.toLowerCase()") ?? "input").ToLowerInvariant();
        var type = (await control.GetAttributeAsync("type") ?? tag).ToLowerInvariant();
        var id = await control.GetAttributeAsync("id") ?? string.Empty;
        var name = await control.GetAttributeAsync("name") ?? string.Empty;
        var role = await control.GetAttributeAsync("role") ?? string.Empty;
        var cssClass = await control.GetAttributeAsync("class") ?? string.Empty;
        var ariaHidden = await control.GetAttributeAsync("aria-hidden") ?? string.Empty;
        var isImplementationDetail = type == "hidden" || ariaHidden.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            cssClass.Contains("requiredInput", StringComparison.OrdinalIgnoreCase);

        var label = await control.GetAttributeAsync("aria-label") ?? await control.GetAttributeAsync("placeholder") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(id))
        {
            var explicitLabel = control.Page.Locator($"label[for='{EscapeCss(id)}']").First;
            if (await explicitLabel.CountAsync() > 0) label = (await explicitLabel.InnerTextAsync()).Trim();
        }
        if (string.IsNullOrWhiteSpace(label))
        {
            var labelledBy = await control.GetAttributeAsync("aria-labelledby") ?? string.Empty;
            var labels = new List<string>();
            foreach (var labelId in labelledBy.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var labelledElement = control.Page.Locator($"#{EscapeCss(labelId)}").First;
                if (await labelledElement.CountAsync() > 0) labels.Add((await labelledElement.InnerTextAsync()).Trim());
            }
            label = string.Join(" ", labels);
        }
        if (string.IsNullOrWhiteSpace(label))
            label = (await control.EvaluateAsync<string>("element => element.closest('.field-wrapper')?.querySelector('label')?.innerText || element.closest('label')?.innerText || element.name || element.id || ''")).Trim();
        var groupRequired = false;
        if (type == "file")
        {
            var groupLabel = await control.EvaluateAsync<string>("element => { const group = element.closest('[role=group]'); const ids = (group?.getAttribute('aria-labelledby') || '').split(/\\s+/).filter(Boolean); return ids.map(id => document.getElementById(id)?.innerText || '').filter(Boolean).join(' '); }");
            if (!string.IsNullOrWhiteSpace(groupLabel)) label = groupLabel.Trim();
            groupRequired = await control.EvaluateAsync<bool>("element => element.closest('[role=group]')?.getAttribute('aria-required') === 'true'");
        }
        if (string.IsNullOrWhiteSpace(label) && type == "file") label = string.IsNullOrWhiteSpace(name) ? id : name;
        if (string.IsNullOrWhiteSpace(label)) label = "Unnamed required field";

        var required = await control.GetAttributeAsync("required") is not null ||
            string.Equals(await control.GetAttributeAsync("aria-required"), "true", StringComparison.OrdinalIgnoreCase) ||
            groupRequired || label.Contains('*');
        var options = tag == "select" ? await control.Locator("option").AllTextContentsAsync() : [];
        return new ControlDescriptor(
            string.Join("|", id, name, tag, type, QuestionMatcher.Normalize(label)),
            label.Trim().TrimEnd('*').Trim(),
            string.IsNullOrWhiteSpace(name) ? id : name,
            tag,
            type,
            role,
            await control.GetAttributeAsync("accept") ?? string.Empty,
            required,
            isImplementationDetail,
            options.Select(value => value.Trim()).Where(value => value.Length > 0).ToArray());
    }

    private static async Task<bool> SetValueAsync(ILocator control, ControlDescriptor descriptor, string value)
    {
        if (descriptor.Tag == "select")
        {
            var option = FindUnambiguousOption(descriptor.Options, value);
            if (option is null) return false;
            await control.SelectOptionAsync(new SelectOptionValue { Label = option });
            var selected = await control.Locator("option:checked").InnerTextAsync();
            return EquivalentOption(selected, option);
        }

        if (descriptor.Role.Equals("combobox", StringComparison.OrdinalIgnoreCase))
            return await SelectCustomOptionAsync(control, value);

        if (descriptor.Type is "checkbox" or "radio")
        {
            if (value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase)) await control.CheckAsync();
            else if (value.Equals("no", StringComparison.OrdinalIgnoreCase) || value.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                if (await control.IsCheckedAsync()) await control.UncheckAsync();
            }
            else return false;
            return true;
        }

        await control.FillAsync(value);
        return true;
    }

    private static async Task<bool> SelectCustomOptionAsync(ILocator control, string value)
    {
        await control.ClickAsync();
        await control.FillAsync(value);
        var optionLocator = control.Page.Locator("[role=option]:visible");
        try { await optionLocator.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 3_000 }); }
        catch (TimeoutException) { return false; }

        var options = (await optionLocator.AllTextContentsAsync()).Select(text => text.Trim()).Where(text => text.Length > 0).ToArray();
        var selected = FindUnambiguousOption(options, value);
        if (selected is null) return false;
        await optionLocator.Filter(new LocatorFilterOptions { HasTextString = selected }).First.ClickAsync();

        var selectedText = await control.EvaluateAsync<string>("element => { const shell = element.closest('.select-shell'); return shell?.querySelector('.select__single-value, .selected')?.textContent || shell?.innerText || element.value || ''; }");
        return FindUnambiguousOption([selectedText], value) is not null || FindUnambiguousOption([selected], selectedText) is not null;
    }

    private static string? FindUnambiguousOption(IEnumerable<string> values, string desired)
    {
        var options = values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var exact = options.Where(option => EquivalentOption(option, desired)).ToArray();
        if (exact.Length == 1) return exact[0];

        var desiredWords = QuestionMatcher.Normalize(desired).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var compatible = options.Where(option =>
        {
            var normalized = QuestionMatcher.Normalize(option);
            return normalized.Contains(QuestionMatcher.Normalize(desired), StringComparison.Ordinal) ||
                desiredWords.Length > 1 && desiredWords.All(word => normalized.Split(' ').Contains(word));
        }).ToArray();
        return compatible.Length == 1 ? compatible[0] : null;
    }

    private static bool EquivalentOption(string left, string right) =>
        QuestionMatcher.Normalize(left).Equals(QuestionMatcher.Normalize(right), StringComparison.Ordinal);

    private static async Task<DetectedQuestion> ToQuestionAsync(ILocator control, ControlDescriptor descriptor)
    {
        if (!descriptor.Role.Equals("combobox", StringComparison.OrdinalIgnoreCase)) return ToQuestion(descriptor);
        await control.ClickAsync();
        var optionLocator = control.Page.Locator("[role=option]:visible");
        IReadOnlyList<string> options = [];
        try
        {
            await optionLocator.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 1_500 });
            options = (await optionLocator.AllTextContentsAsync()).Select(text => text.Trim()).Where(text => text.Length > 0).Distinct().ToArray();
            await control.PressAsync("Escape");
        }
        catch (TimeoutException) { }
        return new DetectedQuestion(descriptor.Label, descriptor.Type, options, descriptor.Required);
    }

    private static string ProfileValue(string label, ApplicantProfile profile)
    {
        var value = QuestionMatcher.Normalize(label);
        if (value.Contains("preferred first name")) return profile.PreferredName;
        if (value.Contains("first name")) return profile.LegalFirstName;
        if (value.Contains("last name") || value.Contains("surname")) return profile.LegalLastName;
        if (value is "name" or "full name") return $"{profile.LegalFirstName} {profile.LegalLastName}".Trim();
        if (value.Contains("email")) return profile.Email;
        if (value.Contains("phone")) return profile.Phone;
        if (value.Contains("linkedin")) return profile.LinkedInUrl;
        if (value.Contains("github")) return profile.GitHubUrl;
        if (value.Contains("portfolio") || value.Contains("website")) return profile.PortfolioUrl;
        if (value.Contains("candidate location") || value.Contains("location city"))
            return string.Join(", ", new[] { profile.City, profile.State }.Where(part => !string.IsNullOrWhiteSpace(part)));
        if (value.Contains("city")) return profile.City;
        if (value is "state" or "state province") return profile.State;
        if (value.Contains("country")) return profile.Country;
        return string.Empty;
    }

    private static bool IsKnownProfileField(string label)
    {
        var normalized = QuestionMatcher.Normalize(label);
        return normalized is "name" or "full name" or "first name" or "last name" or "preferred first name" or
            "email" or "phone" or "city" or "state" or "state province" or "country" or "candidate location" or
            "location city" or "linkedin" or "linkedin profile" or "github" or "github profile" or "portfolio" or
            "portfolio url" or "website" or "personal website" or "resume" or "resume cv" or "cv";
    }

    private static bool IsResume(string label, string name) =>
        label.Contains("resume", StringComparison.OrdinalIgnoreCase) || label.Contains("cv", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("resume", StringComparison.OrdinalIgnoreCase) || name.Contains("cv", StringComparison.OrdinalIgnoreCase);

    private static string FieldName(string label) => QuestionMatcher.Normalize(label) switch { var value when value.Length > 60 => value[..60], var value => value };
    private static DetectedQuestion ToQuestion(ControlDescriptor value) => new(value.Label, value.Type, value.Options, value.Required);
    private static string EscapeCss(string value) => value.Replace("'", "\\'", StringComparison.Ordinal);

    private sealed record ControlDescriptor(
        string Identity,
        string Label,
        string Name,
        string Tag,
        string Type,
        string Role,
        string Accept,
        bool Required,
        bool IsImplementationDetail,
        IReadOnlyList<string> Options);
}
