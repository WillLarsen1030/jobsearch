using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpandDiscoveryAndEligibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JobSources_Source_SourceKey",
                table: "JobSources");

            migrationBuilder.AddColumn<string>(
                name: "Board",
                table: "JobSources",
                type: "TEXT",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FeedKey",
                table: "JobSources",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "FirstSeenUnixSeconds",
                table: "JobSources",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "EligibilityProblemsJson",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "EligibilityUnknownsJson",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<long>(
                name: "FirstSeenUnixSeconds",
                table: "Jobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "IsEligible",
                table: "Jobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceBoard",
                table: "Jobs",
                type: "TEXT",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceFeedKey",
                table: "Jobs",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "LastSuccessfulFetchUtc",
                table: "FetchStates",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "TEXT");

            migrationBuilder.AddColumn<string>(
                name: "Board",
                table: "FetchStates",
                type: "TEXT",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastFailedFetchUtc",
                table: "FetchStates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastFailure",
                table: "FetchStates",
                type: "TEXT",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceName",
                table: "FetchStates",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE "Jobs"
                SET "SourceBoard" = CASE WHEN "Source" = 'Jobicy' THEN 'Remote Jobs API' ELSE "Source" END,
                    "SourceFeedKey" = lower("Source"),
                    "FirstSeenUnixSeconds" = CAST(strftime('%s', "FirstSeenUtc") AS INTEGER);
                UPDATE "JobSources"
                SET "Board" = CASE WHEN "Source" = 'Jobicy' THEN 'Remote Jobs API' ELSE "Source" END,
                    "FeedKey" = lower("Source"),
                    "FirstSeenUnixSeconds" = CAST(strftime('%s', "FirstSeenUtc") AS INTEGER);
                UPDATE "FetchStates"
                SET "SourceName" = "Source",
                    "Board" = CASE WHEN "Source" = 'Jobicy' THEN 'Remote Jobs API' ELSE "Source" END,
                    "Source" = lower("Source");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_JobSources_FeedKey_FirstSeenUnixSeconds",
                table: "JobSources",
                columns: new[] { "FeedKey", "FirstSeenUnixSeconds" });

            migrationBuilder.CreateIndex(
                name: "IX_JobSources_Source_Board_SourceKey",
                table: "JobSources",
                columns: new[] { "Source", "Board", "SourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_FirstSeenUnixSeconds",
                table: "Jobs",
                column: "FirstSeenUnixSeconds");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JobSources_FeedKey_FirstSeenUnixSeconds",
                table: "JobSources");

            migrationBuilder.DropIndex(
                name: "IX_JobSources_Source_Board_SourceKey",
                table: "JobSources");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_FirstSeenUnixSeconds",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "Board",
                table: "JobSources");

            migrationBuilder.DropColumn(
                name: "FeedKey",
                table: "JobSources");

            migrationBuilder.DropColumn(
                name: "FirstSeenUnixSeconds",
                table: "JobSources");

            migrationBuilder.DropColumn(
                name: "EligibilityProblemsJson",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "EligibilityUnknownsJson",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "FirstSeenUnixSeconds",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "IsEligible",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "SourceBoard",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "SourceFeedKey",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "Board",
                table: "FetchStates");

            migrationBuilder.DropColumn(
                name: "LastFailedFetchUtc",
                table: "FetchStates");

            migrationBuilder.DropColumn(
                name: "LastFailure",
                table: "FetchStates");

            migrationBuilder.DropColumn(
                name: "SourceName",
                table: "FetchStates");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "LastSuccessfulFetchUtc",
                table: "FetchStates",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobSources_Source_SourceKey",
                table: "JobSources",
                columns: new[] { "Source", "SourceKey" },
                unique: true);
        }
    }
}
