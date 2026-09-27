using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    SourceJobId = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Company = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Location = table.Column<string>(type: "TEXT", nullable: false),
                    CountryCode = table.Column<string>(type: "TEXT", nullable: false),
                    WorkLocationType = table.Column<int>(type: "INTEGER", nullable: false),
                    EmploymentType = table.Column<int>(type: "INTEGER", nullable: false),
                    MinimumHourlyRate = table.Column<decimal>(type: "TEXT", nullable: true),
                    MaximumHourlyRate = table.Column<decimal>(type: "TEXT", nullable: true),
                    CompensationDescription = table.Column<string>(type: "TEXT", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", nullable: false),
                    EstimatedHoursPerWeek = table.Column<int>(type: "INTEGER", nullable: true),
                    SkillsJson = table.Column<string>(type: "TEXT", nullable: false),
                    RequiresSecurityClearance = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsLikelyStaffingAgency = table.Column<bool>(type: "INTEGER", nullable: false),
                    DateFoundUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DatePostedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DateAppliedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    FollowUpDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    NormalizedCompany = table.Column<string>(type: "TEXT", nullable: false),
                    NormalizedTitle = table.Column<string>(type: "TEXT", nullable: false),
                    NormalizedLocation = table.Column<string>(type: "TEXT", nullable: false),
                    CanonicalUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    FirstSeenUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSeenUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    ScoreReasonsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SourceKey = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    SourceJobId = table.Column<string>(type: "TEXT", nullable: false),
                    OriginalUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    FirstSeenUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSeenUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobSources_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_NormalizedCompany_NormalizedTitle",
                table: "Jobs",
                columns: new[] { "NormalizedCompany", "NormalizedTitle" });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_Score",
                table: "Jobs",
                column: "Score");

            migrationBuilder.CreateIndex(
                name: "IX_JobSources_JobId",
                table: "JobSources",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_JobSources_Source_SourceKey",
                table: "JobSources",
                columns: new[] { "Source", "SourceKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobSources");

            migrationBuilder.DropTable(
                name: "Jobs");
        }
    }
}
