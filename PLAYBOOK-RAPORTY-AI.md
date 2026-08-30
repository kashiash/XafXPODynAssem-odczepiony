# Raporty z czatu AI — instrukcja obsługi

Stan na 30.08.2026. Opisuje, jak w XafXPODynAssem poprosić asystenta AI o raport na encji utworzonej w czasie działania aplikacji.

Legenda: **[Z]** sprawdzone osobiście · **[R]** z cudzego raportu, nie potwierdzone · **[?]** założenie.

---

## 1. Do czego to służy

Asystent AI w aplikacji potrafi dwie rzeczy naraz: założyć encję biznesową bez pisania kodu i zbudować na niej raport XtraReports. Raport ląduje na liście **Raporty**, skąd można go obejrzeć, wydrukować albo otworzyć w projektancie. Rozpiska ma grupowanie, sortowanie, sumy w grupach i sumę końcową.

Model ma do dyspozycji 13 narzędzi [Z]. Trzy dotyczą raportów: `validate_report_spec` sprawdza, czy encja i pola istnieją, `build_report` buduje układ i zapisuje go do `ReportDataV2`, `list_reports` wypisuje gotowe raporty.

---

## 2. Gdzie to stoi

| Element | Wartość |
|---|---|
| Maszyna | Miniak, `192.168.88.25`, LXC 200 `docker` (`10.10.10.10`) |
| Kontener | `xpodyn-rap`, obraz `xpodyn-dbg:latest` |
| Port | `8081` **wewnątrz LXC** — z LAN-u jeszcze niewidoczny, patrz punkt 3 |
| Baza | PostgreSQL 18.6 w kontenerze `postgres`, baza **`XafXPODynAssem_rap`** |
| Pliki wdrożenia | `/opt/xpo-dbg` (aplikacja + Dockerfile), `/opt/xpo-rap/app.env` (konfiguracja, prawa 600) |
| Model | Azure OpenAI, `gpt-5.6-luna`, klucz w `app.env` — nigdy w repozytorium |

Stary kontener `xpodyn` (port 8080, baza `XafXPODynAssem`) należy do innej pracy i **nie jest tym wdrożeniem** [Z]. Jego baza jest pusta, więc nie da się tam zalogować.

---

## 3. Jak wejść

Port 8081 nie ma jeszcze przekierowania z LAN-u, więc najprościej tunelem:

```bash
ssh -i ~/.ssh/mac16 -N -L 18081:10.10.10.10:8081 root@192.168.88.25
```

Potem otwórz `http://localhost:18081`, zaloguj się jako **Admin** z pustym hasłem [Z].

Żeby wejść wprost pod `http://192.168.88.25:8081`, dopisz do `/usr/local/sbin/portforward.sh` trzy reguły wzorowane na tych dla Portainera (9443) i uruchom skrypt ponownie:

```bash
iptables -t nat -A PREROUTING -p tcp --dport 8081 -j DNAT --to-destination 10.10.10.10:8081
iptables -t nat -A POSTROUTING -p tcp -d 10.10.10.10 --dport 8081 -j SNAT --to-source 10.10.10.1
iptables -I FORWARD -p tcp -d 10.10.10.10 --dport 8081 -j ACCEPT
```

Tego kroku **nie wykonałem** — zmiana zapory została zablokowana po mojej stronie [Z].

---

## 4. Jak zamówić raport

Kolejność ma znaczenie. Raport można zbudować tylko na encji **wdrożonej**, nie samej zapisanej w metadanych.

**Krok 1. Załóż encję.** W czacie napisz, czego chcesz, na przykład:

> Utwórz encję SprzedazMiesieczna z polami: Miesiac (System.DateTime), Kontrahent (System.String), WartoscNetto (System.Decimal), LiczbaFaktur (System.Int32).

**Krok 2. Dopowiedz, żeby model naprawdę wywołał narzędzie.** Sam prompt zwykle kończy się zapowiedzią i pytaniem o zgodę — w logu widać wtedy `0 tool iterations` [Z]. Wystarczy druga wiadomość:

> Tak, potwierdzam. Wywołaj teraz narzędzie create_entity.

**Krok 3. Zrób Deploy.** Wejdź w **Zarządzanie schematem → Klasa użytkownika**, kliknij **Deploy Schema**, a potem **potwierdź w oknie dialogowym**. Bez potwierdzenia akcja nic nie robi i w logach nie ma po niej śladu [Z]. Aplikacja kompiluje nowe typy i kończy proces kodem 42, po czym Docker ją podnosi — przerwa trwa kilkanaście sekund.

**Krok 4. Poproś o raport.** Do tego służy kafelek **„Przygotuj raport — rozpiska z grupowaniem i sumami"**. Możesz też napisać własnymi słowami:

> Zbuduj raport na encji SprzedazMiesieczna: tytuł „Sprzedaż miesięczna”, kolumny Miesiac, Kontrahent, WartoscNetto, LiczbaFaktur, grupowanie po Kontrahent, sortowanie po Miesiac, sumowanie w grupach po WartoscNetto i LiczbaFaktur, A4 poziomo.

I znów, jeśli model tylko streści zamówienie, dopowiedz: „Tak, potwierdzam. Wywołaj teraz narzędzie build_report".

---

## 5. Jak sprawdzić, że wyszło

W aplikacji: **Raporty → Raporty**, tam pozycja o nadanym tytule.

W bazie — jednym zapytaniem [Z]:

```sql
select "Name", "ObjectTypeName", length("Content")
from "ReportDataV2" where "GCRecord" is null;
```

Wynik z mojego przebiegu:

```
Sprzedaz miesieczna | XafXPODynAssem.RuntimeEntities.SprzedazMiesieczna | 8416
```

Żeby potwierdzić, że w układzie faktycznie są sumy, wystarczy przeszukać zapisany XML [Z] — powinny się znaleźć `GroupHeader`, `GroupFooter`, `sumSum([WartoscNetto])`, `sumSum([LiczbaFaktur])` oraz etykiety „Razem w grupie:" i „Razem:".

---

## 6. Co gdzie leży w kodzie

| Plik | Rola |
|---|---|
| `Services/SchemaAIToolsProvider.cs` | rejestracja 13 narzędzi i implementacja `build_report`, `validate_report_spec` |
| `Services/ReportSpecBuilder.cs` | zamiana specyfikacji na `XtraReport` — pasma, kolumny, grupy, sumy |
| `BusinessObjects/ReportSpec.cs` | specyfikacja raportu jako obiekt biznesowy, w tym `SummaryFields` |
| `Services/AIChatDefaults.cs` | kafelki podpowiedzi nad czatem |
| `Services/ReportDesignerSink.cs` | most do projektanta — **napisany, ale nigdzie nie podpięty** |

---

## 7. Pułapki

**Obraz musi być zbudowany w konfiguracji Debug.** W Release aplikacja nie zakłada danych startowych: po `--updateDatabase --forceUpdate --silent` schemat powstaje, ale tabele zostają puste i nie ma użytkownika Admin [Z]. Dopiero `#if DEBUG` ustawia `DatabaseUpdateMode = UpdateDatabaseAlways` i seed przechodzi.

**Bez `ASPNETCORE_ENVIRONMENT=Development` interfejs się nie renderuje.** Serwer odpowiada 200, a przeglądarka dostaje pustą stronę i błędy o `dx-blazor-all.js`. Sprawdzaj `/_framework/blazor.server.js`, nie stronę główną.

**Obraz potrzebuje bibliotek Skia.** Bez `libfontconfig1` i `libfreetype6` każde żądanie kończy się błędem 500 [R] — warstwa jest już w `Dockerfile` w `/opt/xpo-dbg`.

**Deploy wymaga polityki restartu.** Aplikacja kończy się kodem 42, więc kontener musi mieć `--restart on-failure:5`, inaczej po wdrożeniu zostaje wyłączony.

**Model bywa zachowawczy.** Zapowiada zamiast działać. Druga wiadomość z nazwą narzędzia rozwiązuje to za każdym razem [Z].

**Powtórzona prośba tworzy drugi raport.** W moim przebiegu w `ReportDataV2` powstały dwa identyczne wiersze, bo prosiłem dwa razy [Z]. Nadmiarowy trzeba skasować ręcznie.

**Playwright wyłącznie bezpośrednio przez node.** Przez MCP przeglądarka jest współdzielona z innymi agentami i karty przełączają się w trakcie testu — wpisany tekst trafia wtedy do cudzej aplikacji [Z].

---

## 8. Czego nie zrobiono

- **Raport nie ma danych.** Tabela `SprzedazMiesieczna` jest pusta, więc podgląd pokaże nagłówki i zero wierszy. Sumy policzą się dopiero po wprowadzeniu rekordów.
- **Projektant nie otwiera się sam po zbudowaniu raportu.** `ReportDesignerSink` czeka na kontroler `AIChatReportDesignerController`, którego nie ma. Do tego trzeba pogodzić cykle życia: dostawca narzędzi jest pojedynczą instancją na aplikację, a sink ma być jeden na obwód Blazor.
- **Brak przekierowania portu 8081** — patrz punkt 3.
- **Nie sprawdziłem raportu w projektancie ani na wydruku.** Potwierdzone są: zapis do bazy, poprawny typ danych i obecność pasm z sumami w XML.
- **Kod nie jest zacommitowany.** Zmiany leżą w drzewie roboczym.
