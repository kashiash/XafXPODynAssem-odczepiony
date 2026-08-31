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
