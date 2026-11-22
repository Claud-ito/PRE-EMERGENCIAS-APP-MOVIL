using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Univalle.Emergencias.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Jo03CoordinationAndDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeliveredAtUtc",
                table: "NotificationEntries",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AutoDueAtUtc",
                table: "Incidents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AutoPreferences",
                columns: table => new
                {
                    CoordinatorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutoPreferences", x => x.CoordinatorUserId);
                });

            migrationBuilder.CreateTable(
                name: "Callouts",
                columns: table => new
                {
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Callouts", x => x.IncidentId);
                });

            migrationBuilder.CreateTable(
                name: "Handoffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProposedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AcceptedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Handoffs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_AutoDueAtUtc",
                table: "Incidents",
                column: "AutoDueAtUtc",
                filter: "[AutoDueAtUtc] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Handoffs_IncidentId",
                table: "Handoffs",
                column: "IncidentId",
                unique: true,
                filter: "[AcceptedAtUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutoPreferences");

            migrationBuilder.DropTable(
                name: "Callouts");

            migrationBuilder.DropTable(
                name: "Handoffs");

            migrationBuilder.DropIndex(
                name: "IX_Incidents_AutoDueAtUtc",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "DeliveredAtUtc",
                table: "NotificationEntries");

            migrationBuilder.DropColumn(
                name: "AutoDueAtUtc",
                table: "Incidents");
        }
    }
}
