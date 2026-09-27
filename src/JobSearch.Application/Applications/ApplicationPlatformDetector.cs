namespace JobSearch.Application.Applications;

public static class ApplicationPlatformDetector
{
    public static ApplicationPlatform Detect(Uri url)
    {
        var host = url.Host.ToLowerInvariant();
        if (host.Contains("greenhouse.io") || host.Contains("greenhouse.com")) return ApplicationPlatform.Greenhouse;
        if (host.Contains("lever.co")) return ApplicationPlatform.Lever;
        return ApplicationPlatform.Generic;
    }
}
