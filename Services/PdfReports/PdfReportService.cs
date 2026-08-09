using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RowadUmrahSystem.Web.Models;

namespace RowadUmrahSystem.Web.Services.PdfReports
{
    public class PdfReportService : IPdfReportService
    {
        public PdfReportService()
        {
            var fontPath = @"C:\Windows\Fonts\arial.ttf";

            if (File.Exists(fontPath))
            {
                FontManager.RegisterFont(File.OpenRead(fontPath));
            }
        }
        public byte[] GenerateBlockedTravelersReport(List<Traveler> travelers)
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(25);
                    page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(10));

                    page.Header()
                        .AlignCenter()
                        .Text("تقرير المسافرين المحظورين")
                        .FontSize(22)
                        .Bold();

                    page.Content().Column(col =>
                    {
                        col.Item()
                            .AlignRight()
                            .Text($"عدد المحظورين: {travelers.Count}")
                            .Bold();

                        col.Item().PaddingTop(10);

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });

                            HeaderCell(table, "الاسم");
                            HeaderCell(table, "رقم الجواز");
                            HeaderCell(table, "الجنسية");
                            HeaderCell(table, "سبب الحظر");
                            HeaderCell(table, "تاريخ الحظر");

                            foreach (var traveler in travelers)
                            {
                                BodyCell(table, traveler.FullName);
                                BodyCell(table, traveler.PassportNumber);
                                BodyCell(table, traveler.Nationality);
                                BodyCell(table, traveler.BlockReason ?? "-");
                                BodyCell(table, traveler.BlockedAt?.ToString("yyyy-MM-dd") ?? "-");
                            }
                        });
                    });

                    page.Footer()
                        .AlignCenter()
                        .Text($"تم إنشاء التقرير بتاريخ {DateTime.Now:yyyy-MM-dd HH:mm}");
                });
            }).GeneratePdf();
        }


        public byte[] GenerateTravelerProfile(Traveler traveler)
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(25);
                    page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(10));

                    page.Header().Element(ComposeHeader);

                    page.Content().Column(col =>
                    {
                        col.Spacing(12);

                        col.Item().Element(c => ComposeStatusBox(c, traveler));

                        if (!string.IsNullOrEmpty(traveler.PassportImagePath))
                        {
                            var passportPath = GetFullPath(traveler.PassportImagePath);

                            if (File.Exists(passportPath))
                            {
                                col.Item().Element(c =>
                                    ComposeImageSection(c, "صورة الجواز", passportPath));
                            }
                        }

                        col.Item().Element(c => ComposeTravelerInfo(c, traveler));
                        col.Item().Element(c => ComposeTrips(c, traveler));
                        col.Item().Element(c => ComposeDocuments(c, traveler));
                    });

                    page.Footer().Element(ComposeFooter);
                });
            }).GeneratePdf();
        }

        private void ComposeHeader(IContainer container)
        {
            container
                .BorderBottom(1)
                .BorderColor(Colors.Grey.Lighten1)
                .PaddingBottom(10)
                .Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item()
                            .AlignRight()
                            .Text("رواد العمرة")
                            .FontSize(20)
                            .Bold();

                        col.Item()
                            .AlignRight()
                            .Text("تقرير بيانات المسافر")
                            .FontSize(14)
                            .FontColor(Colors.Grey.Darken2);
                    });

                    row.ConstantItem(120)
                        .AlignLeft()
                        .Text(DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
                        .FontSize(9)
                        .FontColor(Colors.Grey.Darken1);
                });
        }

        private void ComposeStatusBox(IContainer container, Traveler traveler)
        {
            var statusText = traveler.IsBlocked ? "محظور" : "مسموح";
            var statusColor = traveler.IsBlocked ? Colors.Red.Darken2 : Colors.Green.Darken2;

            container
                .Border(1)
                .BorderColor(statusColor)
                .Background(traveler.IsBlocked ? Colors.Red.Lighten5 : Colors.Green.Lighten5)
                .Padding(10)
                .Row(row =>
                {
                    row.RelativeItem().AlignRight().Column(col =>
                    {
                        col.Item().Text(traveler.FullName).FontSize(18).Bold();
                        col.Item().Text($"رقم الجواز: {traveler.PassportNumber}");
                        col.Item().Text($"الجنسية: {traveler.Nationality}");
                    });

                    row.ConstantItem(100)
                        .AlignCenter()
                        .AlignMiddle()
                        .Text(statusText)
                        .FontSize(16)
                        .Bold()
                        .FontColor(statusColor);
                });
        }

        public byte[] GenerateTripsReport(List<Trip> trips)
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(25);
                    page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(10));

                    page.Header()
                        .AlignCenter()
                        .Text("تقرير الرحلات")
                        .FontSize(22)
                        .Bold();

                    page.Content().Column(col =>
                    {
                        col.Spacing(10);

                        col.Item()
                            .AlignRight()
                            .Text($"عدد الرحلات: {trips.Count}")
                            .Bold();

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(45);
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });

                            HeaderCell(table, "الرقم");
                            HeaderCell(table, "المسافر");
                            HeaderCell(table, "رقم الجواز");
                            HeaderCell(table, "نوع الرحلة");
                            HeaderCell(table, "تاريخ الرحلة");

                            foreach (var trip in trips)
                            {
                                BodyCell(table, trip.Id.ToString());
                                BodyCell(table, trip.Traveler?.FullName ?? "-");
                                BodyCell(table, trip.Traveler?.PassportNumber ?? "-");
                                BodyCell(table, trip.TripType);
                                BodyCell(table, trip.TripDate.ToString("yyyy-MM-dd"));
                            }
                        });
                    });

                    page.Footer()
                        .AlignCenter()
                        .Text($"تم إنشاء التقرير بتاريخ {DateTime.Now:yyyy-MM-dd HH:mm}");
                });
            }).GeneratePdf();
        }
        private void ComposeTravelerInfo(IContainer container, Traveler traveler)
        {
            container.Column(col =>
            {
                col.Item().Element(c => SectionTitle(c, "البيانات الأساسية"));

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });

                    InfoRow(table, "الاسم الكامل", traveler.FullName, "رقم الجواز", traveler.PassportNumber);
                    InfoRow(table, "الجنسية", traveler.Nationality, "الجنس", traveler.Gender);
                    InfoRow(table, "تاريخ الميلاد", traveler.DateOfBirth.ToString("yyyy-MM-dd"), "رقم الهاتف", traveler.PhoneNumber);
                    InfoRow(table, "الإيميل", traveler.Email, "رقم الإقامة", traveler.ResidenceNumber);
                    InfoRow(table, "انتهاء الجواز", traveler.PassportExpiryDate?.ToString("yyyy-MM-dd"), "عدد العمرات", traveler.Trips?.Count.ToString() ?? "0");
                    InfoRow(table, "ملاحظات", traveler.Notes, "سبب الحظر", traveler.BlockReason);
                });
            });
        }

        private void ComposeTrips(IContainer container, Traveler traveler)
        {
            container.Column(col =>
            {
                col.Item().Element(c => SectionTitle(c, "سجل الرحلات"));

                if (traveler.Trips == null || !traveler.Trips.Any())
                {
                    col.Item().Text("لا يوجد رحلات مسجلة.").AlignRight();
                    return;
                }

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(50);
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });

                    HeaderCell(table, "الرقم");
                    HeaderCell(table, "نوع الرحلة");
                    HeaderCell(table, "تاريخ الرحلة");
                    HeaderCell(table, "ملاحظات");

                    foreach (var trip in traveler.Trips.OrderByDescending(t => t.TripDate))
                    {
                        BodyCell(table, trip.Id.ToString());
                        BodyCell(table, trip.TripType);
                        BodyCell(table, trip.TripDate.ToString("yyyy-MM-dd"));
                        BodyCell(table, trip.Notes ?? "-");
                    }
                });
            });
        }

        private void ComposeDocuments(IContainer container, Traveler traveler)
        {
            container.Column(col =>
            {
                col.Item().Element(c => SectionTitle(c, "الوثائق"));

                if (traveler.Documents == null || !traveler.Documents.Any())
                {
                    col.Item().Text("لا يوجد وثائق مرفوعة.").AlignRight();
                    return;
                }

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });

                    HeaderCell(table, "نوع المستند");
                    HeaderCell(table, "اسم الملف");
                    HeaderCell(table, "تاريخ الرفع");

                    foreach (var doc in traveler.Documents.OrderByDescending(d => d.UploadedAt))
                    {
                        BodyCell(table, GetDocumentTypeArabic(doc.DocumentType));
                        BodyCell(table, doc.FileName);
                        BodyCell(table, doc.UploadedAt.ToString("yyyy-MM-dd HH:mm"));
                    }
                });

                var images = traveler.Documents
                    .Where(d => IsImage(d.FilePath))
                    .OrderByDescending(d => d.UploadedAt)
                    .ToList();

                if (images.Any())
                {
                    col.Item().PaddingTop(10).Element(c => SectionTitle(c, "صور الوثائق"));

                    foreach (var doc in images)
                    {
                        var imagePath = GetFullPath(doc.FilePath);

                        if (File.Exists(imagePath))
                        {
                            col.Item().PaddingTop(6).Text($"{GetDocumentTypeArabic(doc.DocumentType)} - {doc.FileName}").Bold().AlignRight();

                            col.Item()
                                .Border(1)
                                .BorderColor(Colors.Grey.Lighten2)
                                .Padding(5)
                                .AlignCenter()
                                .MaxHeight(220)
                                .Image(imagePath)
                                .FitArea();
                        }
                    }
                }
            });
        }

        private void ComposeImageSection(IContainer container, string title, string imagePath)
        {
            container.Column(col =>
            {
                col.Item().Element(c => SectionTitle(c, title));

                col.Item()
                    .Border(1)
                    .BorderColor(Colors.Grey.Lighten2)
                    .Padding(5)
                    .AlignCenter()
                    .MaxHeight(240)
                    .Image(imagePath)
                    .FitArea();
            });
        }



        private void ComposeFooter(IContainer container)
        {
            container
                .BorderTop(1)
                .BorderColor(Colors.Grey.Lighten1)
                .PaddingTop(8)
                .AlignCenter()
                .Text(x =>
                {
                    x.Span("تم إنشاء التقرير بواسطة نظام رواد العمرة - ");
                    x.CurrentPageNumber();
                    x.Span(" / ");
                    x.TotalPages();
                });
        }

        private static void SectionTitle(IContainer container, string title)
        {
            container
                .Background(Colors.Blue.Lighten4)
                .Padding(7)
                .AlignRight()
                .Text(title)
                .FontSize(13)
                .Bold();
        }

        private static void InfoRow(TableDescriptor table, string title1, string? value1, string title2, string? value2)
        {
            HeaderCell(table, title1);
            BodyCell(table, value1 ?? "-");
            HeaderCell(table, title2);
            BodyCell(table, value2 ?? "-");
        }

        private static void HeaderCell(TableDescriptor table, string text)
        {
            table.Cell()
                .Border(1)
                .BorderColor(Colors.Grey.Lighten2)
                .Background(Colors.Grey.Lighten4)
                .Padding(5)
                .AlignRight()
                .Text(text)
                .Bold();
        }

        private static void BodyCell(TableDescriptor table, string text)
        {
            table.Cell()
                .Border(1)
                .BorderColor(Colors.Grey.Lighten2)
                .Padding(5)
                .AlignRight()
                .Text(text);
        }

        private static string GetFullPath(string relativePath)
        {
            return Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                relativePath.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
        }

        private static bool IsImage(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var ext = Path.GetExtension(path).ToLowerInvariant();

            return ext == ".jpg" || ext == ".jpeg" || ext == ".png";
        }

        private static string GetDocumentTypeArabic(string type)
        {
            return type switch
            {
                "PersonalPhoto" => "صورة شخصية",
                "Visa" => "تأشيرة",
                "PassportCopy" => "نسخة جواز",
                "PDF" => "ملف PDF",
                "Other" => "أخرى",
                _ => type
            };
        }

        public byte[] GenerateTravelersReport(List<Traveler> travelers)
        {
            return GenerateTableReport(
                "تقرير المسافرين",
                $"عدد المسافرين: {travelers.Count}",
                new[] { "الاسم", "رقم الجواز", "الجنسية", "الهاتف", "الحالة" },
                travelers.Select(t => new[]
                {
            t.FullName,
            t.PassportNumber,
            t.Nationality,
            t.PhoneNumber,
            t.IsBlocked ? "محظور" : "مسموح"
                }).ToList()
            );
        }

        public byte[] GenerateDeletedTravelersReport(List<Traveler> travelers)
        {
            return GenerateTableReport(
                "تقرير أرشيف المسافرين",
                $"عدد المسافرين المؤرشفين: {travelers.Count}",
                new[] { "الاسم", "رقم الجواز", "الجنسية", "تاريخ الأرشفة", "تمت بواسطة" },
                travelers.Select(t => new[]
                {
            t.FullName,
            t.PassportNumber,
            t.Nationality,
            t.DeletedAt?.ToString("yyyy-MM-dd HH:mm") ?? "-",
            t.DeletedBy ?? "-"
                }).ToList()
            );
        }

        public byte[] GenerateAuditLogsReport(List<AuditLog> logs)
        {
            return GenerateTableReport(
                "تقرير سجل العمليات",
                $"عدد العمليات: {logs.Count}",
                new[] { "التاريخ", "الموظف", "العملية", "المسافر", "رقم الجواز", "التفاصيل" },
                logs.Select(a => new[]
                {
            a.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
            a.EmployeeName,
            a.Action,
            a.TravelerName ?? "-",
            a.PassportNumber ?? "-",
            a.Details ?? "-"
                }).ToList()
            );
        }

        public byte[] GenerateDocumentsReport(List<TravelerDocument> documents)
        {
            return GenerateTableReport(
                "تقرير الوثائق النشطة",
                $"عدد الوثائق: {documents.Count}",
                new[] { "المسافر", "رقم الجواز", "نوع المستند", "اسم الملف", "تاريخ الرفع" },
                documents.Select(d => new[]
                {
            d.Traveler?.FullName ?? "-",
            d.Traveler?.PassportNumber ?? "-",
            GetDocumentTypeArabic(d.DocumentType),
            d.FileName,
            d.UploadedAt.ToString("yyyy-MM-dd HH:mm")
                }).ToList()
            );
        }

        public byte[] GenerateDeletedDocumentsReport(List<TravelerDocument> documents)
        {
            return GenerateTableReport(
                "تقرير أرشيف الوثائق",
                $"عدد الوثائق المؤرشفة: {documents.Count}",
                new[] { "المسافر", "رقم الجواز", "نوع المستند", "اسم الملف", "تاريخ الأرشفة" },
                documents.Select(d => new[]
                {
            d.Traveler?.FullName ?? "-",
            d.Traveler?.PassportNumber ?? "-",
            GetDocumentTypeArabic(d.DocumentType),
            d.FileName,
            d.DeletedAt?.ToString("yyyy-MM-dd HH:mm") ?? "-"
                }).ToList()
            );
        }

        public byte[] GenerateExpiringPassportsReport(List<Traveler> travelers)
        {
            return GenerateTableReport(
                "تقرير الجوازات التي تنتهي قريباً",
                $"عدد الجوازات: {travelers.Count}",
                new[] { "الاسم", "رقم الجواز", "الجنسية", "تاريخ انتهاء الجواز" },
                travelers.Select(t => new[]
                {
            t.FullName,
            t.PassportNumber,
            t.Nationality,
            t.PassportExpiryDate?.ToString("yyyy-MM-dd") ?? "-"
                }).ToList()
            );
        }

        public byte[] GenerateExpiredPassportsReport(List<Traveler> travelers)
        {
            return GenerateTableReport(
                "تقرير الجوازات المنتهية",
                $"عدد الجوازات المنتهية: {travelers.Count}",
                new[] { "الاسم", "رقم الجواز", "الجنسية", "تاريخ انتهاء الجواز" },
                travelers.Select(t => new[]
                {
            t.FullName,
            t.PassportNumber,
            t.Nationality,
            t.PassportExpiryDate?.ToString("yyyy-MM-dd") ?? "-"
                }).ToList()
            );
        }

        private byte[] GenerateTableReport(
            string title,
            string summary,
            string[] headers,
            List<string[]> rows)
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(25);
                    page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(9));

                    page.Header()
                        .BorderBottom(1)
                        .BorderColor(Colors.Grey.Lighten1)
                        .PaddingBottom(8)
                        .AlignCenter()
                        .Text(title)
                        .FontSize(20)
                        .Bold();

                    page.Content().Column(col =>
                    {
                        col.Spacing(10);

                        col.Item()
                            .AlignRight()
                            .Text(summary)
                            .Bold();

                        col.Item()
                            .AlignRight()
                            .Text($"تاريخ التقرير: {DateTime.Now:yyyy-MM-dd HH:mm}")
                            .FontSize(9)
                            .FontColor(Colors.Grey.Darken1);

                        if (rows.Count == 0)
                        {
                            col.Item()
                                .PaddingTop(20)
                                .AlignCenter()
                                .Text("لا توجد بيانات لعرضها.");
                            return;
                        }

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                foreach (var _ in headers)
                                    columns.RelativeColumn();
                            });

                            foreach (var header in headers)
                                HeaderCell(table, header);

                            foreach (var row in rows)
                            {
                                foreach (var cell in row)
                                    BodyCell(table, cell);
                            }
                        });
                    });

                    page.Footer().Element(ComposeFooter);
                });
            }).GeneratePdf();
        }
    }
}