using System.Collections.Generic;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Ganss.Xss;

namespace XafXPODynAssem.Module.Services
{
    /// <summary>
    /// Single source of truth for AI chat UI configuration shared
    /// across platform projects.
    /// </summary>
    public static class AIChatDefaults
    {
        // -- Header / Empty State -------------------------------------------

        public const string HeaderText = "Asystent schematu AI";

        public const string EmptyStateText =
            "Zapytaj o cokolwiek związanego ze schematem — tworzenie encji, dodawanie pól, zarządzanie relacjami i więcej.\nDziała na LLMTornado.";

        // -- Prompt Suggestions ---------------------------------------------

        /// <summary>
        /// Lightweight DTO used to build suggestion controls.
        /// </summary>
        public record PromptSuggestionItem(string Title, string Text, string Prompt);

        public static IReadOnlyList<PromptSuggestionItem> PromptSuggestions { get; } = new List<PromptSuggestionItem>
        {
            new("Utwórz encję",
                "Nowa encja w czasie działania",
                "Utwórz nową encję o nazwie Pracownik z polami: Imie (string), Nazwisko (string), Email (string), DataZatrudnienia (DateTime), Wynagrodzenie (decimal)"),

            new("Lista encji",
                "Pokaż encje i ich pola",
                "Wypisz wszystkie encje utworzone w czasie działania wraz z polami i bieżącym statusem"),

            new("Dodaj pola",
                "Rozszerz istniejącą encję",
                "Pokaż niezatwierdzone zmiany i pomóż mi dodać nowe pola do encji"),

            new("Uprawnienia",
                "Dostęp według ról",
                "Pomóż mi ustawić uprawnienia rolowe dla encji utworzonych w czasie działania"),

            new("Przygotuj raport",
                "Rozpiska z grupowaniem i sumami",
                "Zbuduj raport na encji SprzedazMiesieczna: tytuł „Sprzedaż miesięczna”, kolumny Miesiac, Kontrahent, WartoscNetto, WartoscVat, LiczbaFaktur, grupowanie po Kontrahent, sortowanie po Miesiac, sumowanie w grupach po WartoscNetto, WartoscVat i LiczbaFaktur, A4 poziomo. Jeśli encja jeszcze nie istnieje albo nie jest wdrożona, powiedz mi wprost, czego brakuje i co mam kliknąć."),
        };

        // -- Markdown to HTML -----------------------------------------------

        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseEmphasisExtras()
            .UseAutoLinks()
            .UseTaskLists()
            .Build();

        private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

        private static HtmlSanitizer CreateSanitizer()
        {
            var sanitizer = new HtmlSanitizer();
            // Ensure table tags survive sanitization
            foreach (var tag in new[] { "table", "thead", "tbody", "tr", "th", "td" })
                sanitizer.AllowedTags.Add(tag);
            return sanitizer;
        }

        /// <summary>
        /// Converts a Markdown string to sanitized HTML.
        /// Thread-safe — the pipeline and sanitizer instances are reentrant.
        /// </summary>
        public static string ConvertMarkdownToHtml(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
                return string.Empty;

            var html = Markdown.ToHtml(markdown, Pipeline);
            return Sanitizer.Sanitize(html);
        }
    }
}
