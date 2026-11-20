using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Univalle.Emergencias.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Al01IncidentsAndReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Incidents",
                columns: table => new
                {
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FloorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OpenedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CurrentCoordinatorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InitialAlertEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InitialAlertRecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InitialAlertState = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    InitialAlertDeliveryStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    InitialAlertAttempts = table.Column<int>(type: "int", nullable: false),
                    InitialAlertLastAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Incidents", x => x.IncidentId);
                });

            migrationBuilder.CreateTable(
                name: "IncidentReports",
                columns: table => new
                {
                    ReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReporterUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ModuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FloorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreliminarySeverity = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReportedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsInitial = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentReports", x => x.ReportId);
                    table.ForeignKey(
                        name: "FK_IncidentReports_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "IncidentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IncidentReports_IncidentId_ReportedAtUtc",
                table: "IncidentReports",
                columns: new[] { "IncidentId", "ReportedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_IncidentReports_ReporterUserId_ClientRequestId",
                table: "IncidentReports",
                columns: new[] { "ReporterUserId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_CurrentCoordinatorUserId_Status",
                table: "Incidents",
                columns: new[] { "CurrentCoordinatorUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_InitialAlertEventId",
                table: "Incidents",
                column: "InitialAlertEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_InitialAlertState_InitialAlertLastAttemptAtUtc",
                table: "Incidents",
                columns: new[] { "InitialAlertState", "InitialAlertLastAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Incidents_ActiveLocation",
                table: "Incidents",
                columns: new[] { "ModuleId", "FloorId" },
                unique: true,
                filter: "[Status] = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IncidentReports");

            migrationBuilder.DropTable(
                name: "Incidents");
        }
    }
}
