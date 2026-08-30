# Decyzje na bramkach orkiestratora Maister

Użytkownik w zleceniu tej sesji wprost zniósł zatrzymywanie się na bramkach:
„Na bramkach/gate'ach orkiestratora **nie zatrzymuj się i nie pytaj** — wybierz rozsądną opcję,
zapisz w raporcie, którą i dlaczego, i kontynuuj."

Dodatkowo: pracuję jako subagent i **nie mam narzędzia `AskUserQuestion`** w swoim zestawie —
bramki są fizycznie niewykonalne, nie tylko zniesione decyzją użytkownika.

| # | Bramka | Wybór | Uzasadnienie |
|---|---|---|---|
| 0 | `maister:work` — klasyfikacja | `development` bez wywołania `task-classifier` | Zadanie modyfikuje kod i dodaje funkcję. Klasyfikacja jednoznaczna; round-trip do subagenta to koszt bez informacji. |
| 0b | Dashboard operatora (`html_output`) | wyłączony | Brak `.maister/config.yml`; nikt nie ogląda dashboardu — użytkownik czyta raport końcowy. Budżet idzie na kryteria sukcesu. |
| 1 | Faza 1 — `codebase-analyzer` | analiza inline zamiast Skilla | Pełną analizę (SchemaAIToolsProvider, AIChatService, ReportsV2, reflekcja po DLL) mam już w kontekście z rozpoznania przed wejściem w Maistera. Ponowne wyprowadzanie jej przez subagenta to duplikat. |
| 2 | Faza 2 — `gap-analyzer` + DECISION GATE | inline; brak `decisions_needed` | Luka jest jednozdaniowa i już zdiagnozowana czterema dowodami w zleceniu. Zapisana w `gap-analysis.md`. |
| 3 | Faza 3 — TDD Red Gate | **pominięta** | `has_reproducible_defect: false`. To nie jest defekt do odtworzenia — ścieżka nigdy nie istniała. Nie ma projektu testowego w repo (`XafXPODynAssem.slnx` nie zawiera projektu testów), więc czerwony test nie miałby gdzie żyć. Weryfikacja jest runtime'owa (3 twarde kryteria). |
| 4 | Faza 4 — makiety UI | **pominięta** | `ui_heavy: false`. Nie dodaję żadnego ekranu — używam istniejącego czatu i wbudowanego projektanta raportów XAF. |
| 5 | Faza 5 — `specification-creator` | spec pisana inline | Zakres to ~2 narzędzia AI + zapis do ReportDataV2. Spec ma 1 stronę; delegacja kosztowałaby więcej niż sam artefakt. |
| 6 | Faza 6 — audyt specyfikacji | **pominięty** | Rolę niezależnego adwersarza pełni w tej sesji `advisor` — wywołany przed implementacją, wyłapał realny bloker (brak `set_DataTypeName`), którego audytor spec by nie zobaczył. |
| 7 | Faza 7 — `implementation-planner` | plan pisany inline | Plan to 5 kroków w 2 plikach. |
| 8 | Faza 8 — `implementation-plan-executor` | implementacja bezpośrednia | Jedna spójna zmiana w 2 plikach; rozbicie na subagentów wprowadza ryzyko rozjazdu kontraktu bez zysku. |
| 10 | Faza 10 — opcje weryfikacji | tylko build + weryfikacja runtime | Trzy kryteria sukcesu z zlecenia są twardsze niż statyczne przeglądy i to one rozstrzygają. |
| 11/12 | Weryfikacja + E2E | wykonana | Sekcja „Kryteria sukcesu" w raporcie końcowym. |

## Świadomy dług

Delegacja do subagentów Maistera została w większości zastąpiona pracą inline. Zysk: budżet
kontekstu poszedł na weryfikację runtime (kryteria 2 i 3), która jest jedynym wiarygodnym
dowodem w tym zadaniu. Koszt: brak niezależnego drugiego spojrzenia na kod poza `advisor`.
