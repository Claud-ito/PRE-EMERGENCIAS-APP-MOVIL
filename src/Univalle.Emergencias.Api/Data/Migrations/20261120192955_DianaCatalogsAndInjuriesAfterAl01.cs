using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Univalle.Emergencias.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DianaCatalogsAndInjuriesAfterAl01 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Brigades",
                columns: table => new
                {
                    BrigadeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    LeaderUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Brigades", x => x.BrigadeId);
                });

            migrationBuilder.CreateTable(
                name: "InjuryRecords",
                columns: table => new
                {
                    InjuryRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PhysicianUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InjuryRecords", x => x.InjuryRecordId);
                    table.ForeignKey(
                        name: "FK_InjuryRecords_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "IncidentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LocationModules",
                columns: table => new
                {
                    ModuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationModules", x => x.ModuleId);
                });

            migrationBuilder.CreateTable(
                name: "SupportContacts",
                columns: table => new
                {
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportContacts", x => x.ContactId);
                });

            migrationBuilder.CreateTable(
                name: "BrigadeMembers",
                columns: table => new
                {
                    BrigadeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Specialty = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrigadeMembers", x => new { x.BrigadeId, x.UserId });
                    table.ForeignKey(
                        name: "FK_BrigadeMembers_Brigades_BrigadeId",
                        column: x => x.BrigadeId,
                        principalTable: "Brigades",
                        principalColumn: "BrigadeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BrigadeModuleAssignments",
                columns: table => new
                {
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrigadeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaderUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MembersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrigadeModuleAssignments", x => x.AssignmentId);
                    table.ForeignKey(
                        name: "FK_BrigadeModuleAssignments_Brigades_BrigadeId",
                        column: x => x.BrigadeId,
                        principalTable: "Brigades",
                        principalColumn: "BrigadeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BrigadeModuleAssignments_LocationModules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "LocationModules",
                        principalColumn: "ModuleId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LocationFloors",
                columns: table => new
                {
                    FloorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationFloors", x => x.FloorId);
                    table.ForeignKey(
                        name: "FK_LocationFloors_LocationModules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "LocationModules",
                        principalColumn: "ModuleId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BrigadeModuleAssignments_BrigadeId",
                table: "BrigadeModuleAssignments",
                column: "BrigadeId");

            migrationBuilder.CreateIndex(
                name: "IX_BrigadeModuleAssignments_ModuleId",
                table: "BrigadeModuleAssignments",
                column: "ModuleId",
                unique: true,
                filter: "[EndedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BrigadeModuleAssignments_ModuleId_StartedAtUtc",
                table: "BrigadeModuleAssignments",
                columns: new[] { "ModuleId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Brigades_Name",
                table: "Brigades",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InjuryRecords_IncidentId",
                table: "InjuryRecords",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_LocationFloors_ModuleId_Name",
                table: "LocationFloors",
                columns: new[] { "ModuleId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LocationModules_Code",
                table: "LocationModules",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrigadeMembers");

            migrationBuilder.DropTable(
                name: "BrigadeModuleAssignments");

            migrationBuilder.DropTable(
                name: "InjuryRecords");

            migrationBuilder.DropTable(
                name: "LocationFloors");

            migrationBuilder.DropTable(
                name: "SupportContacts");

            migrationBuilder.DropTable(
                name: "Brigades");

            migrationBuilder.DropTable(
                name: "LocationModules");
        }
    }
}
