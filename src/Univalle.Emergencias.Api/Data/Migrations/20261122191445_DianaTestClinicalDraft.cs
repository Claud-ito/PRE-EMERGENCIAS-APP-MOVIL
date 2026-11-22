using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Univalle.Emergencias.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DianaTestClinicalDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TestClinicalDraftJson",
                table: "InjuryRecords",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TestClinicalDraftJson",
                table: "InjuryRecords");
        }
    }
}
