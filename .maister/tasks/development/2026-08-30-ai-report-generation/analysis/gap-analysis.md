# Analiza luki — generowanie raportów przez agenta AI

## TL;DR

Kod budujący raport istnieje, ale nie ma ani jednego wywołania. Brakuje trzech ogniw: narzędzi
AI po stronie providera, mostu „narzędzie → `XtraReport`" i zapisu do `ReportDataV2`. Do tego
kontrakt przyjęty przez poprzednią sesję (typ danych przez `ReportDataV2.DataTypeName`) jest
**niewykonalny** — ta właściwość nie ma settera.

## Kluczowe decyzje

1. Typ danych wnosi `CollectionDataSource.ObjectTypeName`, nie `ReportDataV2.DataTypeName`.
2. Narzędzia raportowe dołączają do istniejącego `SchemaAIToolsProvider` (nie nowy provider) — dzięki temu wpinają się w działającą pętlę tool-callingu bez zmian w `AIChatService` i `AIServiceCollectionExtensions`.
3. `ReportSpec` nie jest utrwalany. Narzędzia budują go w pamięci (`new ReportSpec(session)` w tymczasowym ObjectSpace nie jest potrzebny — patrz spec).
4. `ReportDesignerSink` zostaje niepodpięty — świadomie, poza zakresem.

## Stan zastany vs stan docelowy

| Element | Zastany | Docelowy |
|---|---|---|
| Narzędzia AI | 10, żadnego raportowego | 12 — `+ validate_report_spec`, `+ build_report` |
| Call-site `ReportSpecBuilder.Build` | brak | `SchemaAIToolsProvider.BuildReport` |
| Źródło danych raportu | brak (usunięte w poprzedniej sesji) | `CollectionDataSource { ObjectTypeName = <typ runtime> }` |
| `ReportDataV2` | 0 wierszy | ≥1 wiersz z niepustym `DataTypeName` |
| Podpowiedź „Dane pod raport" | prowadzi donikąd | prowadzi do `build_report` |

## Bloker znaleziony w analizie (koryguje zlecenie)

Zlecenie mówi: *„Zastąpiono ją kanonicznym kontraktem XAF — raport nie trzyma `DataSource`, typ
ustawia wywołujący przez `ReportDataV2.DataTypeName`. […] Nie cofaj jej."*

Reflekcja po `DevExpress.Persistent.BaseImpl.Xpo.v26.1.dll` pokazuje, że `ReportDataV2` ma
`get_DataTypeName()` i **nie ma** `set_DataTypeName` — w przeciwieństwie do `DisplayName`,
`Content`, `IsInplaceReport`, `ParametersObjectTypeName`, które settery mają. `DataTypeName` jest
pochodną, wyliczaną ze źródła danych zapisanego raportu.

Uzasadnienie usunięcia (*„klasy `CollectionDataSource` nie ma wśród typów publicznych ani
forwardowanych w DevExpress 26.1.4, przeskanowano 377 assembly"*) też jest błędne. Klasa istnieje:

```
DevExpress.Persistent.Base.v26.1.dll :: DevExpress.Persistent.Base.ReportsV2.CollectionDataSource
```

— w przestrzeni `DevExpress.Persistent.Base.ReportsV2`, nie `DevExpress.ExpressApp.ReportsV2`,
gdzie najwyraźniej szukano. Pakiet `DevExpress.Persistent.Base` jest już referencją modułu.
`ObjectTypeName` (settable, `string`) dziedziczy z `DataSourceBase`.

**Wniosek:** instrukcja „nie cofaj tej zmiany" opiera się na nieprawdziwej przesłance. Źródło
danych wraca — bez niego kryterium sukcesu #3 (raport da się wykonać) jest nieosiągalne, bo
projektant nie ma czego związać.

## Ryzyka

| Ryzyko | Wpływ | Mitygacja |
|---|---|---|
| `SaveReport` nie wypełni `DataTypeName` | kryterium #3 pada | Fallback: ctor `ReportDataV2(Session, Type)` przez `((XPObjectSpace)os).Session`. Sprawdzian: `SELECT "DataTypeName" FROM "ReportDataV2"` — NULL = porażka. |
| Model wywoła `create_entity` zamiast `build_report` → `Environment.Exit(42)` → zerwany obwód | test runtime niemożliwy | Prompt wskazuje encję już wdrożoną (Status=Runtime). Sprawdzone: 7 takich encji. |
| Wszystkie tabele runtime puste → podgląd bez wierszy wygląda jak błąd | mylny werdykt | Zasiać kilka wierszy do `Produkt` przed testem podglądu. |
| Narzędzia chodzą przez `INonSecuredObjectSpaceFactory` (omijają XAF Security) | dług bezpieczeństwa | Nie pogarszam: używam istniejącego prywatnego helpera `CreateObjectSpaceForType`, nie dokładam nowej ścieżki. Opisane w raporcie. |
