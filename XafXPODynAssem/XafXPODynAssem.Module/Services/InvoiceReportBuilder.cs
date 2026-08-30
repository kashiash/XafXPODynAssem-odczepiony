using System.Drawing;
using DevExpress.Drawing;
using DevExpress.Persistent.Base.ReportsV2;
using DevExpress.XtraReports.Parameters;
using DevExpress.XtraReports.UI;
using DevExpress.XtraReports.Wizards;

namespace XafXPODynAssem.Module.Services
{
    /// <summary>Jedno powiazanie slotu faktury: albo sciezka do pola, albo staly tekst.</summary>
    public sealed record InvoiceSlot(TemplateFieldKind Kind, string Value, bool IsLiteral);

    /// <summary>Kolumny tabelki stawek VAT. Wszystkie sciezki wzgledem encji POZYCJI.</summary>
    public sealed record VatSummarySpec(string RatePath, string NetPath, string VatPath, string GrossPath);

    /// <summary>
    /// Sklada fakture jako raport NAD ENCJA NAGLOWKA (jeden rekord = jedna faktura):
    ///
    ///   GroupHeaderBand  — naglowek dokumentu, PageBreak.BeforeBandExceptFirstEntry
    ///   DetailBand       — XRSubreport z pozycjami biezacej faktury
    ///   GroupFooterBand  — XRSubreport z zestawieniem stawek VAT
    ///
    /// Dlaczego subreport, a nie DetailReportBand z DataMember: typy runtime'owe generowane przez
    /// <see cref="RuntimeAssemblyBuilder"/> NIE maja kolekcji dzieci (generator nie emituje
    /// [Association] ani XPCollection), wiec z poziomu Faktury nie istnieje sciezka do pozycji.
    /// Powiazanie idzie wiec parametrem: master oddaje swoj Oid, podraport filtruje sie
    /// po <c>[Referencja.Oid] = ?pDocKey</c>.
    /// </summary>
    public static class InvoiceReportBuilder
    {
        const string FontFamily = "Segoe UI";
        const string DocKeyParameter = "pDocKey";
        const float LineHeight = 14f;

        /// <summary>Sloty opisujace POJEDYNCZY WIERSZ faktury — reszta to dane naglowka.</summary>
        public static readonly HashSet<TemplateFieldKind> LineSlots = new()
        {
            TemplateFieldKind.ProductName, TemplateFieldKind.ProductDescription,
            TemplateFieldKind.Quantity, TemplateFieldKind.UnitPrice,
            TemplateFieldKind.UnitDiscount, TemplateFieldKind.UnitTax,
            TemplateFieldKind.Discount, TemplateFieldKind.Tax,
            TemplateFieldKind.DiscountLineTotal, TemplateFieldKind.TaxLineTotal,
            TemplateFieldKind.LineTotal,
        };

        /// <summary>Sloty pokazywane jako kwoty (z symbolem waluty).</summary>
        static readonly HashSet<TemplateFieldKind> MoneySlots = new()
        {
            TemplateFieldKind.UnitPrice, TemplateFieldKind.UnitDiscount, TemplateFieldKind.UnitTax,
            TemplateFieldKind.DiscountLineTotal, TemplateFieldKind.TaxLineTotal, TemplateFieldKind.LineTotal,
            TemplateFieldKind.Subtotal, TemplateFieldKind.DiscountTotal, TemplateFieldKind.TaxTotal,
            TemplateFieldKind.Total,
        };

        /// <summary>Kolejnosc i podpisy kolumn tabeli pozycji.</summary>
        static readonly (TemplateFieldKind Kind, string Caption, float Weight)[] LineColumnOrder =
        {
            (TemplateFieldKind.ProductName, "Nazwa", 3.0f),
            (TemplateFieldKind.ProductDescription, "Opis", 3.0f),
            (TemplateFieldKind.Quantity, "Ilosc", 1.0f),
            (TemplateFieldKind.UnitPrice, "Cena jedn.", 1.3f),
            (TemplateFieldKind.UnitDiscount, "Rabat jedn.", 1.3f),
            (TemplateFieldKind.UnitTax, "VAT jedn.", 1.3f),
            (TemplateFieldKind.Discount, "Rabat", 1.0f),
            (TemplateFieldKind.Tax, "VAT", 1.0f),
            (TemplateFieldKind.DiscountLineTotal, "Rabat wart.", 1.3f),
            (TemplateFieldKind.TaxLineTotal, "Kwota VAT", 1.3f),
            (TemplateFieldKind.LineTotal, "Wartosc", 1.4f),
        };

        /// <summary>Podpisy blokow naglowka — w kolejnosci drukowania.</summary>
        static readonly TemplateFieldKind[] VendorOrder =
        {
            TemplateFieldKind.VendorName, TemplateFieldKind.VendorContactName, TemplateFieldKind.VendorAddress,
            TemplateFieldKind.VendorCity, TemplateFieldKind.VendorCountry, TemplateFieldKind.VendorPhone,
            TemplateFieldKind.VendorEmail, TemplateFieldKind.VendorWebsite,
        };

        static readonly TemplateFieldKind[] CustomerOrder =
        {
            TemplateFieldKind.CustomerName, TemplateFieldKind.CustomerContactName, TemplateFieldKind.CustomerAddress,
            TemplateFieldKind.CustomerCity, TemplateFieldKind.CustomerCountry,
        };

        /// <summary>
        /// Buduje kompletny raport faktury.
        /// </summary>
        /// <param name="headerDataSource">Zrodlo dla mastera — CollectionDataSource (zapis) albo zywa lista (render).</param>
        /// <param name="lineDataSource">Zrodlo dla obu podraportow.</param>
        /// <param name="backReferencePath">Sciezka od encji pozycji do naglowka, np. "Faktura".</param>
        /// <param name="orderByPath">Pole porzadkujace dokumenty; null = kolejnosc zrodla.</param>
        public static XtraReport Build(
            object headerDataSource,
            object lineDataSource,
            string backReferencePath,
            IReadOnlyList<InvoiceSlot> slots,
            VatSummarySpec vat,
            string title,
            string currencySymbol,
            string orderByPath)
        {
            var report = new XtraReport { DataSource = headerDataSource };
            report.PaperKind = DevExpress.Drawing.Printing.DXPaperKind.A4;
            report.Margins.Left = report.Margins.Right = (int)ReportUnits.MmToReport(report, 15);
            report.Margins.Top = report.Margins.Bottom = (int)ReportUnits.MmToReport(report, 12);
            var usable = report.PageWidthF - report.Margins.Left - report.Margins.Right;

            var header = new GroupHeaderBand { Name = "docHeader", PageBreak = PageBreak.BeforeBandExceptFirstEntry };
            // Grupujemy po kluczu rekordu — jeden rekord naglowka = jedna faktura = jedna grupa.
            // Pole porzadkujace idzie PRZED Oid, zeby faktury szly po numerze, a nie po GUID-zie.
            if (!string.IsNullOrWhiteSpace(orderByPath))
                header.GroupFields.Add(new GroupField(orderByPath, XRColumnSortOrder.Ascending));
            header.GroupFields.Add(new GroupField("Oid", XRColumnSortOrder.Ascending));
            header.HeightF = FillHeader(header, slots, usable, title, currencySymbol);
            report.Bands.Add(header);

            var detail = new DetailBand { HeightF = 24 };
            var itemsSub = new XRSubreport
            {
                Name = "subPozycje",
                WidthF = usable,
                HeightF = 24,
                ReportSource = BuildItemsReport(lineDataSource, backReferencePath, slots, usable, currencySymbol),
            };
            itemsSub.ParameterBindings.Add(new ParameterBinding(DocKeyParameter, null, "Oid"));
            detail.Controls.Add(itemsSub);
            report.Bands.Add(detail);

            var footer = new GroupFooterBand { HeightF = 24 };
            if (vat != null)
            {
                var vatSub = new XRSubreport
                {
                    Name = "subStawkiVat",
                    TopF = 10,
                    WidthF = usable,
                    HeightF = 24,
                    ReportSource = BuildVatReport(lineDataSource, backReferencePath, vat, usable, currencySymbol),
                };
                vatSub.ParameterBindings.Add(new ParameterBinding(DocKeyParameter, null, "Oid"));
                footer.Controls.Add(vatSub);
                footer.HeightF = 40;
            }
            else
            {
                // Bez tabelki stawek podsumowania naglowka sa jedynym miejscem z kwotami zbiorczymi.
                footer.HeightF = 10 + FillTotals(footer, slots, usable, currencySymbol);
            }
            report.Bands.Add(footer);

            var pageFooter = new PageFooterBand { HeightF = 20 };
            pageFooter.Controls.Add(new XRPageInfo
            {
                WidthF = usable,
                HeightF = 18,
                TextFormatString = "Strona {0} z {1}",
                TextAlignment = DevExpress.XtraPrinting.TextAlignment.MiddleRight,
                Font = new DXFont(FontFamily, 8),
            });
            report.Bands.Add(pageFooter);

            return report;
        }

        // ------------------------------------------------------------------ naglowek

        static float FillHeader(Band band, IReadOnlyList<InvoiceSlot> slots, float usable,
            string title, string currency)
        {
            var titleLabel = new XRLabel
            {
                WidthF = usable * 0.62f,
                HeightF = 26,
                Text = string.IsNullOrWhiteSpace(title) ? "Faktura" : title,
                Font = new DXFont(FontFamily, 15, DXFontStyle.Bold),
            };
            band.Controls.Add(titleLabel);

            var number = Find(slots, TemplateFieldKind.InvoiceNumber);
            if (number != null)
            {
                var numberLabel = new XRLabel
                {
                    LeftF = usable * 0.62f,
                    WidthF = usable * 0.38f,
                    HeightF = 26,
                    Font = new DXFont(FontFamily, 15, DXFontStyle.Bold),
                    TextAlignment = DevExpress.XtraPrinting.TextAlignment.MiddleRight,
                };
                Bind(numberLabel, number, "'nr ' + ToStr({0})");
                band.Controls.Add(numberLabel);
            }

            var top = 32f;
            foreach (var kind in new[] { TemplateFieldKind.InvoiceDate, TemplateFieldKind.InvoiceDueDate })
            {
                var slot = Find(slots, kind);
                if (slot == null) continue;
                var caption = kind == TemplateFieldKind.InvoiceDate ? "Data wystawienia: " : "Termin platnosci: ";
                var label = new XRLabel
                {
                    TopF = top,
                    WidthF = usable,
                    HeightF = LineHeight,
                    Font = new DXFont(FontFamily, 9),
                };
                Bind(label, slot, "'" + caption + "' + ToStr({0})");
                band.Controls.Add(label);
                top += LineHeight;
            }

            var blockTop = top + 8;
            var vendorHeight = FillParty(band, slots, VendorOrder, "Sprzedawca", 0, usable * 0.48f, blockTop);
            var customerHeight = FillParty(band, slots, CustomerOrder, "Nabywca", usable * 0.52f, usable * 0.48f, blockTop);

            return blockTop + Math.Max(vendorHeight, customerHeight) + 10;
        }

        static float FillParty(Band band, IReadOnlyList<InvoiceSlot> slots, TemplateFieldKind[] order,
            string caption, float left, float width, float top)
        {
            var present = order.Select(k => Find(slots, k)).Where(s => s != null).ToList();
            if (present.Count == 0) return 0;

            band.Controls.Add(new XRLabel
            {
                LeftF = left,
                TopF = top,
                WidthF = width,
                HeightF = LineHeight,
                Text = caption,
                Font = new DXFont(FontFamily, 8, DXFontStyle.Bold),
                ForeColor = Color.DimGray,
            });

            var y = top + LineHeight;
            foreach (var slot in present)
            {
                var label = new XRLabel
                {
                    LeftF = left,
                    TopF = y,
                    WidthF = width,
                    HeightF = LineHeight,
                    Font = new DXFont(FontFamily, 9,
                        slot.Kind is TemplateFieldKind.VendorName or TemplateFieldKind.CustomerName
                            ? DXFontStyle.Bold : DXFontStyle.Regular),
                };
                Bind(label, slot, "ToStr({0})");
                band.Controls.Add(label);
                y += LineHeight;
            }
            return y - top;
        }

        /// <summary>Kwoty zbiorcze z naglowka — uzywane tylko wtedy, gdy nie ma tabelki stawek VAT.</summary>
        static float FillTotals(Band band, IReadOnlyList<InvoiceSlot> slots, float usable, string currency)
        {
            var order = new (TemplateFieldKind Kind, string Caption)[]
            {
                (TemplateFieldKind.Subtotal, "Razem netto"),
                (TemplateFieldKind.DiscountTotal, "Rabat"),
                (TemplateFieldKind.TaxTotal, "Razem VAT"),
                (TemplateFieldKind.Total, "Do zaplaty"),
            };
            var y = 0f;
            foreach (var (kind, caption) in order)
            {
                var slot = Find(slots, kind);
                if (slot == null) continue;
                var label = new XRLabel
                {
                    LeftF = usable * 0.55f,
                    TopF = y,
                    WidthF = usable * 0.45f,
                    HeightF = 16,
                    Font = new DXFont(FontFamily, 10,
                        kind == TemplateFieldKind.Total ? DXFontStyle.Bold : DXFontStyle.Regular),
                    TextAlignment = DevExpress.XtraPrinting.TextAlignment.MiddleRight,
                };
                Bind(label, slot, "'" + caption + ": ' + ToStr({0}) + ' " + Escape(currency) + "'");
                band.Controls.Add(label);
                y += 16;
            }
            return y;
        }

        // ------------------------------------------------------------------ podraport pozycji

        static XtraReport BuildItemsReport(object lineDataSource, string backReferencePath,
            IReadOnlyList<InvoiceSlot> slots, float usable, string currency)
        {
            var report = NewSubreport(lineDataSource, backReferencePath);

            var columns = LineColumnOrder
                .Select(c => (c.Caption, c.Weight, Slot: Find(slots, c.Kind)))
                .Where(c => c.Slot != null)
                .ToList();

            if (columns.Count == 0)
            {
                report.Bands.Add(new DetailBand { HeightF = 16 });
                return report;
            }

            var totalWeight = 0.5f + columns.Sum(c => c.Weight);   // 0.5 na kolumne "Lp."
            var unit = usable / totalWeight;

            var head = new ReportHeaderBand { HeightF = 20 };
            head.Controls.Add(BuildItemsRow(columns, unit, usable, currency, header: true));
            report.Bands.Add(head);

            var detail = new DetailBand { HeightF = 16 };
            detail.Controls.Add(BuildItemsRow(columns, unit, usable, currency, header: false));
            report.Bands.Add(detail);
            return report;
        }

        static XRTable BuildItemsRow(
            List<(string Caption, float Weight, InvoiceSlot Slot)> columns,
            float unit, float usable, string currency, bool header)
        {
            var table = new XRTable { WidthF = usable, HeightF = header ? 18 : 15 };
            var row = new XRTableRow();
            table.Rows.Add(row);

            var lp = NewCell(unit * 0.5f, header, right: true);
            if (header) lp.Text = "Lp.";
            else lp.ExpressionBindings.Add(new ExpressionBinding("BeforePrint", "Text", "[DataSource.CurrentRowIndex] + 1"));
            row.Cells.Add(lp);

            foreach (var (caption, weight, slot) in columns)
            {
                var numeric = MoneySlots.Contains(slot.Kind)
                              || slot.Kind is TemplateFieldKind.Quantity or TemplateFieldKind.Discount or TemplateFieldKind.Tax;
                var cell = NewCell(unit * weight, header, right: numeric);
                if (header) cell.Text = caption;
                else
                {
                    Bind(cell, slot, "{0}");
                    if (MoneySlots.Contains(slot.Kind)) cell.TextFormatString = "{0:N2} " + currency;
                    else if (numeric) cell.TextFormatString = "{0:N2}";
                }
                row.Cells.Add(cell);
            }
            return table;
        }

        // ------------------------------------------------------------------ podraport stawek VAT

        static XtraReport BuildVatReport(object lineDataSource, string backReferencePath,
            VatSummarySpec vat, float usable, string currency)
        {
            var report = NewSubreport(lineDataSource, backReferencePath);
            var width = usable * 0.62f;
            var left = usable - width;

            var columns = new (string Caption, string Path, bool Money)[]
            {
                ("Stawka VAT", vat.RatePath, false),
                ("Netto", vat.NetPath, true),
                ("VAT", vat.VatPath, true),
                ("Brutto", vat.GrossPath, true),
            };

            var head = new ReportHeaderBand { HeightF = 34 };
            head.Controls.Add(new XRLabel
            {
                LeftF = left,
                WidthF = width,
                HeightF = 14,
                Text = "Zestawienie wedlug stawek VAT",
                Font = new DXFont(FontFamily, 9, DXFontStyle.Bold),
            });
            var caption = BuildVatRow(columns, width, currency, RowMode.Header);
            caption.LeftF = left;
            caption.TopF = 16;
            head.Controls.Add(caption);
            report.Bands.Add(head);

            // Grupa po stawce — naglowek grupy jest niewidoczny, liczy sie tylko stopka.
            var group = new GroupHeaderBand { HeightF = 0, Visible = false };
            group.GroupFields.Add(new GroupField(vat.RatePath, XRColumnSortOrder.Ascending));
            report.Bands.Add(group);

            // Wiersze pozycji sa ukryte — w tabelce stawek maja sie pokazac wylacznie podsumowania.
            // Visible = false NIE psuje agregatow: silnik i tak przechodzi po wszystkich wierszach.
            report.Bands.Add(new DetailBand { HeightF = 0, Visible = false });

            var groupFooter = new GroupFooterBand { HeightF = 15 };
            var perRate = BuildVatRow(columns, width, currency, RowMode.GroupSummary);
            perRate.LeftF = left;
            groupFooter.Controls.Add(perRate);
            report.Bands.Add(groupFooter);

            var reportFooter = new ReportFooterBand { HeightF = 17 };
            var total = BuildVatRow(columns, width, currency, RowMode.ReportSummary);
            total.LeftF = left;
            reportFooter.Controls.Add(total);
            report.Bands.Add(reportFooter);
            return report;
        }

        enum RowMode { Header, GroupSummary, ReportSummary }

        static XRTable BuildVatRow((string Caption, string Path, bool Money)[] columns,
            float width, string currency, RowMode mode)
        {
            var table = new XRTable { WidthF = width, HeightF = mode == RowMode.Header ? 16 : 15 };
            var row = new XRTableRow();
            table.Rows.Add(row);

            var cellWidth = width / columns.Length;
            foreach (var (caption, path, money) in columns)
            {
                var cell = NewCell(cellWidth, bold: mode != RowMode.GroupSummary, right: money);
                switch (mode)
                {
                    case RowMode.Header:
                        cell.Text = caption;
                        break;
                    case RowMode.GroupSummary when money:
                    case RowMode.ReportSummary when money:
                        cell.Summary = new XRSummary
                        {
                            Running = mode == RowMode.GroupSummary ? SummaryRunning.Group : SummaryRunning.Report,
                            Func = SummaryFunc.Sum,
                            FormatString = "{0:N2} " + currency,
                        };
                        cell.ExpressionBindings.Add(new ExpressionBinding("BeforePrint", "Text", $"sumSum([{path}])"));
                        break;
                    case RowMode.GroupSummary:
                        cell.ExpressionBindings.Add(new ExpressionBinding("Text", $"[{path}]"));
                        break;
                    case RowMode.ReportSummary:
                        cell.Text = "Razem";
                        break;
                }
                row.Cells.Add(cell);
            }
            return table;
        }

        // ------------------------------------------------------------------ narzedzia

        static XtraReport NewSubreport(object lineDataSource, string backReferencePath)
        {
            var report = new XtraReport { DataSource = lineDataSource };
            report.Parameters.Add(new Parameter
            {
                Name = DocKeyParameter,
                Type = typeof(Guid),
                Visible = false,
            });
            // Kluczowe: bez kolekcji dzieci na typie runtime'owym jedynym wiazaniem master-detail
            // jest filtr po kluczu naglowka przekazanym parametrem.
            report.FilterString = $"[{backReferencePath}.Oid] = ?{DocKeyParameter}";
            return report;
        }

        static XRTableCell NewCell(float width, bool bold, bool right) => new()
        {
            WidthF = width,
            Font = new DXFont(FontFamily, 8.5f, bold ? DXFontStyle.Bold : DXFontStyle.Regular),
            Padding = new DevExpress.XtraPrinting.PaddingInfo(3, 3, 1, 1),
            BorderColor = Color.Silver,
            Borders = DevExpress.XtraPrinting.BorderSide.All,
            BackColor = bold ? Color.WhiteSmoke : Color.Transparent,
            TextAlignment = right
                ? DevExpress.XtraPrinting.TextAlignment.MiddleRight
                : DevExpress.XtraPrinting.TextAlignment.MiddleLeft,
        };

        /// <summary>
        /// Wpina slot w kontrolke: pole idzie wyrazeniem, staly tekst zwyklym Text.
        /// <paramref name="expressionFormat"/> to szablon z {0} w miejscu na <c>[Sciezka]</c>.
        /// </summary>
        static void Bind(XRControl control, InvoiceSlot slot, string expressionFormat)
        {
            if (slot.IsLiteral)
            {
                if (expressionFormat == "{0}")
                {
                    control.Text = slot.Value;
                    return;
                }
                control.ExpressionBindings.Add(new ExpressionBinding("Text",
                    string.Format(expressionFormat, "'" + Escape(slot.Value) + "'")));
                return;
            }
            control.ExpressionBindings.Add(new ExpressionBinding("Text",
                string.Format(expressionFormat, "[" + slot.Value + "]")));
        }

        static string Escape(string text) => (text ?? string.Empty).Replace("'", "''");

        static InvoiceSlot Find(IReadOnlyList<InvoiceSlot> slots, TemplateFieldKind kind)
            => slots.FirstOrDefault(s => s.Kind == kind);
    }
}
