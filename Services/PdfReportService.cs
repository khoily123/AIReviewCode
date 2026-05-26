using AIReviewerAPI.DTOs;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Services
{
    public class PdfReportService : IPdfReportService
    {
        public byte[] GenerateReviewPdf(ReviewResponseDto data, string originalCode)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.DefaultTextStyle(t => t.FontSize(10).FontFamily(Fonts.Arial));

                    // ===== HEADER =====
                    page.Header().PaddingBottom(10).BorderBottom(2).BorderColor("#4f46e5").Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("AI Code Review Report")
                                .FontSize(22).Bold().FontColor("#4f46e5");
                            col.Item().Text($"Ngày tạo: {data.ReviewedAt:dd/MM/yyyy HH:mm} UTC")
                                .FontSize(9).FontColor("#64748b");
                        });
                        row.ConstantItem(120).AlignRight().AlignMiddle()
                            .Text("🤖 AI Review Tool").FontSize(11).Bold().FontColor("#7c3aed");
                    });

                    // ===== CONTENT =====
                    page.Content().PaddingTop(16).Column(col =>
                    {
                        // --- SCORES ---
                        col.Item().PaddingBottom(12).Column(section =>
                        {
                            section.Item().PaddingBottom(6)
                                .Text("Điểm đánh giá").FontSize(13).Bold().FontColor("#1e293b");

                            ScoreRow(section, "⚡  Hiệu suất (Performance)", data.PerformanceScore, "#f59e0b");
                            ScoreRow(section, "🔒  Bảo mật (Security)", data.SecurityScore, "#ef4444");
                            ScoreRow(section, "🔧  Bảo trì (Maintainability)", data.MaintainabilityScore, "#10b981");
                        });

                        col.Item().BorderTop(1).BorderColor("#e2e8f0").PaddingTop(12).PaddingBottom(12).Column(section =>
                        {
                            section.Item().PaddingBottom(6)
                                .Text("Tổng quan (Summary)").FontSize(13).Bold().FontColor("#1e293b");
                            section.Item()
                                .Background("#f0fdf4").Border(1).BorderColor("#bbf7d0")
                                .Padding(10).Text(data.Summary ?? "Không có tổng quan.").FontColor("#166534");
                        });

                        // --- BUGS ---
                        var bugs = data.DetectedBugs;
                        col.Item().BorderTop(1).BorderColor("#e2e8f0").PaddingTop(12).PaddingBottom(12).Column(section =>
                        {
                            section.Item().PaddingBottom(6).Row(r =>
                            {
                                r.AutoItem().Text("Lỗi phát hiện").FontSize(13).Bold().FontColor("#1e293b");
                                r.AutoItem().PaddingLeft(8)
                                    .Background(bugs != null && bugs.Count > 0 ? "#fef2f2" : "#f0fdf4")
                                    .Padding(2).Padding(4)
                                    .Text(bugs != null && bugs.Count > 0 ? $"{bugs.Count} issues" : "Không có lỗi ✓")
                                    .FontColor(bugs != null && bugs.Count > 0 ? "#dc2626" : "#16a34a")
                                    .Bold().FontSize(9);
                            });

                            if (bugs != null && bugs.Count > 0)
                            {
                                section.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(c =>
                                    {
                                        c.ConstantColumn(60);
                                        c.RelativeColumn();
                                    });

                                    table.Header(h =>
                                    {
                                        h.Cell().Background("#fef3c7").Padding(6)
                                            .Text("Dòng").Bold().FontSize(9);
                                        h.Cell().Background("#fef3c7").Padding(6)
                                            .Text("Mô tả lỗi").Bold().FontSize(9);
                                    });

                                    foreach (var bug in bugs)
                                    {
                                        table.Cell().BorderBottom(1).BorderColor("#fed7aa")
                                            .Padding(6).Text($"Line {bug.Line}").FontColor("#ea580c").Bold().FontSize(9);
                                        table.Cell().BorderBottom(1).BorderColor("#fed7aa")
                                            .Padding(6).Text(bug.Description ?? "").FontSize(9);
                                    }
                                });
                            }
                            else
                            {
                                section.Item().Background("#f0fdf4").Border(1).BorderColor("#bbf7d0")
                                    .Padding(10).Text("🎉 Tuyệt vời! Code của bạn không có lỗi.")
                                    .FontColor("#166534").Bold();
                            }
                        });

                        // --- FIXED CODE ---
                        if (!string.IsNullOrEmpty(data.FixedCode))
                        {
                            col.Item().BorderTop(1).BorderColor("#e2e8f0").PaddingTop(12).PaddingBottom(12).Column(section =>
                            {
                                section.Item().PaddingBottom(6)
                                    .Text("Code đã sửa (Auto Fix)").FontSize(13).Bold().FontColor("#1e293b");
                                section.Item().Background("#1e1e2e").Padding(12)
                                    .Text(data.FixedCode).FontColor("#cdd6f4")
                                    .FontFamily(Fonts.CourierNew).FontSize(8);
                            });
                        }

                        // --- UNIT TESTS ---
                        if (!string.IsNullOrEmpty(data.UnitTests))
                        {
                            col.Item().BorderTop(1).BorderColor("#e2e8f0").PaddingTop(12).Column(section =>
                            {
                                section.Item().PaddingBottom(6)
                                    .Text("Unit Tests (xUnit)").FontSize(13).Bold().FontColor("#1e293b");
                                section.Item().Background("#0f172a").Padding(12)
                                    .Text(data.UnitTests).FontColor("#7dd3fc")
                                    .FontFamily(Fonts.CourierNew).FontSize(8);
                            });
                        }
                    });

                    // ===== FOOTER =====
                    page.Footer().AlignCenter().PaddingTop(8).BorderTop(1).BorderColor("#e2e8f0")
                        .Text(t =>
                        {
                            t.Span("AI Code Review Tool  •  ").FontColor("#94a3b8").FontSize(9);
                            t.Span("Trang ").FontColor("#94a3b8").FontSize(9);
                            t.CurrentPageNumber().FontColor("#64748b").FontSize(9);
                            t.Span(" / ").FontColor("#94a3b8").FontSize(9);
                            t.TotalPages().FontColor("#64748b").FontSize(9);
                        });
                });
            }).GeneratePdf();
        }

        private static void ScoreRow(ColumnDescriptor col, string label, int score, string color)
        {
            col.Item().PaddingBottom(6).Row(row =>
            {
                row.ConstantItem(200).AlignMiddle().Text(label).FontSize(10);
                row.RelativeItem().AlignMiddle().Height(12).Row(bar =>
                {
                    if (score > 0)
                        bar.RelativeItem(score).Background(color).Height(12);
                    if (score < 100)
                        bar.RelativeItem(100 - score).Background("#e2e8f0").Height(12);
                });
                row.ConstantItem(50).AlignMiddle().AlignRight()
                    .Text($"{score}/100").Bold().FontColor(color);
            });
        }
    }
}
