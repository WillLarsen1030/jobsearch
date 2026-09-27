using JobSearch.Application.Applications;
using JobSearch.Infrastructure.Applications;

namespace JobSearch.Tests.Applications;

public sealed class LocalApplicantProfileStoreTests
{
    [Fact]
    public async Task Store_PersistsProfileAndVerifiesSelectedResume()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var resume = Path.Combine(directory, "resume.pdf");
            await File.WriteAllTextAsync(resume, "fixture");
            var store = new LocalApplicantProfileStore(Path.Combine(directory, "profile.json"));
            var profile = new ApplicantProfile { LegalFirstName = "Test", LegalLastName = "Applicant", Email = "test@example.com", Phone = "555", ResumePath = resume };
            await store.SaveAsync(profile);

            var loaded = await store.GetAsync();

            Assert.Equal("Test", loaded.LegalFirstName);
            Assert.True(store.Validate(loaded).ResumeExists);
            Assert.Empty(store.Validate(loaded).MissingRequiredFacts);
        }
        finally { Directory.Delete(directory, true); }
    }
}
