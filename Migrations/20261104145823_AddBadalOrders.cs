using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RowadUmrahSystem.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddBadalOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BadalPerformers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: true),
                    FirstName = table.Column<string>(type: "TEXT", nullable: false),
                    FullName = table.Column<string>(type: "TEXT", nullable: false),
                    City = table.Column<string>(type: "TEXT", nullable: false),
                    Languages = table.Column<string>(type: "TEXT", nullable: false),
                    Rating = table.Column<double>(type: "REAL", nullable: false),
                    BadalCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Verified = table.Column<bool>(type: "INTEGER", nullable: false),
                    OwnUmrahDone = table.Column<bool>(type: "INTEGER", nullable: false),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BadalPerformers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BadalOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Reference = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ClientRequestId = table.Column<string>(type: "TEXT", nullable: true),
                    BeneficiaryFullName = table.Column<string>(type: "TEXT", nullable: false),
                    BeneficiaryGender = table.Column<string>(type: "TEXT", nullable: false),
                    BeneficiaryStatus = table.Column<string>(type: "TEXT", nullable: false),
                    BeneficiaryRelation = table.Column<string>(type: "TEXT", nullable: true),
                    PersonalDua = table.Column<string>(type: "TEXT", nullable: true),
                    PersonalDuaApproved = table.Column<bool>(type: "INTEGER", nullable: false),
                    PersonalDuaRejectedReason = table.Column<string>(type: "TEXT", nullable: true),
                    TimingType = table.Column<string>(type: "TEXT", nullable: false),
                    TimingDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeclarationAcceptedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", nullable: false),
                    CurrencyIso = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    PaymentInvoiceId = table.Column<string>(type: "TEXT", nullable: true),
                    PaymentId = table.Column<string>(type: "TEXT", nullable: true),
                    PaymentMethod = table.Column<string>(type: "TEXT", nullable: true),
                    PaymentStatus = table.Column<string>(type: "TEXT", nullable: false),
                    LastPaymentError = table.Column<string>(type: "TEXT", nullable: true),
                    PaymentReturnUrl = table.Column<string>(type: "TEXT", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PerformerId = table.Column<int>(type: "INTEGER", nullable: true),
                    AssignedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ScheduledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RefundedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RefundReason = table.Column<string>(type: "TEXT", nullable: true),
                    CertificateNumber = table.Column<string>(type: "TEXT", nullable: true),
                    CertificatePdfPath = table.Column<string>(type: "TEXT", nullable: true),
                    CertificateIssuedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BadalOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BadalOrders_BadalPerformers_PerformerId",
                        column: x => x.PerformerId,
                        principalTable: "BadalPerformers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BadalRiteEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BadalOrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    Rite = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Latitude = table.Column<double>(type: "REAL", nullable: true),
                    Longitude = table.Column<double>(type: "REAL", nullable: true),
                    AccuracyMeters = table.Column<double>(type: "REAL", nullable: true),
                    Zone = table.Column<string>(type: "TEXT", nullable: true),
                    Rounds = table.Column<int>(type: "INTEGER", nullable: true),
                    NiyyaText = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BadalRiteEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BadalRiteEvents_BadalOrders_BadalOrderId",
                        column: x => x.BadalOrderId,
                        principalTable: "BadalOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BadalMedia",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BadalRiteEventId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    StoragePath = table.Column<string>(type: "TEXT", nullable: false),
                    TakenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BadalMedia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BadalMedia_BadalRiteEvents_BadalRiteEventId",
                        column: x => x.BadalRiteEventId,
                        principalTable: "BadalRiteEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BadalMedia_BadalRiteEventId",
                table: "BadalMedia",
                column: "BadalRiteEventId");

            migrationBuilder.CreateIndex(
                name: "IX_BadalOrders_PerformerId",
                table: "BadalOrders",
                column: "PerformerId");

            migrationBuilder.CreateIndex(
                name: "IX_BadalRiteEvents_BadalOrderId",
                table: "BadalRiteEvents",
                column: "BadalOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BadalMedia");

            migrationBuilder.DropTable(
                name: "BadalRiteEvents");

            migrationBuilder.DropTable(
                name: "BadalOrders");

            migrationBuilder.DropTable(
                name: "BadalPerformers");
        }
    }
}
