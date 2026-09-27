using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobSearch.Infrastructure.Configuration;

public static class JobSearchSettingsLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<JobSearchAppSettings> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<JobSearchAppSettings>(json, SerializerOptions)
            ?? throw new InvalidOperationException("Configuration could not be loaded.");
    }
}
