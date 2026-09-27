namespace JobSearch.Infrastructure.Configuration;

public static class DatabasePathResolver
{
    public static string Resolve(string configuredPath, string startDirectory)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "JobSearch.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.GetFullPath(configuredPath, directory?.FullName ?? startDirectory);
    }
}
