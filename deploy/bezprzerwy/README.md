# Wdrożenie bez przerwy w działaniu

Aktualizacja bez wyłączania aplikacji: nowa kopia wstaje obok działającej, na wolnym
porcie. Dopiero gdy odpowiada poprawnie, nginx przełącza na nią ruch. Stara kopia
zostaje zatrzymana i służy do ewentualnego cofnięcia.

## Pliki

| Plik | Rola |
|---|---|
| `nginx.conf` | przełącznik ruchu; nasłuchuje na 8090, dokłada nagłówek `X-Kopia` |
| `upstream.conf` | jedna linia z adresem aktywnej kopii — to ona się podmienia |
| `wdroz.sh` | całe wdrożenie: start, kontrola gotowości, przełączenie, wygaszenie starej |

## Uruchomienie przełącznika (raz)

```bash
docker run -d --name mordeczka-nginx --network host --restart unless-stopped \
  -v /opt/mordeczka/nginx/nginx.conf:/etc/nginx/nginx.conf:ro \
  -v /opt/mordeczka/nginx/upstream.conf:/etc/nginx/upstream.conf:ro \
  nginx:1.27-alpine
```

## Wdrożenie

```bash
/opt/mordeczka/wdroz.sh xpodyn:wznawianie --baza XafXPODynAssem --wersja 2026-08-31
```

Kod wyjścia 0 oznacza, że ruch jest już na nowej kopii. Kod 1 oznacza, że nowa kopia
nie wstała, ruch **nie** został przełączony i dalej obsługuje go stara.

## Co zostało zmierzone

Na Miniaku, LXC 200, baza testowa `XafXPODynAssem_bz`:

- **Udane wdrożenie:** 429 żądań w trakcie, 429 odpowiedzi 200, **zero błędów**.
  Przełączenie widoczne w nagłówku `X-Kopia`: `8092 → 8093` o 08:35:58.55.
- **Nieudane wdrożenie** (celowo zepsuty obraz): ruch nie został przełączony,
  stara kopia obsłużyła 355 żądań bez błędu, skrypt zakończył się kodem 1.

## Dlaczego stara kopia jest zatrzymywana, a nie zostawiana jako zapasowa

Obie kopie sięgają do **jednej bazy** i każda ma `XAF_UPDATE_DB=1`, a ta aplikacja
generuje klasy z metadanych w czasie działania. Dwie różne wersje kodu potrafią
z tych samych metadanych zbudować różne modele — wtedy obie piszą do jednego schematu.
Tak powstała awaria z pętlą restartów. Zatrzymana kopia jest bezpieczna i nadal nadaje
się do cofnięcia; działająca obok nowej — nie.

## Czego to nie rozwiązuje

Przerwy przy **Deploy Schema** wywołanym z aplikacji. Tam użytkownik sam zmienia
schemat i restart jest z definicji. To jest rozwiązane osobno: strona wraca sama,
bez odświeżania (`_Host.cshtml`, commit `75f9bab`).

---

# Trzy repliki za rozdzielaczem (red / green / blue)

`trzy-repliki.sh` uruchamia trzy kopie aplikacji na portach 8101–8103, a nginx
(`nginx-lb.conf` + `upstream-lb.conf`) rozdziela między nie ruch na porcie 8090.

```bash
docker volume create mordeczka-keys
/opt/mordeczka/trzy-repliki.sh xpodyn:wznawianie XafXPODynAssem
```

## Trzy rzeczy, bez których to nie działa

**1. `ip_hash` w upstreamie.** Blazor wiąże obwód SignalR z konkretnym procesem.
Zwykłe round-robin wysłałoby negocjację na jedną replikę, a WebSocket na drugą —
strona wisiałaby na banerze ponownego łączenia.

**2. `proxy_set_header Host $http_host`, a nie `$host`.** `$host` gubi numer portu,
więc aplikacja budowała przekierowanie na `http://localhost/LoginPage` bez portu
i przeglądarka dostawała `ERR_CONNECTION_REFUSED`. Na porcie 80/443 tego nie widać —
wychodzi dopiero przy niestandardowym porcie.

**3. Wspólny wolumen kluczy ochrony danych** (`mordeczka-keys` na
`/root/.aspnet/DataProtection-Keys`). Bez niego ciasteczko logowania wystawione
przez jedną replikę jest nieczytelne dla pozostałych i po przełączeniu użytkownik
ląduje na ekranie logowania. Zmierzone: bez wspólnych kluczy strona wracała sama,
ale **wylogowana**; ze wspólnymi — wraca zalogowana.

## Co zostało zmierzone

- **Ruch HTTP przy ubitej replice:** 210 żądań, 210 odpowiedzi 200, **zero błędów**.
  W nagłówku `X-Kopia` widać `8103, 8102` — nginx próbował martwej repliki i przekazał
  żądanie żywej w obrębie tego samego żądania.
- **Żywa sesja Blazora przy ubitej replice:** baner ponownego łączenia pokazał się,
  strona wróciła **sama po 6,0 s**, nadal zalogowana, na innej replice.

## Deploy Schema przy trzech replikach

Było tak: po `Deploy Schema` restartowała się **tylko ta replika**, z której wywołano
wdrożenie. Pozostałe dwie chodziły dalej ze starym modelem, więc użytkownik, którego
rozdzielacz skierował na starą replikę, nie widział nowej encji. Rozdzielacz kieruje
po adresie klienta, więc kto raz trafił na starą, ten na niej zostawał.

Teraz każda replika sama pilnuje, czy jej model jest aktualny (`ReplicaSyncService`
w `Blazor.Server/Services`). Co kilkanaście sekund liczy **odcisk metadanych** — skrót
ze wszystkich pól wszystkich klas runtime. Gdy odcisk się zmieni, replika wie, że inna
wdrożyła schemat, i restartuje się tak samo jak przy własnym wdrożeniu (kod 42).

Liczenie wierszy tu nie wystarcza: zmiana w miejscu (inny typ, inna nazwa, inna
widoczność) nie zmienia ich liczby. Dlatego odcisk, a nie licznik.

### Trzy warunki, bez których to by szkodziło

1. **Nie naraz.** Gdyby wszystkie trzy zobaczyły zmianę w tej samej sekundzie, zgasłyby
   jednocześnie — czyli dokładnie ta przerwa, dla której trzymamy trzy repliki.
   Każda czeka swoją kolej: `REPLIKA_INDEKS` razy `REPLIKA_ODSTEP`.
2. **Tylko przy żywym sąsiedzie.** Tuż przed restartem replika sprawdza, czy któraś
   z pozostałych odpowiada. Jeśli nie — odkłada restart, zamiast dołożyć się do przerwy.
3. **Tylko przy metadanych, które się kompilują.** Zepsute metadane po restarcie będą
   tak samo zepsute; restart zamieniłby jedną złą replikę w pętlę restartów. Walidacja
   idzie przez `ValidateCompilation`, czyli tę samą ścieżkę, co wdrożenie z interfejsu.

### Zmienne środowiska

| Zmienna | Rola |
|---|---|
| `REPLIKA_INDEKS` | numer w kolejce restartów, od 0. **Bez niej mechanizm śpi** — pojedyncza instancja działa jak dotąd |
| `REPLIKA_PEERS` | adresy wszystkich replik po przecinku; własny adres rozpoznawany po porcie z `ASPNETCORE_URLS` |
| `REPLIKA_ODSTEP` | sekundy między kolejnymi replikami (domyślnie 90) |
| `REPLIKA_SONDA` | co ile sekund liczymy odcisk (domyślnie 15) |
| `REPLIKA_ROZBIEG` | zwłoka po starcie, zanim zaczniemy pilnować (domyślnie 60) |

`trzy-repliki.sh` ustawia je sam.

### Co zostało zmierzone

Miniak, LXC 200, baza `XafXPODynAssem`, `ODSTEP=45`. Nowa encja wstawiona do metadanych
o 09:54:12. Repliki wstały z nowym modelem:

| replika | numer w kolejce | wstała | od zmiany |
|---|---|---|---|
| red | 0 | 09:54:15 | 3 s |
| green | 1 | 09:55:06 | 54 s |
| blue | 2 | 09:55:56 | 104 s |

Odcisk we wszystkich trzech logach przeszedł `E3B0C442 → BD14F9FF`. Tabela nowej encji
powstała w bazie. **Ruch w trakcie całej kaskady: 986 żądań, 986 odpowiedzi 200,
zero błędów.**

## Czego to nadal NIE rozwiązuje

**Wymiany obrazu przy trzech replikach.** `wdroz.sh` zna tylko układ dwóch kopii na
przemian; uruchomiony na trójce nadpisałby listę replik jednym wpisem i skasował
wszystkie trzy kontenery. Od teraz **odmawia** startu, gdy w liście replik jest więcej
niż jeden serwer.

Rolowanie replik po jednej przy nowym obrazie **nie jest bezpieczne**: nowy obraz może
mieć inny generator, więc z tych samych metadanych zbuduje inny model — a wtedy dwie
różne wersje piszą do jednego schematu. Właściwe rozwiązanie to wymiana całej trójki:
nowa trójka wstaje obok na portach 8111–8113, jedno przeładowanie nginxa przepina
cały upstream, dopiero potem stara trójka gaśnie. Zaprojektowane, jeszcze nie napisane.
