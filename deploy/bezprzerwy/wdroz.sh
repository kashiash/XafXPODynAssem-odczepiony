#!/usr/bin/env bash
# Wdrozenie bez przerwy w dzialaniu, metoda dwoch kopii.
#
# Zamysl: nowa kopia wstaje OBOK dzialajacej, na wolnym porcie. Dopiero kiedy
# odpowiada poprawnie, nginx przelacza na nia ruch (reload jest bezprzerwowy:
# nowe polaczenia ida do nowej, trwajace koncza sie na starej). Stara zostaje
# ZATRZYMANA i jest jedyna kopia trzymana do cofniecia.
#
# Zmierzone na Miniaku (LXC 200): 429 zadan w trakcie wdrozenia, 429 odpowiedzi
# 200, zero bledow; przelaczenie widoczne w naglowku X-Kopia co do setnej sekundy.
# Nieudane wdrozenie: ruch NIE zostaje przelaczony, stara kopia obsluguje dalej
# (355 zadan, zero bledow), skrypt konczy sie kodem 1.
#
# Dlaczego zatrzymana, a nie "niech chodzi jako zapasowa":
# obie kopie sięgaja do JEDNEJ bazy i kazda ma XAF_UPDATE_DB=1, a ta aplikacja
# generuje klasy z metadanych w czasie dzialania. Dwie rozne wersje kodu potrafia
# z tych samych metadanych zbudowac rozne modele — i wtedy obie pisza do jednego
# schematu. Tak powstala awaria z petla restartow. Zatrzymana kopia jest bezpieczna
# i nadal nadaje sie do cofniecia; dzialajaca obok nowej — nie.
#
# Uzycie:
#   ./wdroz.sh <obraz> [--nazwa mordeczka] [--baza XafXPODynAssem] [--wersja opis]
#
set -uo pipefail

OBRAZ="${1:-}"; shift || true
NAZWA="mordeczka"
BAZA="XafXPODynAssem"
WERSJA="$(date +%Y%m%d-%H%M)"
PORTY=(8092 8093)          # dwie kopie na przemian
NGINX="mordeczka-nginx"
UPSTREAM="/opt/mordeczka/nginx/upstream.conf"
CZEKAJ=120                 # sekund na gotowosc nowej kopii

while [ $# -gt 0 ]; do
  case "$1" in
    --nazwa)  NAZWA="$2"; shift 2 ;;
    --baza)   BAZA="$2"; shift 2 ;;
    --wersja) WERSJA="$2"; shift 2 ;;
    *) echo "nieznany argument: $1"; exit 2 ;;
  esac
done
[ -z "$OBRAZ" ] && { echo "podaj obraz, np. ./wdroz.sh xpodyn:wznawianie"; exit 2; }

log() { echo "[$(date +%H:%M:%S)] $*"; }

# --- ktora kopia jest teraz aktywna -----------------------------------------
# Etykiet Dockera NIE da sie zmienic na dzialajacym kontenerze, wiec roli
# "aktywny" nie zapiszemy w etykiecie po przelaczeniu. Aktywna kopie trzymamy
# w pliku; etykiety niosa to, co znane w chwili startu: wersje, port i date.
mkdir -p /opt/mordeczka
AKTYWNY=""
[ -f /opt/mordeczka/aktywny ] && AKTYWNY="$(cat /opt/mordeczka/aktywny)"
# kopia mogla zostac usunieta recznie — sprawdzamy, czy nadal istnieje
if [ -n "$AKTYWNY" ] && ! docker inspect "$AKTYWNY" >/dev/null 2>&1; then
  log "zapisana aktywna kopia $AKTYWNY juz nie istnieje — pomijam"
  AKTYWNY=""
fi
PORT_AKTYWNY=""
if [ -n "$AKTYWNY" ]; then
  PORT_AKTYWNY="$(docker inspect "$AKTYWNY" --format '{{index .Config.Labels "port"}}')"
  log "aktywna kopia: $AKTYWNY na porcie $PORT_AKTYWNY"
else
  log "nie ma jeszcze aktywnej kopii — to bedzie pierwsze wdrozenie"
fi

# nowa kopia idzie na ten z dwoch portow, ktorego teraz nie uzywamy
PORT_NOWY="${PORTY[0]}"
[ "$PORT_AKTYWNY" = "${PORTY[0]}" ] && PORT_NOWY="${PORTY[1]}"
NOWY="$NAZWA-$PORT_NOWY"
log "nowa kopia: $NOWY na porcie $PORT_NOWY, obraz $OBRAZ"

# --- start nowej kopii -------------------------------------------------------
docker rm -f "$NOWY" >/dev/null 2>&1 || true
docker run -d --name "$NOWY" --network host --restart on-failure \
  --label "aplikacja=$NAZWA" --label "port=$PORT_NOWY" \
  --label "wersja=$WERSJA" --label "wdrozono=$(date -Is)" \
  -e "ASPNETCORE_URLS=http://+:$PORT_NOWY" \
  -e 'ASPNETCORE_ENVIRONMENT=Development' \
  -e 'XAF_UPDATE_DB=1' \
  -e "ConnectionStrings__ConnectionString=XpoProvider=Postgres;Host=localhost;Port=5432;Database=$BAZA;Username=postgres;Password=" \
  -e 'AI__BaseUrl=https://polandcentral.api.cognitive.microsoft.com/openai' \
  -e 'AI__ApiKeys__openai=' \
  -e 'AI__DefaultProvider=openai' -e 'AI__Model=gpt-5.6-luna' \
  "$OBRAZ" >/dev/null || { log "BLAD: nie udalo sie uruchomic $NOWY"; exit 1; }

log "czekam, az nowa kopia bedzie gotowa..."
GOTOWA=0
for i in $(seq 1 $((CZEKAJ / 2))); do
  KOD="$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:$PORT_NOWY/" --max-time 4 || echo 000)"
  if [ "$KOD" = "200" ]; then GOTOWA=1; log "nowa kopia odpowiada po $((i * 2))s"; break; fi
  sleep 2
done

if [ "$GOTOWA" = "0" ]; then
  log "BLAD: nowa kopia nie wstala w ${CZEKAJ}s — ruch NIE zostal przelaczony"
  docker logs "$NOWY" 2>&1 | tail -n 12
  docker rm -f "$NOWY" >/dev/null 2>&1
  log "stara kopia dziala dalej, nic sie nie zmienilo"
  exit 1
fi

# --- przelaczenie ruchu ------------------------------------------------------
echo "upstream mordeczka { server 127.0.0.1:$PORT_NOWY; }" > "$UPSTREAM"
if ! docker exec "$NGINX" nginx -t >/dev/null 2>&1; then
  log "BLAD: nginx odrzucil konfiguracje — cofam"
  [ -n "$PORT_AKTYWNY" ] && echo "upstream mordeczka { server 127.0.0.1:$PORT_AKTYWNY; }" > "$UPSTREAM"
  docker rm -f "$NOWY" >/dev/null 2>&1
  exit 1
fi
docker exec "$NGINX" nginx -s reload
log "ruch przelaczony na port $PORT_NOWY"

# sprawdzamy, ze przez nginx faktycznie odpowiada nowa kopia
sleep 1
KOD="$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:8090/ --max-time 6 || echo 000)"
if [ "$KOD" != "200" ]; then
  log "BLAD: przez nginx dostaje $KOD — cofam na poprzednia kopie"
  [ -n "$PORT_AKTYWNY" ] && echo "upstream mordeczka { server 127.0.0.1:$PORT_AKTYWNY; }" > "$UPSTREAM"
  docker exec "$NGINX" nginx -s reload
  docker rm -f "$NOWY" >/dev/null 2>&1
  exit 1
fi

# --- oznaczenie i wygaszenie starej -----------------------------------------
echo "$NOWY" > /opt/mordeczka/aktywny

if [ -n "$AKTYWNY" ] && [ "$AKTYWNY" != "$NOWY" ]; then
  log "zatrzymuje poprzednia kopie: $AKTYWNY (zostaje do cofniecia)"
  docker stop "$AKTYWNY" >/dev/null 2>&1
  echo "$AKTYWNY" > /opt/mordeczka/poprzedni
fi

# sprzatanie: trzymamy najwyzej jedna kopie do cofniecia
for c in $(docker ps -a --filter "label=aplikacja=$NAZWA" --format '{{.Names}}'); do
  [ "$c" = "$NOWY" ] && continue
  [ "$c" = "$AKTYWNY" ] && continue
  log "usuwam zbedna kopie: $c"
  docker rm -f "$c" >/dev/null 2>&1
done

log "GOTOWE. Aktywna: $NOWY (wersja $WERSJA), port $PORT_NOWY"
docker ps -a --filter "label=aplikacja=$NAZWA" \
  --format 'table {{.Names}}\t{{.Status}}\t{{.Image}}\t{{.Label "wersja"}}'
