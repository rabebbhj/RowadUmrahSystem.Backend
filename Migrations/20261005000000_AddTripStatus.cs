using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RowadUmrahSystem.Web.Migrations
{
    public partial class AddTripStatus : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Trips",
                type: "TEXT",
                nullable: false,
                defaultValue: "pending");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                table: "Trips");
        }
    }
}
