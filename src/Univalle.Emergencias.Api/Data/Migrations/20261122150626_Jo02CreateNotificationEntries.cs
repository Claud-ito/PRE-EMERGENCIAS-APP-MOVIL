    using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Univalle.Emergencias.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Jo02CreateNotificationEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationEntries",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RegisteredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeliveryStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReadAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationEntries", x => x.EventId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationEntries_RecipientUserId_RegisteredAtUtc",
                table: "NotificationEntries",
                columns: new[] { "RecipientUserId", "RegisteredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationEntries");
        }
    }
}
