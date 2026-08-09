using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RowadUmrahSystem.Web.Migrations
{
    /// <inheritdoc />
    public partial class UpgradeUserPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanArchiveDocuments",
                table: "UserPermissions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanArchiveTravelers",
                table: "UserPermissions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanBlockTravelers",
                table: "UserPermissions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanCreateTravelers",
                table: "UserPermissions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanCreateTrips",
                table: "UserPermissions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanEditTravelers",
                table: "UserPermissions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanExportReports",
                table: "UserPermissions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanRestoreDocuments",
                table: "UserPermissions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanRestoreTravelers",
                table: "UserPermissions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanUnblockTravelers",
                table: "UserPermissions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanUploadDocuments",
                table: "UserPermissions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanViewAuditLogs",
                table: "UserPermissions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanViewBlocks",
                table: "UserPermissions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanViewDocuments",
                table: "UserPermissions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanViewTravelers",
                table: "UserPermissions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanViewTrips",
                table: "UserPermissions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanArchiveDocuments",
                table: "UserPermissions");

            migrationBuilder.DropColumn(
                name: "CanArchiveTravelers",
                table: "UserPermissions");

            migrationBuilder.DropColumn(
                name: "CanBlockTravelers",
                table: "UserPermissions");

            migrationBuilder.DropColumn(
                name: "CanCreateTravelers",
                table: "UserPermissions");

            migrationBuilder.DropColumn(
                name: "CanCreateTrips",
                table: "UserPermissions");

            migrationBuilder.DropColumn(
                name: "CanEditTravelers",
                table: "UserPermissions");

            migrationBuilder.DropColumn(
                name: "CanExportReports",
                table: "UserPermissions");

            migrationBuilder.DropColumn(
                name: "CanRestoreDocuments",
                table: "UserPermissions");

            migrationBuilder.DropColumn(
                name: "CanRestoreTravelers",
                table: "UserPermissions");

            migrationBuilder.DropColumn(
                name: "CanUnblockTravelers",
                table: "UserPermissions");

            migrationBuilder.DropColumn(
                name: "CanUploadDocuments",
                table: "UserPermissions");

            migrationBuilder.DropColumn(
                name: "CanViewAuditLogs",
                table: "UserPermissions");

            migrationBuilder.DropColumn(
                name: "CanViewBlocks",
                table: "UserPermissions");

            migrationBuilder.DropColumn(
                name: "CanViewDocuments",
                table: "UserPermissions");

            migrationBuilder.DropColumn(
                name: "CanViewTravelers",
                table: "UserPermissions");

            migrationBuilder.DropColumn(
                name: "CanViewTrips",
                table: "UserPermissions");
        }
    }
}
