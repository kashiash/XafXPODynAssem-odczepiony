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
# Kazda replika dostaje REPLIKA_INDEKS i liste sasiadow. Po Deploy Schema wykonanym
# na jednej replice pozostale rozpoznaja zmiane metadanych po odcisku i restartuja sie
# same — po kolei, z odstepem ODSTEP sekund, i tylko gdy inna replika odpowiada.
# Bez tego uzytkownik trafiajacy na stara replike nie zobaczylby nowej encji.
#
#   ./trzy-repliki.sh <obraz> [baza]
#
set -uo pipefail

OBRAZ="${1:-xpodyn:wznawianie}"
BAZA="${2:-XafXPODynAssem}"
declare -A REPLIKI=( [red]=8101 [green]=8102 [blue]=8103 )
declare -A INDEKS=( [red]=0 [green]=1 [blue]=2 )
KOLEJNOSC=(red green blue)
PEERS="http://127.0.0.1:8101,http://127.0.0.1:8102,http://127.0.0.1:8103"
ODSTEP="${ODSTEP:-90}"        # sekundy miedzy restartami kolejnych replik
CIERPLIWOSC="${CIERPLIWOSC:-300}"  # ile sekund czekamy, az pracujacy skoncza

log() { echo "[$(date +%H:%M:%S)] $*"; }

# --- sekrety -----------------------------------------------------------------
# Haslo do bazy i klucz do uslugi AI biora sie ze SRODOWISKA, nie z tego pliku.
# To repozytorium jest publiczne; wpisany tu klucz bylby kluczem oglos zonym swiatu
# w chwili wypchniecia. Ustaw je przed uruchomieniem, np. z pliku poza repozytorium:
#
#   set -a; . /opt/mordeczka/sekrety.env; set +a
#   ./trzy-repliki.sh ...
#
HASLO_BAZY="${MORDECZKA_HASLO_BAZY:-}"
KLUCZ_AI="${MORDECZKA_KLUCZ_AI:-}"
[ -z "$HASLO_BAZY" ] && { echo "BRAK: ustaw MORDECZKA_HASLO_BAZY"; exit 2; }
[ -z "$KLUCZ_AI" ]   && { echo "BRAK: ustaw MORDECZKA_KLUCZ_AI"; exit 2; }

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
    -e "REPLIKA=$k" \
    -e "REPLIKA_INDEKS=${INDEKS[$k]}" \
    -e "REPLIKA_PEERS=$PEERS" \
    -e "REPLIKA_ODSTEP=$ODSTEP" \
    -e "REPLIKA_CIERPLIWOSC=$CIERPLIWOSC" \
    -e "ConnectionStrings__ConnectionString=XpoProvider=Postgres;Host=localhost;Port=5432;Database=$BAZA;Username=postgres;Password=$HASLO_BAZY" \
    -e 'AI__BaseUrl=https://polandcentral.api.cognitive.microsoft.com/openai' \
    -e "AI__ApiKeys__openai=$KLUCZ_AI" \
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
