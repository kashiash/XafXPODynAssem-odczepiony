#!/usr/bin/env bash
# Uruchamia trzy repliki aplikacji — red, green, blue — za jednym nginxem.
#
# Wszystkie trzy chodza na TYM SAMYM obrazie i TEJ SAMEJ bazie. To jest wazne:
# aplikacja generuje klasy z metadanych w czasie dzialania, wiec repliki na roznych
# obrazach zbudowalyby z jednej bazy rozne modele. Jeden obraz — jeden model.
#
# Repliki startuja PO KOLEI, a nie naraz. Kazda przy starcie robi aktualizacje
# schematu (XAF_UPDATE_DB=1); trzy jednoczesne DDL na jednej bazie potrafia sie
# zderzyc. Pierwsza wykonuje robote, kolejne zastaja gotowe.
#
# Wszystkie trzy dziela WSPOLNY zestaw kluczy ochrony danych (wolumen
# mordeczka-keys). Bez tego ciasteczko logowania wystawione przez jedna replike
# jest nieczytelne dla pozostalych i po przelaczeniu uzytkownik laduje z powrotem
# na ekranie logowania. Sprawdzone: bez wspolnych kluczy strona wraca sama,
# ale jako niezalogowana.
#
#   ./trzy-repliki.sh <obraz> [baza]
#
set -uo pipefail

OBRAZ="${1:-xpodyn:wznawianie}"
BAZA="${2:-XafXPODynAssem}"
declare -A REPLIKI=( [red]=8101 [green]=8102 [blue]=8103 )
KOLEJNOSC=(red green blue)

log() { echo "[$(date +%H:%M:%S)] $*"; }

for k in "${KOLEJNOSC[@]}"; do
  PORT="${REPLIKI[$k]}"
  NAZWA="mordeczka-$k"
  log "--- $NAZWA na porcie $PORT ---"
  docker rm -f "$NAZWA" >/dev/null 2>&1 || true
  docker run -d --name "$NAZWA" --network host --restart on-failure \
    -v mordeczka-keys:/root/.aspnet/DataProtection-Keys \
    --label "aplikacja=mordeczka" --label "replika=$k" --label "port=$PORT" \
    --label "wdrozono=$(date -Is)" \
    -e "ASPNETCORE_URLS=http://+:$PORT" \
    -e 'ASPNETCORE_ENVIRONMENT=Development' \
    -e 'XAF_UPDATE_DB=1' \
    -e "ConnectionStrings__ConnectionString=XpoProvider=Postgres;Host=localhost;Port=5432;Database=$BAZA;Username=postgres;Password=" \
    -e 'AI__BaseUrl=https://polandcentral.api.cognitive.microsoft.com/openai' \
    -e 'AI__ApiKeys__openai=' \
    -e 'AI__DefaultProvider=openai' -e 'AI__Model=gpt-5.6-luna' \
    "$OBRAZ" >/dev/null || { log "BLAD: nie udalo sie uruchomic $NAZWA"; exit 1; }

  GOTOWA=0
  for i in $(seq 1 60); do
    KOD="$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:$PORT/" --max-time 4 || echo 000)"
    if [ "$KOD" = "200" ]; then GOTOWA=1; log "$NAZWA gotowa po $((i * 2))s"; break; fi
    sleep 2
  done
  [ "$GOTOWA" = "0" ] && { log "BLAD: $NAZWA nie wstala"; docker logs "$NAZWA" 2>&1 | tail -8; exit 1; }
done

log "wszystkie trzy repliki dzialaja"
docker ps --filter 'label=aplikacja=mordeczka' \
  --format 'table {{.Names}}\t{{.Status}}\t{{.Label "replika"}}\t{{.Label "port"}}'
