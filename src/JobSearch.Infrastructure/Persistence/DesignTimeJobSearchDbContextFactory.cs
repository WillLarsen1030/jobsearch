using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobSearch.Infrastructure.Persistence;

public sealed class DesignTimeJobSearchDbContextFactory : IDesignTimeDbContextFactory<JobSearchDbContext>
{
    public JobSearchDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<JobSearchDbContext>()
            .UseSqlite("Data Source=jobsearch-design.db")
            .Options;
        return new JobSearchDbContext(options);
    }
}
