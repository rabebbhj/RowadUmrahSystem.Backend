using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RowadUmrahSystem.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupKhatma : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroupKhatmas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PackageId = table.Column<string>(type: "TEXT", nullable: false),
                    DepartureDate = table.Column<string>(type: "TEXT", nullable: false),
                    Dedication = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupKhatmas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GroupKhatmaHizbs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GroupKhatmaId = table.Column<int>(type: "INTEGER", nullable: false),
                    Hizb = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: true),
                    ReservedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReadAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupKhatmaHizbs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupKhatmaHizbs_GroupKhatmas_GroupKhatmaId",
                        column: x => x.GroupKhatmaId,
                        principalTable: "GroupKhatmas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroupKhatmaHizbs_GroupKhatmaId_Hizb",
                table: "GroupKhatmaHizbs",
                columns: new[] { "GroupKhatmaId", "Hizb" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupKhatmas_PackageId_DepartureDate_CompletedAt",
                table: "GroupKhatmas",
                columns: new[] { "PackageId", "DepartureDate", "CompletedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupKhatmaHizbs");

            migrationBuilder.DropTable(
                name: "GroupKhatmas");
        }
    }
}
