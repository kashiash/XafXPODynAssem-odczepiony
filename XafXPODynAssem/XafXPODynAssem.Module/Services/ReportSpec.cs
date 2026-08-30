using DevExpress.Drawing.Printing;

namespace XafXPODynAssem.Module.Services
{
    public enum ReportOrientation
    {
        Portrait = 0,
        Landscape = 1
    }

    /// <summary>
    /// Wejscie deterministycznego budowniczego raportow (<see cref="ReportSpecBuilder"/>).
    ///
    /// Zwykly obiekt w pamieci — celowo NIE jest encja XPO. Narzedzia AI skladaja go
    /// z parametrow wywolania i porzucaja po zbudowaniu raportu; jedynym trwalym
    /// artefaktem jest ReportDataV2. Wersja utrwalana (BaseObject + DefaultClassOptions)
    /// dawala tylko pusta tabele i pusta pozycje w nawigacji.
    ///
    /// Zawiera wylacznie pola, ktore <see cref="ReportSpecBuilder.Build"/> naprawde czyta —
    /// kolumny, grupowanie i sortowanie ida osobnymi parametrami Build.
    /// </summary>
    public sealed class ReportSpec
    {
        /// <summary>Domyslny margines (mm) — narzedzia MUSZA go pokazac uzytkownikowi w odpowiedzi.</summary>
        public const int DefaultMarginMm = 10;

        public string Title { get; set; }

        /// <summary>Filtr w skladni DevExpress Criteria.</summary>
        public string FilterCriteria { get; set; }

        /// <summary>Po jednej linii na wiersz; tekst z [NazwaPola] podstawia wartosc z rekordu.</summary>
        public string HeaderLines { get; set; }

        /// <summary>Pola liczbowe sumowane w pasmie podsumowania, po przecinku.</summary>
        public string SummaryFields { get; set; }

        public bool SortDescending { get; set; }

        public ReportOrientation Orientation { get; set; } = ReportOrientation.Portrait;

        public DXPaperKind PaperKind { get; set; } = DXPaperKind.A4;

        public int MarginLeft { get; set; } = DefaultMarginMm;
        public int MarginTop { get; set; } = DefaultMarginMm;
        public int MarginRight { get; set; } = DefaultMarginMm;
        public int MarginBottom { get; set; } = DefaultMarginMm;
    }
}
