using System.Text.Json;
using JobSearch.Application.Applications;

namespace JobSearch.Infrastructure.Applications;

public sealed class LocalApplicantProfileStore(string path) : IApplicantProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<ApplicantProfile> GetAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            var initial = new ApplicantProfile
            {
                CurrentTitle = "Senior .NET/C# Developer",
                YearsOfExperience = 10,
                RemotePreference = "Remote",
                CompensationPreference = "$50+/hour; preferred $60-$80+/hour",
                TechnologySkills = ["C#", ".NET", "ASP.NET Core", "REST APIs", "Microservices", "SQL Server", "PostgreSQL", "MongoDB", "Redis", "React", "Angular", "Umbraco", "Git", "GitHub"]
            };
            await SaveAsync(initial, cancellationToken);
            return initial;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ApplicantProfile>(stream, JsonOptions, cancellationToken) ?? new ApplicantProfile();
    }

    public async Task SaveAsync(ApplicantProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = $"{path}.tmp";
        await using (var stream = File.Create(temporaryPath))
            await JsonSerializer.SerializeAsync(stream, profile, JsonOptions, cancellationToken);
        File.Move(temporaryPath, path, true);
    }

    public ProfileValidation Validate(ApplicantProfile profile)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(profile.LegalFirstName)) missing.Add("legal first name");
        if (string.IsNullOrWhiteSpace(profile.LegalLastName)) missing.Add("legal last name");
        if (string.IsNullOrWhiteSpace(profile.Email)) missing.Add("email");
        if (string.IsNullOrWhiteSpace(profile.Phone)) missing.Add("phone");
        var resumeExists = !string.IsNullOrWhiteSpace(profile.ResumePath) && File.Exists(profile.ResumePath);
        return new ProfileValidation(resumeExists, missing);
    }
}
