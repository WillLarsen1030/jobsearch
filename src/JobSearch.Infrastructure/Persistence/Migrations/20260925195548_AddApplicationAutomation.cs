using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApplicationAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Pattern = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    NormalizedPattern = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Answer = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    AnswerType = table.Column<int>(type: "INTEGER", nullable: false),
                    Confidence = table.Column<decimal>(type: "TEXT", nullable: false),
                    AlwaysRequireConfirmation = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    LastConfirmedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationAnswers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Applications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Platform = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicationUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    ResumePath = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    AutomationMode = table.Column<int>(type: "INTEGER", nullable: false),
                    AutoSubmitApproved = table.Column<bool>(type: "INTEGER", nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    BrowserRunId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FieldsFilledJson = table.Column<string>(type: "TEXT", nullable: false),
                    WarningsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Applications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Applications_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationEvents_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Question = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    NormalizedQuestion = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    FieldType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    OptionsJson = table.Column<string>(type: "TEXT", nullable: false),
                    IsRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    Answer = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    IsResolved = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationQuestions_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationAnswers_NormalizedPattern",
                table: "ApplicationAnswers",
                column: "NormalizedPattern",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationEvents_ApplicationId_OccurredAtUtc",
                table: "ApplicationEvents",
                columns: new[] { "ApplicationId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationQuestions_ApplicationId_IsResolved",
                table: "ApplicationQuestions",
                columns: new[] { "ApplicationId", "IsResolved" });

            migrationBuilder.CreateIndex(
                name: "IX_Applications_JobId",
                table: "Applications",
                column: "JobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Applications_Status_UpdatedAtUtc",
                table: "Applications",
                columns: new[] { "Status", "UpdatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationAnswers");

            migrationBuilder.DropTable(
                name: "ApplicationEvents");

            migrationBuilder.DropTable(
                name: "ApplicationQuestions");

            migrationBuilder.DropTable(
                name: "Applications");
        }
    }
}
