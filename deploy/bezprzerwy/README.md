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

## Czego ten układ NIE rozwiązuje

Po `Deploy Schema` restartuje się **tylko ta replika**, z której wywołano wdrożenie.
Pozostałe dwie chodzą dalej ze starym modelem runtime, dopóki ich nie zrestartujesz.
Przy trzech replikach wdrożenie schematu wymaga więc restartu wszystkich trzech —
inaczej użytkownik trafiający na starą replikę nie zobaczy nowej encji.
