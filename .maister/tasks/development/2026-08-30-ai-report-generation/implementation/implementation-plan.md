# Plan implementacji — narzędzia raportowe AI

## TL;DR

Dwa nowe narzędzia AI w istniejącym providerze + przywrócenie źródła danych w builderze.
Dwa pliki dotknięte, zero nowych zależności, zero zmian w rejestracji DI.

## Kluczowe decyzje

- **Płaskie parametry zamiast obiektu.** `build_report` przyjmuje `entityName`, `fieldPaths`, `title`… jako osobne argumenty skalarne. Powód: `AIFunctionFactory.Create` generuje z nich JSON Schema, a `AIChatService.ExecuteToolAsync` deserializuje do `Dictionary<string, object>` — zagnieżdżony obiekt przeszedłby przez tę ścieżkę jako `JsonElement` i wymagałby ręcznego mapowania.
- **`ReportSpec` w pamięci, bez sesji XPO.** `ReportSpecBuilder.Build` czyta ze `spec` tylko właściwości POCO. `ReportSpec` dziedziczy po `BaseObject`, więc potrzebuje `Session` — używam `new ReportSpec(nested session)` z tego samego ObjectSpace, ale **nie** commituję. Alternatywa odrzucona: rozbicie sygnatury buildera na parametry (zmiana cudzego kontraktu bez potrzeby).
- **Walidacja jest współdzielona.** `validate_report_spec` i `build_report` wołają ten sam prywatny `ValidateReportRequest`; `build_report` odmawia, gdy walidacja zwraca braki. Dzięki temu narzędzia nie mogą się rozjechać.

## Grupy zadań

### Grupa 1 — Źródło danych w builderze (`Services/ReportSpecBuilder.cs`)

- [x] 1.1 Przywrócić `report.DataSource = new CollectionDataSource { ObjectTypeName = runtimeTypeFullName }` (using `DevExpress.Persistent.Base.ReportsV2`), zastępując komentarz z linii 55.
- [x] 1.2 Przenieść `FilterString` → `CollectionDataSource.CriteriaString` (filtr na źródle działa po stronie zapytania; `FilterString` raportu filtruje po pobraniu).

**Kryterium odbioru:** build przechodzi; `report.DataSource` jest typu `CollectionDataSource` z ustawionym `ObjectTypeName`.

### Grupa 2 — Narzędzia AI (`Services/SchemaAIToolsProvider.cs`)

- [x] 2.1 `ResolveRuntimeType(string entityName)` — dopasowanie po `Name` w `XafXPODynAssemModule.AssemblyManager.RuntimeTypes`, bez rozróżniania wielkości liter; przy braku zwraca listę dostępnych nazw.
- [x] 2.2 `ValidateReportRequest(...)` — sprawdza: encja istnieje, pola istnieją na typie (case-insensitive, z naprawą wielkości liter), `groupBy`/`sortBy` są wśród znanych pól. Zwraca listę braków.
- [x] 2.3 `ValidateReportSpec(...)` — narzędzie AI, zwraca raport walidacji jako markdown, nic nie zapisuje.
- [x] 2.4 `BuildReport(...)` — waliduje, buduje `ReportSpec` (in-memory), woła `ReportSpecBuilder.Build`, zapisuje przez `IReportStorage.SaveReport`, commituje, zwraca potwierdzenie z kluczem.
- [x] 2.5 Rejestracja obu w `CreateTools()`; logi `[Tool:build_report] Called` / `[Tool:validate_report_spec] Called` w kształcie zgodnym z pozostałymi 10 narzędziami.

**Kryterium odbioru:** `dotnet build` czysty; `Tools.Count == 12`.

### Grupa 3 — Weryfikacja runtime

- [x] 3.1 Zasiać wiersze do tabeli `Produkt` (wszystkie tabele runtime są puste).
- [x] 3.2 Uruchomić aplikację na wolnym porcie, wywołać prompt w czacie, złapać `[Tool:build_report] Called` w logu.
- [x] 3.3 `SELECT "DisplayName","DataTypeName" FROM "ReportDataV2"` — wiersz z niepustym `DataTypeName`.
- [x] 3.4 Otworzyć raport w projektancie XAF i wykonać podgląd (Playwright, własna karta).

## Poza zakresem

- `AIChatReportDesignerController` i podpięcie `ReportDesignerSink` — `SchemaAIToolsProvider` jest singletonem, sink zaprojektowano jako scoped per obwód Blazor; narzędzie-singleton nie sięgnie instancji sinka z obwodu czatu. Rozwiązanie tej niezgodności zasięgów to osobne zadanie, a kryterium #3 mówi „raport da się otworzyć w projektancie", nie „czat sam go otwiera".
- Naprawa omijania XAF Security przez narzędzia AI.
- Utrwalanie `ReportSpec` jako encji (klasa zostaje, ale żadne narzędzie jej nie zapisuje).
