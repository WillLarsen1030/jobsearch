using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "FetchStates",
                columns: table => new
                {
                    Source = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LastSuccessfulFetchUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FetchStates", x => x.Source);
                });

            migrationBuilder.CreateTable(
                name: "JobStatusHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OldStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    NewStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobStatusHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobStatusHistory_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobStatusHistory_JobId_ChangedAtUtc",
                table: "JobStatusHistory",
                columns: new[] { "JobId", "ChangedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FetchStates");

            migrationBuilder.DropTable(
                name: "JobStatusHistory");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Jobs");
        }
    }
}
