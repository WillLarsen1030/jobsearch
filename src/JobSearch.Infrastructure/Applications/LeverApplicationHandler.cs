using JobSearch.Application.Applications;

namespace JobSearch.Infrastructure.Applications;

public sealed class LeverApplicationHandler : IApplicationPlatformHandler
{
    private readonly DeterministicFormFiller filler = new();
    public ApplicationPlatform Platform => ApplicationPlatform.Lever;
    public bool CanHandle(Uri url) => ApplicationPlatformDetector.Detect(url) == Platform;
    public Task<AutomationResult> FillAsync(ApplicationAutomationContext context, CancellationToken cancellationToken) => filler.FillAsync(context, "Lever");
}
