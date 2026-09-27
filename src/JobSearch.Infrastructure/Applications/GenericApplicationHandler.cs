using JobSearch.Application.Applications;

namespace JobSearch.Infrastructure.Applications;

public sealed class GenericApplicationHandler : IApplicationPlatformHandler
{
    private readonly DeterministicFormFiller filler = new();
    public ApplicationPlatform Platform => ApplicationPlatform.Generic;
    public bool CanHandle(Uri url) => true;
    public Task<AutomationResult> FillAsync(ApplicationAutomationContext context, CancellationToken cancellationToken) => filler.FillAsync(context, "Generic form");
}
