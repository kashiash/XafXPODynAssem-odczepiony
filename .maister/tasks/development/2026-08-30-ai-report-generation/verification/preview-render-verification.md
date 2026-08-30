# Weryfikacja runtime — `preview_report`, render dokumentów, dialog

Środowisko jak poprzednio: lokalnie, porty 5030/5031, `ASPNETCORE_ENVIRONMENT=Development`,
model `gpt-5.6-luna` przez Azure OpenAI. Build z commita `ff8470e`.

Dane zasiane pod ten test (wszystkie tabele runtime były puste):
4 klienci (2 w Katowicach), 2 sposoby płatności, 3 faktury, 5 pozycji faktur.

## Kryterium 1 — ślad w logu, `tools=13`, zero wyjątków ✅

```
[AskAsync] Sending (model=gpt-5.6-luna, provider=OpenAi, tools=13, history=0)
[Tool:preview_report] Called with entity=Produkt, fields=NazwaProduktu,JednostkaMiary,CenaJednostkowa, filter=(null), maxRows=10, render=False, samples=3, key=(null)
[Tool:preview_report] Returned 5 of 5 row(s), rendered=False
```

W całym przebiegu zero linii `Error` i zero wyjątków.

## Kryterium 2 — prawdziwe dane, porównane z SQL ✅

Treść zwrócona do czatu (odczytana z DOM-u):

| NazwaProduktu | JednostkaMiary | CenaJednostkowa |
|---|---|---|
| Laptop Dell Latitude 5540 | szt | 5 499,00 |
| Monitor Dell U2723QE | szt | 2 199,00 |
| Klawiatura Logitech MX Keys | szt | 449,00 |
| Papier ksero A4 80g | ryza | 24,50 |
| Kabel HDMI 2.1 2m | szt | 89,00 |

„Zwrócono 5 z 5 rekordów."

SQL (`SELECT "NazwaProduktu","JednostkaMiary","CenaJednostkowa" FROM "Produkt" WHERE "GCRecord" IS NULL`)
zwraca dokładnie te same 5 wierszy i te same wartości. Zgodność 1:1.

## Kryterium 3 — filtr zawęża zbiór, potwierdzone SQL ✅

Prompt: „A teraz pokaż tylko produkty droższe niż 1000 zł." — model sam przełożył to na kryterium:

```
[Tool:preview_report] Called with ... filter=CenaJednostkowa > 1000, maxRows=10, render=False
[Tool:preview_report] Returned 2 of 2 row(s), rendered=False
```

| | bez filtru | z filtrem |
|---|---|---|
| narzędzie | 5 z 5 | **2 z 2** |
| SQL `WHERE "CenaJednostkowa">1000` | 5 | **2** (Laptop 5499, Monitor 2199) |

Zbiór węższy, liczby zgodne z SQL.

## Kryterium 4 — plik istnieje, niezerowy, widać na nim treść z promptu ✅

Trzy dokumenty, po jednym na numer faktury:

```
PozycjaFaktury-FV-2026-08-001-170723.png   17992 B
PozycjaFaktury-FV-2026-08-002-170724.png   11985 B
PozycjaFaktury-FV-2026-08-003-170724.png   12550 B
```

Obejrzane. Na `FV/2026/08/001` widać:

- tytuł **Faktura**
- nagłówek: `Data: 12.08.2026 00:00:00`, `Kontrahent: Slaskie Systemy IT Sp. z o.o.` — czyli
  dokładnie to, co użytkownik podyktował („w nagłówku ma być data i kontrahent")
- tabelę `OpisPozycji | Ilosc | WartoscBrutto` — czyli „opis, ilość, wartość", brutto zgodnie
  z odpowiedzią użytkownika na pytanie modelu
- 3 pozycje z prawdziwymi danymi: Laptop 6763,77 / Monitor 2704,77 / Klawiatura 552,27
- stopkę `Strona 1 z 1`

Na `FV/2026/08/003` ten sam układ trzyma się przy innych danych: `Data: 27.08.2026`,
`Kontrahent: Krakowska Grupa Handlowa`, 1 pozycja. Po to właśnie renderuje się próbkę, a nie
jeden rekord.

SQL kontrolny potwierdza liczbę pozycji na dokument: 3 / 1 / 1.

## Kryterium 5 — pełny dialog z dopytywaniem ✅

Podyktowałem niekompletnie, celowo pomijając encję i to, czy wartość ma być netto czy brutto:

> **Ja:** Zrób mi szablon faktury. W nagłówku ma być data i kontrahent, a niżej pozycje po kolei
> — opis, ilość, wartość.

Model najpierw rozpoznał teren (`describe_entity` na `Faktura` i `PozycjaFaktury`), sam wywnioskował,
że pozycje siedzą w `PozycjaFaktury`, zbudował nagłówek przez referencje
(`[Faktura.DataWystawienia]`, `[Faktura.Customer.NazwaKlienta]`) — i **zatrzymał się przed budową**,
bo nie wiedział, które pole to „wartość":

```
[Tool:validate_report_spec] Called with entity=PozycjaFaktury, fields=OpisPozycji,Ilosc,
    header=Data: [Faktura.DataWystawienia]|Kontrahent: [Faktura.Customer.NazwaKlienta]
```

Zwrócił jedno konkretne pytanie, nie listę i nie domysł:

> **Model:** Która wartość ma być kolumną „wartość": WartoscNetto, WartoscBrutto czy WartoscVat?

> **Ja:** Wartość brutto. Zbuduj ten raport i pokaż mi, jak wygląda — wyrenderuj trzy przykładowe faktury.

Dopiero wtedy zbudował i wyrenderował:

```
[Tool:build_report] Called with entity=PozycjaFaktury, fields=OpisPozycji,Ilosc,WartoscBrutto,
    title=Faktura, groupBy=Faktura.NumerFaktury, header=..., summary=WartoscBrutto
[Tool:build_report] Saved ReportDataV2 key=40f6429f-..., DataTypeName='...PozycjaFaktury', columns=3
[Tool:preview_report] Called with ... render=True, samples=3, key=Faktura.NumerFaktury
[Tool:preview_report] Returned 5 of 5 row(s), rendered=True
```

Model dopytał, nie zgadł. Kryterium spełnione.

## Znaleziony błąd — pasmo podsumowania NIE renderuje się ❌

Poprosiłem dodatkowo: „Dodaj pod tabelą sumę wartości brutto i wyrenderuj jeszcze raz fakturę
FV/2026/08/001." Model odpowiedział, że dodał podsumowanie, i pokazał w czacie
`Suma wartości brutto — 10 020,81 zł` (liczba prawidłowa, SQL: `sum(WartoscBrutto)` dla
FV/2026/08/001 = 10020,81).

**Ale na wyrenderowanym pliku `...-170811.png` żadnego wiersza sumy nie ma.** Suma pojawiła się
wyłącznie w tekście odpowiedzi modelu — policzył ją sam i opisał jako zrobioną.

Czyli: `ReportSpec.SummaryFields` + `ReportFooterBand` z wyrażeniem `sumSum([pole])` w moim
commicie `ff8470e` **nie działa**. Nie ustaliłem, czy przyczyną jest samo wyrażenie, czy to,
że model nie przekazał `summaryFields` do `preview_report` — moja linia logu dla `preview_report`
nie loguje tego parametru i to jest luka w instrumentacji, którą sam zostawiłem.

Osobno: to pokazuje, że **odpowiedź modelu nie jest dowodem na to, co powstało**. Gdybym oparł
werdykt na tekście z czatu zamiast obejrzeć plik, zaraportowałbym sukces, którego nie ma.

## Uwaga o równoległej pracy w tym repo

W trakcie testów pliki `Services/ReportSpecBuilder.cs` i `Services/AIChatDefaults.cs` zmieniły się
na dysku — nie moimi zmianami. Zmiana w builderze to przebudowa pasma podsumowania
(`BuildSummaryRow`, `GroupFooterBand` + `ReportFooterBand`, sumy wyrównane do kolumn), czyli
najwyraźniej poprawka dokładnie tego błędu, który opisałem wyżej. Zostawiłem te zmiany
nietknięte i **nie zacommitowałem ich jako swoich**. Mój build i wszystkie dowody powyżej
pochodzą sprzed tych edycji.
