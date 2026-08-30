using System.Drawing;
using DevExpress.Drawing;
using DevExpress.ExpressApp.ReportsV2;
using DevExpress.Persistent.Base.ReportsV2;
using DevExpress.XtraReports.UI;
using XafXPODynAssem.Module.BusinessObjects;

namespace XafXPODynAssem.Module.Services
{
    /// <summary>Jedyne miejsce konwersji mm &lt;-&gt; jednostka raportu (XtraReport.ReportUnit).</summary>
    public static class ReportUnits
    {
        const float MmPerInch = 25.4f;

        public static float MmToReport(XtraReport report, float mm) => mm * UnitsPerMm(report.ReportUnit);

        static float UnitsPerMm(ReportUnit unit) => unit switch
        {
            ReportUnit.Inches => 1f / MmPerInch,
            ReportUnit.HundredthsOfAnInch => 100f / MmPerInch,   // domyślna jednostka XtraReport
            ReportUnit.Millimeters => 1f,
            ReportUnit.TenthsOfAMillimeter => 10f,
            ReportUnit.Pixels => 96f / MmPerInch,                // 1 px = 1/96 cala
            _ => 100f / MmPerInch,
        };
    }

    /// <summary>Kolumna raportu — ścieżka pola po naprawie plus podpis nagłówka.</summary>
    public sealed record ReportColumnSpec(string Path, string Caption);

    /// <summary>
    /// Deterministyczna połowa pipeline'u raportowego: zamienia <see cref="ReportSpec"/> na
    /// <see cref="XtraReport"/> nad tabelą encji runtime'owej.
    ///
    /// Szerokość strony NIE jest stałą (jak <c>const float PageWidth = 650f</c> w
    /// XafAIReportDesigner, przez co tamten projekt nie obsługuje orientacji ani formatu) —
    /// najpierw ustawiamy PaperKind + Landscape + marginesy, a dopiero potem czytamy
    /// <c>report.PageWidthF</c>, które już uwzględnia format i orientację.
    ///
    /// Czcionki wyłącznie przez <see cref="DXFont"/> — <c>System.Drawing.Font</c> wywala Gdip na macOS.
    /// </summary>
    public static class ReportSpecBuilder
    {
        const string FontFamily = "Segoe UI";

        public static XtraReport Build(
            ReportSpec spec,
            string runtimeTypeFullName,
            IReadOnlyList<ReportColumnSpec> columns,
            string groupByPath,
            string sortByPath)
        {
            var report = new XtraReport();

            // 0) Zrodlo danych. To ONO wnosi typ encji do zapisanego raportu — ReportDataV2.DataTypeName
            // jest wlasciwoscia TYLKO DO ODCZYTU (reflekcja po DevExpress.Persistent.BaseImpl.Xpo.v26.1:
            // jest get_DataTypeName, nie ma set_DataTypeName), wiec wywolujacy nie ma jak jej ustawic.
            // CollectionDataSource zyje w DevExpress.Persistent.Base.ReportsV2 (NIE w
            // DevExpress.ExpressApp.ReportsV2 — tam jej faktycznie nie ma, stad wczesniejsza pomylka).
            var dataSource = new CollectionDataSource { ObjectTypeName = runtimeTypeFullName };
            if (!string.IsNullOrWhiteSpace(spec.FilterCriteria))
                dataSource.CriteriaString = spec.FilterCriteria;
            report.DataSource = dataSource;

            // 1) Format + orientacja + marginesy PRZED policzeniem szerokości użytecznej.
            report.PaperKind = spec.PaperKind;
            report.Landscape = spec.Orientation == ReportOrientation.Landscape;
            report.Margins.Left = (int)ReportUnits.MmToReport(report, spec.MarginLeft);
            report.Margins.Right = (int)ReportUnits.MmToReport(report, spec.MarginRight);
            report.Margins.Top = (int)ReportUnits.MmToReport(report, spec.MarginTop);
            report.Margins.Bottom = (int)ReportUnits.MmToReport(report, spec.MarginBottom);

            // 2) Dopiero teraz szerokość użyteczna — z PaperKind + orientacji − marginesy.
            var usable = report.PageWidthF - report.Margins.Left - report.Margins.Right;
            if (usable < 1) usable = 1;

            // Pasmo naglowka: tytul + opcjonalny blok dokumentowy (numer, data, kontrahent...).
            // Wyrazenia [Pole] i [Referencja.Pole] rozwiazuja sie wzgledem PIERWSZEGO wiersza
            // zrodla — dlatego dokument renderuje sie na danych zawezonych do jednego rekordu
            // nadrzednego, a nie na calej tabeli.
            var headerLines = ParseHeaderLines(spec.HeaderLines);
            var headerHeight = 40f + headerLines.Count * LineHeight;
            var reportHeader = new ReportHeaderBand { HeightF = headerHeight };
            reportHeader.Controls.Add(new XRLabel
            {
                Text = spec.Title ?? "Raport",
                WidthF = usable,
                HeightF = 30,
                Font = new DXFont(FontFamily, 16, DXFontStyle.Bold),
            });

            var lineTop = 34f;
            foreach (var line in headerLines)
            {
                var label = new XRLabel
                {
                    TopF = lineTop,
                    WidthF = usable,
                    HeightF = LineHeight,
                    Font = new DXFont(FontFamily, 10),
                };
                // Linia bez [Pola] to zwykly tekst; z [Polem] — wyrazenie sklejane z fragmentow.
                var expression = ToExpression(line);
                if (expression == null) label.Text = line;
                else label.ExpressionBindings.Add(new ExpressionBinding("Text", expression));
                reportHeader.Controls.Add(label);
                lineTop += LineHeight;
            }
            report.Bands.Add(reportHeader);

            var pageHeader = new PageHeaderBand { HeightF = 26 };
            pageHeader.Controls.Add(BuildRow(columns, usable, isHeader: true));
            report.Bands.Add(pageHeader);

            if (!string.IsNullOrWhiteSpace(groupByPath))
            {
                var groupHeader = new GroupHeaderBand { HeightF = 26, Name = "grp" };
                groupHeader.GroupFields.Add(new GroupField(groupByPath, XRColumnSortOrder.Ascending));
                var groupLabel = new XRLabel
                {
                    WidthF = usable,
                    HeightF = 22,
                    Font = new DXFont(FontFamily, 11, DXFontStyle.Bold),
                };
                groupLabel.ExpressionBindings.Add(new ExpressionBinding("Text", $"[{groupByPath}]"));
                groupHeader.Controls.Add(groupLabel);
                report.Bands.Add(groupHeader);
            }

            var detail = new DetailBand { HeightF = 22 };
            detail.Controls.Add(BuildRow(columns, usable, isHeader: false));
            if (!string.IsNullOrWhiteSpace(sortByPath))
            {
                detail.SortFields.Add(new GroupField(sortByPath,
                    spec.SortDescending ? XRColumnSortOrder.Descending : XRColumnSortOrder.Ascending));
            }
            report.Bands.Add(detail);

            // Pasmo podsumowania — sumy pol liczbowych pod tabela (wartosc netto, brutto...).
            var summary = ParseList(spec.SummaryFields);
            if (summary.Count > 0)
            {
                var summaryBand = new ReportFooterBand { HeightF = 12 + summary.Count * LineHeight };
                var top = 8f;
                foreach (var field in summary)
                {
                    var label = new XRLabel
                    {
                        TopF = top,
                        WidthF = usable,
                        HeightF = LineHeight,
                        TextAlignment = DevExpress.XtraPrinting.TextAlignment.MiddleRight,
                        Font = new DXFont(FontFamily, 10, DXFontStyle.Bold),
                    };
                    label.ExpressionBindings.Add(new ExpressionBinding(
                        "BeforePrint", "Text", $"'Razem {field}: ' + sumSum([{field}])"));
                    summaryBand.Controls.Add(label);
                    top += LineHeight;
                }
                report.Bands.Add(summaryBand);
            }

            var pageFooter = new PageFooterBand { HeightF = 20 };
            var pageInfo = new XRPageInfo
            {
                WidthF = usable,
                HeightF = 18,
                TextFormatString = "Strona {0} z {1}",
                TextAlignment = DevExpress.XtraPrinting.TextAlignment.MiddleRight,
                Font = new DXFont(FontFamily, 8),
            };
            pageFooter.Controls.Add(pageInfo);
            report.Bands.Add(pageFooter);

            return report;
        }

        const float LineHeight = 16f;

        /// <summary>Linie naglowka: rozdzielone znakiem nowej linii albo pionowa kreska.</summary>
        public static IReadOnlyList<string> ParseHeaderLines(string raw)
            => (raw ?? string.Empty)
                .Split(new[] { '\n', '\r', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();

        /// <summary>Lista pol rozdzielona przecinkami.</summary>
        public static IReadOnlyList<string> ParseList(string raw)
            => (raw ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

        /// <summary>
        /// Zamienia „Faktura nr [NumerFaktury]" na wyrazenie XtraReports
        /// <c>'Faktura nr ' + [NumerFaktury]</c>. Zwraca null, gdy linia nie ma zadnego pola —
        /// wtedy wywolujacy ustawia zwykly Text (taniej i nie ryzykuje bledu wyrazenia).
        /// </summary>
        public static string ToExpression(string line)
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(line, @"\[([^\]]+)\]");
            if (matches.Count == 0) return null;

            var parts = new List<string>();
            var pos = 0;
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                if (m.Index > pos)
                    parts.Add(Quote(line.Substring(pos, m.Index - pos)));
                parts.Add($"ToStr([{m.Groups[1].Value}])");
                pos = m.Index + m.Length;
            }
            if (pos < line.Length) parts.Add(Quote(line.Substring(pos)));
            return string.Join(" + ", parts);
        }

        static string Quote(string text) => "'" + text.Replace("'", "''") + "'";

        static XRTable BuildRow(IReadOnlyList<ReportColumnSpec> columns, float usableWidth, bool isHeader)
        {
            var table = new XRTable { WidthF = usableWidth, HeightF = isHeader ? 24 : 20 };
            var row = new XRTableRow();
            table.Rows.Add(row);

            var cellWidth = columns.Count > 0 ? usableWidth / columns.Count : usableWidth;
            foreach (var column in columns)
            {
                var cell = new XRTableCell
                {
                    WidthF = cellWidth,
                    Font = new DXFont(FontFamily, 9.75f, isHeader ? DXFontStyle.Bold : DXFontStyle.Regular),
                    Padding = new DevExpress.XtraPrinting.PaddingInfo(4, 4, 2, 2),
                };
                if (isHeader)
                {
                    cell.Text = column.Caption;
                    cell.BackColor = Color.WhiteSmoke;
                }
                else
                {
                    cell.ExpressionBindings.Add(new ExpressionBinding("Text", $"[{column.Path}]"));
                }
                row.Cells.Add(cell);
            }
            return table;
        }
    }
}
