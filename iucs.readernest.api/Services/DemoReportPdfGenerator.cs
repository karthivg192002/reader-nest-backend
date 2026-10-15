using System.Globalization;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Dto.Admission;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// Month-end demo report PDF (Admission → Reports): one section per admission counsellor,
    /// each with its own totals, then a grand total. Layout only; the data comes from
    /// DemoBookingService.GetMonthlyReportAsync.
    /// </summary>
    public class DemoReportPdfGenerator : IDemoReportPdfGenerator
    {
        private const string Ink = "#1F2937";
        private const string Muted = "#6B7280";
        private const string Accent = "#4F46E5";
        private const string HeaderFill = "#EEF2FF";
        private const string LineColor = "#E5E7EB";

        static DemoReportPdfGenerator()
        {
            // Same Community license InvoicePdfGenerator sets — see its note.
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public byte[] Generate(DemoMonthlyReportDto report)
        {
            var allRows = report.Counselors.SelectMany(c => c.Rows).ToList();

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(28);
                    page.DefaultTextStyle(x => x.FontSize(9).FontColor(Ink));

                    page.Header().PaddingBottom(10).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text($"Demo Report — {report.MonthLabel}").FontSize(18).Bold().FontColor(Accent);
                            c.Item().Text("The Reader Nest · demos by admission counsellor").FontColor(Muted);
                        });
                        row.ConstantItem(220).AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Text($"{allRows.Count} demo(s) · {allRows.Count(r => r.Enrolled)} enrolled");
                            c.Item().AlignRight().Text($"Amount paid: {Money(allRows.Sum(r => r.AmountPaid ?? 0))}").Bold();
                            c.Item().AlignRight().Text($"Generated {IstNow(report.GeneratedAtUtc)}").FontSize(8).FontColor(Muted);
                        });
                    });

                    page.Content().Column(col =>
                    {
                        if (report.Counselors.Count == 0)
                        {
                            col.Item().PaddingTop(30).AlignCenter().Text("No demos were scheduled in this month.").FontColor(Muted);
                            return;
                        }

                        foreach (var counselor in report.Counselors)
                        {
                            col.Item().PaddingTop(12).PaddingBottom(4).Row(row =>
                            {
                                row.RelativeItem().Text(counselor.CounselorName).FontSize(12).Bold();
                                row.AutoItem().AlignBottom().Text(
                                    $"{counselor.DemoCount} demo(s) · {counselor.CompletedCount} completed · {counselor.EnrolledCount} enrolled · paid {Money(counselor.TotalAmountPaid)}")
                                    .FontColor(Muted);
                            });

                            col.Item().Table(table =>
                            {
                                table.ColumnsDefinition(c =>
                                {
                                    c.ConstantColumn(24);
                                    c.ConstantColumn(118);
                                    c.RelativeColumn(3);
                                    c.RelativeColumn(3);
                                    c.RelativeColumn(2);
                                    c.RelativeColumn(2);
                                    c.ConstantColumn(80);
                                });

                                table.Header(h =>
                                {
                                    foreach (var title in new[] { "#", "Demo date & time", "Student (parent)", "Teacher", "Demo status", "Outcome", "Amount paid" })
                                    {
                                        h.Cell().Background(HeaderFill).BorderBottom(1).BorderColor(LineColor).Padding(5).Text(title).Bold();
                                    }
                                });

                                var i = 0;
                                foreach (var r in counselor.Rows)
                                {
                                    i++;
                                    Cell(table).Text(i.ToString(CultureInfo.InvariantCulture)).FontColor(Muted);
                                    Cell(table).Text(r.DateTimeLabel);
                                    Cell(table).Text(t =>
                                    {
                                        t.Span(r.StudentName).SemiBold();
                                        t.Span($"  ({r.ParentName})").FontColor(Muted);
                                    });
                                    Cell(table).Text(r.TeacherName);
                                    Cell(table).Text(r.DemoStatus);
                                    Cell(table).Text(r.Outcome);
                                    Cell(table).AlignRight().Text(r.AmountPaid is { } paid ? Money(paid) : "—");
                                }
                            });
                        }
                    });

                    page.Footer().AlignCenter().Text(t =>
                    {
                        t.DefaultTextStyle(x => x.FontSize(8).FontColor(Muted));
                        t.Span("Page ");
                        t.CurrentPageNumber();
                        t.Span(" of ");
                        t.TotalPages();
                    });
                });
            }).GeneratePdf();
        }

        private static IContainer Cell(TableDescriptor table) =>
            table.Cell().BorderBottom(1).BorderColor(LineColor).PaddingVertical(4).PaddingHorizontal(5);

        private static string Money(decimal amount) => $"INR {amount.ToString("#,0.##", CultureInfo.InvariantCulture)}";

        private static string IstNow(DateTime utc)
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(iucs.readernest.application.Helper.DateTimeDisplay.DefaultTimeZoneId);
            return TimeZoneInfo.ConvertTimeFromUtc(utc, zone).ToString("dd MMM yyyy, h:mm tt", CultureInfo.InvariantCulture);
        }
    }
}
