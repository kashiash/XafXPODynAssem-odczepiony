# Weryfikacja runtime — trzy kryteria sukcesu

Środowisko: lokalny macOS, `ASPNETCORE_ENVIRONMENT=Development`, porty 5030/5031
(5000/5001 zajęte przez proces innej sesji, PID 25109 — nietknięty).
Baza: lokalny PostgreSQL `XafXPODynAssem`. Model: `gpt-5.6-luna` przez Azure OpenAI
(polandcentral), klucz z `az`, nigdy w repo.

Prompt wpisany w czacie:

> Zrób raport z encji Produkt. Kolumny: NazwaProduktu, JednostkaMiary, CenaJednostkowa.
> Posortuj po CenaJednostkowa malejąco. Tytuł raportu: Cennik produktów.

## Kryterium 1 — prompt uruchamia narzędzie raportowe ✅

```
[AskAsync] Sending (model=gpt-5.6-luna, provider=OpenAi, tools=12, history=0)
[ToolLoop] Iteration 1: 1 tool call(s)
[Tool:validate_report_spec] Called with entity=Produkt, fields=NazwaProduktu,JednostkaMiary,CenaJednostkowa
[Tool:validate_report_spec] Valid, 3 column(s)
[ToolLoop] Iteration 2: 1 tool call(s)
[Tool:build_report] Called with entity=Produkt, fields=NazwaProduktu,JednostkaMiary,CenaJednostkowa, title=Cennik produktów, groupBy=(null), sortBy=CenaJednostkowa
[Tool:build_report] Saved ReportDataV2 key=5f201062-a6e8-400e-bbe0-bfb02eee9aa4, DisplayName='Cennik produktów', DataTypeName='XafXPODynAssem.RuntimeEntities.Produkt', columns=3
[AskAsync] Response: 238 chars, 2 tool iterations
```

`tools=12` (było 10). Model sam wybrał kolejność walidacja → budowa, bez podpowiedzi w promptcie.
W całym logu zero wyjątków i zero linii `Error`.

## Kryterium 2 — wiersz w ReportDataV2, potwierdzony SQL ✅

Stan przed: `SELECT count(*) FROM "ReportDataV2"` → `0`.

Po:

```
$ psql -d XafXPODynAssem -c 'SELECT "Oid","Name","ObjectTypeName", octet_length("Content") AS content_bytes, "GCRecord" FROM "ReportDataV2";'

                 Oid                  |       Name       |             ObjectTypeName             | content_bytes | GCRecord
--------------------------------------+------------------+----------------------------------------+---------------+----------
 5f201062-a6e8-400e-bbe0-bfb02eee9aa4 | Cennik produktów | XafXPODynAssem.RuntimeEntities.Produkt |          3784 |
(1 row)
```

Dokładnie jeden wiersz — mimo że pętla narzędziowa zalogowała „Response still has tool calls,
continuing loop", `build_report` wykonał się raz. `Oid` zgadza się z kluczem z logu.

**Uwaga do nazewnictwa:** w bazie kolumna nazywa się `ObjectTypeName`, nie `DataTypeName`.
`DataTypeName` to właściwość CLR wyliczana z zapisanego `Content` — dlatego nie ma settera.
To potwierdza diagnozę: typ mógł wejść wyłącznie przez źródło danych raportu, nie przez
przypisanie do `DataTypeName`.

## Kryterium 3 — projektant i wykonanie ✅

**Wykonanie** (`/ReportViewer_DetailView/5f201062-…`): dokument wyrenderowany, „1 of 1" stron,
zrzut `report-preview.png`. Widać:

| NazwaProduktu | JednostkaMiary | CenaJednostkowa |
|---|---|---|
| Laptop Dell Latitude 5540 | szt | 5499,00000000 |
| Monitor Dell U2723QE | szt | 2199,00000000 |
| Klawiatura Logitech MX Keys | szt | 449,00000000 |
| Kabel HDMI 2.1 2m | szt | 89,00000000 |
| Papier ksero A4 80g | ryza | 24,50000000 |

Nagłówek „Cennik produktów", stopka „Strona 1 z 1", sortowanie malejąco po cenie — zgodnie
z promptem. Wszystkie 5 zasianych wierszy się związało.

**Projektant** (`/ReportDesigner_DetailView/5f201062-…`): otwiera się w pełni, zrzut
`report-designer.png`. Pasma: TopMargin, ReportHeader, PageHeader, Detail, PageFooter,
BottomMargin. Detail ma wyrażenia `[Nazwa Produktu]`, `[Jednostka Miary]`, `[Cena Jednostkowa]`.
Panel Właściwości → **Źródło danych = `CollectionDataSource`** — dowód, że wiązanie jest
poprawne i widoczne dla projektanta.

## Czego NIE zweryfikowano

- **Kontener `xpodyn` na Proxmoksie.** Weryfikacja poszła w całości lokalnie. Playbook
  `/Volumes/MyNet/proxmox/README.md` (stan 30.08.2026) wymienia tylko VM 100 `win11` i LXC 200
  `docker` — kontenera `xpodyn` tam nie ma. Koordynator ostrzegł, że obraz na serwerze stoi na
  starszym runtime i wymagałby przebudowy; lokalny test omija ten problem i mierzy dokładnie ten
  kod, który jest w commicie.
- **Ścieżka Win.** `XafXPODynAssem.Win` nie kompiluje się na macOS (`NETSDK1100`,
  `EnableWindowsTargeting`) — to stan zastany, nie regresja tej zmiany. Kod raportowy leży
  w projekcie Module wspólnym dla obu platform, ale na WinForms go nie uruchomiono.
- **Grupowanie i filtr.** `groupByField` i `filterCriteria` są zaimplementowane i walidowane,
  ale w teście runtime nie zostały użyte (model ich nie potrzebował). Przetestowano ścieżkę
  kolumny + sortowanie.
- **Zachowanie przy włączonym XAF Security.** Narzędzia chodzą przez
  `INonSecuredObjectSpaceFactory`, tak jak wszystkie pozostałe — omijają Security. Nie
  pogorszono, ale i nie naprawiono.
