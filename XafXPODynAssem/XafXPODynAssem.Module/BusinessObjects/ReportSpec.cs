using System.ComponentModel;
using DevExpress.Drawing.Printing;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;

namespace XafXPODynAssem.Module.BusinessObjects
{
    /// <summary>Cykl życia specyfikacji raportu budowanej w czacie AI.</summary>
    public enum ReportSpecStatus
    {
        /// <summary>Szkic — powstał z pierwszego zdania użytkownika, może być niekompletny.</summary>
        Draft = 0,
        /// <summary>Zwalidowany bez braków — <c>build_report</c> może działać.</summary>
        Ready = 1,
        /// <summary>Raport zbudowany i zapisany do ReportDataV2.</summary>
        Built = 2
    }

    public enum ReportOrientation
    {
        Portrait = 0,
        Landscape = 1
    }

    /// <summary>
    /// Specyfikacja raportu XtraReports budowanego przez narzędzia AI na encji runtime'owej.
    /// Nośnik dopytywania: narzędzia AI zakładają <see cref="ReportSpecStatus.Draft"/>,
    /// <c>validate_report_spec</c> zwraca braki jako dane, a <c>build_report</c> odmawia,
    /// dopóki braki istnieją.
    /// </summary>
    [DefaultClassOptions]
    [NavigationItem("Zarządzanie schematem")]
    [DefaultProperty(nameof(Title))]
    [XafDisplayName("Specyfikacja raportu")]
    public class ReportSpec : BaseObject
    {
        /// <summary>Domyślny margines (mm) — narzędzia MUSZĄ go pokazać użytkownikowi w odpowiedzi.</summary>
        public const int DefaultMarginMm = 10;

        public ReportSpec(Session session) : base(session) { }

        public override void AfterConstruction()
        {
            base.AfterConstruction();
            status = ReportSpecStatus.Draft;
            orientation = ReportOrientation.Portrait;
            paperKind = DXPaperKind.A4;
            marginLeft = marginTop = marginRight = marginBottom = DefaultMarginMm;
        }

        string title;
        [XafDisplayName("Tytuł raportu")]
        public string Title
        {
            get => title;
            set => SetPropertyValue(nameof(Title), ref title, value);
        }

        CustomClass targetClass;
        [XafDisplayName("Encja docelowa")]
        public CustomClass TargetClass
        {
            get => targetClass;
            set => SetPropertyValue(nameof(TargetClass), ref targetClass, value);
        }

        string fieldPaths;
        [Size(SizeAttribute.Unlimited)]
        [XafDisplayName("Pola (ścieżki, po przecinku)")]
        public string FieldPaths
        {
            get => fieldPaths;
            set => SetPropertyValue(nameof(FieldPaths), ref fieldPaths, value);
        }

        string groupByField;
        [XafDisplayName("Grupowanie po polu")]
        public string GroupByField
        {
            get => groupByField;
            set => SetPropertyValue(nameof(GroupByField), ref groupByField, value);
        }

        string sortByField;
        [XafDisplayName("Sortowanie po polu")]
        public string SortByField
        {
            get => sortByField;
            set => SetPropertyValue(nameof(SortByField), ref sortByField, value);
        }

        bool sortDescending;
        [XafDisplayName("Sortowanie malejąco")]
        public bool SortDescending
        {
            get => sortDescending;
            set => SetPropertyValue(nameof(SortDescending), ref sortDescending, value);
        }

        string filterCriteria;
        [Size(SizeAttribute.Unlimited)]
        [XafDisplayName("Filtr (DevExpress Criteria)")]
        public string FilterCriteria
        {
            get => filterCriteria;
            set => SetPropertyValue(nameof(FilterCriteria), ref filterCriteria, value);
        }

        // -- Układ dokumentowy (faktura, protokół, zamówienie) ----------------
        // Raport listowy ma sam nagłówek + tabelę. Dokument ma jeszcze blok nagłówkowy
        // z polami (numer, data, kontrahent) i podsumowanie pod tabelą.

        string headerLines;
        [Size(SizeAttribute.Unlimited)]
        [XafDisplayName("Linie nagłówka dokumentu")]
        [Description("Po jednej linii na wiersz. Tekst z [NazwaPola] podstawia wartość z rekordu, " +
                     "np. „Faktura nr [NumerFaktury]”.")]
        public string HeaderLines
        {
            get => headerLines;
            set => SetPropertyValue(nameof(HeaderLines), ref headerLines, value);
        }

        string summaryFields;
        [Size(SizeAttribute.Unlimited)]
        [XafDisplayName("Pola do podsumowania (po przecinku)")]
        [Description("Pola liczbowe sumowane w pasmie podsumowania pod tabelą.")]
        public string SummaryFields
        {
            get => summaryFields;
            set => SetPropertyValue(nameof(SummaryFields), ref summaryFields, value);
        }

        // -- Układ strony ----------------------------------------------------

        ReportOrientation orientation;
        [XafDisplayName("Orientacja")]
        public ReportOrientation Orientation
        {
            get => orientation;
            set => SetPropertyValue(nameof(Orientation), ref orientation, value);
        }

        DXPaperKind paperKind;
        [XafDisplayName("Format papieru")]
        public DXPaperKind PaperKind
        {
            get => paperKind;
            set => SetPropertyValue(nameof(PaperKind), ref paperKind, value);
        }

        int marginLeft;
        [XafDisplayName("Margines lewy (mm)")]
        public int MarginLeft
        {
            get => marginLeft;
            set => SetPropertyValue(nameof(MarginLeft), ref marginLeft, value);
        }

        int marginTop;
        [XafDisplayName("Margines górny (mm)")]
        public int MarginTop
        {
            get => marginTop;
            set => SetPropertyValue(nameof(MarginTop), ref marginTop, value);
        }

        int marginRight;
        [XafDisplayName("Margines prawy (mm)")]
        public int MarginRight
        {
            get => marginRight;
            set => SetPropertyValue(nameof(MarginRight), ref marginRight, value);
        }

        int marginBottom;
        [XafDisplayName("Margines dolny (mm)")]
        public int MarginBottom
        {
            get => marginBottom;
            set => SetPropertyValue(nameof(MarginBottom), ref marginBottom, value);
        }

        // -- Stan ------------------------------------------------------------

        ReportSpecStatus status;
        [XafDisplayName("Status")]
        public ReportSpecStatus Status
        {
            get => status;
            set => SetPropertyValue(nameof(Status), ref status, value);
        }

        string builtReportKey;
        [XafDisplayName("Klucz zbudowanego raportu")]
        public string BuiltReportKey
        {
            get => builtReportKey;
            set => SetPropertyValue(nameof(BuiltReportKey), ref builtReportKey, value);
        }
    }
}
