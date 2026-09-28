using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RowadUmrahSystem.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddTravelerProfileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FirstNameArabic",
                table: "Travelers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FatherNameArabic",
                table: "Travelers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GrandFatherNameArabic",
                table: "Travelers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FamilyNameArabic",
                table: "Travelers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstNameEnglish",
                table: "Travelers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FatherNameEnglish",
                table: "Travelers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GrandFatherNameEnglish",
                table: "Travelers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FamilyNameEnglish",
                table: "Travelers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Profession",
                table: "Travelers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BirthCountry",
                table: "Travelers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BirthCity",
                table: "Travelers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MaritalStatus",
                table: "Travelers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResidenceExpiryDate",
                table: "Travelers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "FirstNameArabic", table: "Travelers");
            migrationBuilder.DropColumn(name: "FatherNameArabic", table: "Travelers");
            migrationBuilder.DropColumn(name: "GrandFatherNameArabic", table: "Travelers");
            migrationBuilder.DropColumn(name: "FamilyNameArabic", table: "Travelers");
            migrationBuilder.DropColumn(name: "FirstNameEnglish", table: "Travelers");
            migrationBuilder.DropColumn(name: "FatherNameEnglish", table: "Travelers");
            migrationBuilder.DropColumn(name: "GrandFatherNameEnglish", table: "Travelers");
            migrationBuilder.DropColumn(name: "FamilyNameEnglish", table: "Travelers");
            migrationBuilder.DropColumn(name: "Profession", table: "Travelers");
            migrationBuilder.DropColumn(name: "BirthCountry", table: "Travelers");
            migrationBuilder.DropColumn(name: "BirthCity", table: "Travelers");
            migrationBuilder.DropColumn(name: "MaritalStatus", table: "Travelers");
            migrationBuilder.DropColumn(name: "ResidenceExpiryDate", table: "Travelers");
        }
    }
}
