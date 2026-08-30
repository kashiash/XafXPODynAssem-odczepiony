# Weryfikacja po scaleniu do master + build_invoice_report

Build z `master` (po `2037ac8`), lokalnie, porty 5030/5031, model `gpt-5.6-luna`.

## Zadanie 1 — pasmo sum po scaleniu ✅ DZIAŁA

Wcześniej: suma nie renderowała się, a model twierdził, że jest. Przyczyny nie dało się
ustalić, bo `preview_report` nie logował `summaryFields`.

**Instrumentacja dopisana** (`53e6bd4`) — teraz widać parametr w obu liniach:

```
[Tool:preview_report] Called with entity=PozycjaFaktury, fields=OpisPozycji,Ilosc,WartoscBrutto,
    ..., header=Faktura nr [Faktura.NumerFaktury]|..., summary=WartoscBrutto
[Tool:preview_report] Returned 5 of 5 row(s), rendered=True, summaryFields=WartoscBrutto
```

Czyli `summaryFields` **było** przekazywane — poprzednia awaria leżała po stronie
budowania pasma, nie po stronie modelu. Scalona wersja drugiego agenta to naprawiła.

**Obejrzany plik** `po-scaleniu-pasmo-sum.png`: pod tabelą wiersz `Razem:` z kwotą
`10 020,81` wyrównaną pod kolumną `WartoscBrutto`.

SQL kontrolny:

```
 NumerFaktury   | pozycji |  suma_brutto
----------------+---------+---------------
 FV/2026/08/001 |       3 | 10020.81000000
```

Pozycje: 6763,77 + 2704,77 + 552,27 = **10 020,81**. Zgadza się co do grosza.

**Bez podwójnego liczenia** — gdyby sumy liczyły się dwa razy, wyszłoby 20 041,62.
Mechanizm jest jeden, źródłem prawdy jest `spec.SummaryFields`; `GroupFooterBand`
powstaje tylko przy grupowaniu, `ReportFooterBand` zawsze.

Uwaga do kodu (nie blokuje): w `BuildSummaryRow` komórka dostaje **jednocześnie**
`cell.Summary = new XRSummary{Func=Sum}` i `ExpressionBinding("BeforePrint","Text", sumSum([...]))`.
Dwa mechanizmy piszą do tego samego `Text`. Wynik jest poprawny, ale jeden z nich
jest zbędny i przy zmianach może zacząć przeszkadzać.

## Zadanie 2 — ścieżki kropkowane w TemplateField.Value ✅ DZIAŁAJĄ

Największe ryzyko planu szablonów. Test na typach generowanych Roslynem,
`Pozycja → Faktura → Klient`, czyli **dwa poziomy referencji**:

| Slot | Ścieżka | Wynik na dokumencie |
|---|---|---|
| `CustomerName` | `Faktura.Klient.Nazwa` | Slaskie Systemy IT Sp. z o.o. |
| `CustomerCity` | `Faktura.Klient.Miasto` | Katowice |
| `InvoiceNumber` | `Faktura.Numer` | FV/2026/08/001 |
| `InvoiceDate` | `Faktura.Data` | 12 sierpnia, 2026 |

Dowód: `analysis/dx-sciezki-kropkowane.png`, `TOTAL 9897,00 zł` = 5499 + 4398.

**Spłaszczanie do projekcji nie jest potrzebne.** `build_invoice_report` może brać
kolekcję prosto z `IObjectSpace.GetObjects` i wiązać przez referencje.

## build_invoice_report — dopisane i zweryfikowane ✅

Narzędzia 13 → **14**.

```
[AskAsync] Sending (model=gpt-5.6-luna, provider=OpenAi, tools=14, history=0)
[Tool:build_invoice_report] Called with entity=PozycjaFaktury, template=Invoice1,
    mapping=InvoiceNumber=Faktura.NumerFaktury;ProductName=OpisPozycji;Quantity=Ilosc;UnitPrice=CenaJednostkowa,
    literals=VendorName=Brekhof Sp. z o.o.;VendorCity=Katowice,
    filter=Faktura.NumerFaktury = 'FV/2026/08/001', render=True, key=Faktura.NumerFaktury, samples=1
[Tool:build_invoice_report] Saved ReportDataV2 key=599bff2c-...,
    DataTypeName='XafXPODynAssem.RuntimeEntities.PozycjaFaktury'
[Tool:build_invoice_report] Done — 6 slot(s), 1 document(s), 3 row(s)
```

**Obejrzany plik** `build-invoice-report.png`: układ DevExpressa z polem na logo,
sprzedawca z literałów („Brekhof Sp. z o.o.", „Katowice"), `INVOICE #FV/2026/08/001`
(przez referencję), trzy pozycje z cenami, `TOTAL 8147,00 zl`.

SQL: `SumaNetto` dla FV/2026/08/001 = 8147,00. Zgadza się (5499 + 2199 + 449).

`DataTypeName` niepuste, więc raport da się otworzyć w projektancie.

### Walidacja typów slotów ✅

Prompt: „jako ilość użyj pola OpisPozycji" (String w slocie liczbowym).

```
[Tool:build_invoice_report] Refused — 1 problem(s), 0 missing
```

Odpowiedź w czacie: „Nie mogę użyć OpisPozycji jako ilości — to pole ma typ System.String,
a szablon faktury DevExpress wymaga, aby Quantity było polem liczbowym." Plus dwie sensowne
propozycje wyjścia. **Odrzucone, nie wyrenderowane jako śmieć.**

### Routing — poprawka po pierwszym nieudanym teście

Pierwsze podejście: model poszedł w `validate_report_spec` zamiast w narzędzie fakturowe,
bo sekcja „## Reports" promptu systemowego o nim nie wiedziała. Dopisana reguła kierująca
faktury do `build_invoice_report`. Po poprawce model trafia za pierwszym razem.

## Znane usterki (nienaprawione)

1. **Kolumna QTY łamie się na dwie linie** — `Ilosc` to `numeric` o dużej precyzji, więc
   renderuje się jako „1,000 00000". Trzeba dołożyć formatowanie (`FormatString` na slocie
   albo zaokrąglenie w projekcji). Widać na `build-invoice-report.png`.
2. **BILL TO oraz INVOICE DATE puste** w teście — model nie zmapował `CustomerName`
   ani `InvoiceDate`, bo mój prompt o nich nie wspomniał. To zachowanie modelu, nie błąd
   narzędzia, ale warto rozważyć oznaczenie tych slotów jako zalecanych.
3. **Podwójny mechanizm sum** w `BuildSummaryRow` (opisany wyżej) — działa, ale jest zbędny.

## Czego nie zweryfikowano

- Otwarcia raportu z `build_invoice_report` **w projektancie XAF** i podglądu przez XAF —
  sprawdziłem tylko, że wiersz w `ReportDataV2` ma niepuste `DataTypeName`.
- Szablonów `Invoice2` .. `Invoice9` — testowany wyłącznie `Invoice1`.
- Slotów podatkowych i rabatowych (`Tax`, `Discount`, `TaxTotal`) pod kątem polskiego VAT-u.
