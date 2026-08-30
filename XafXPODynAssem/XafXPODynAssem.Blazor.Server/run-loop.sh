#!/bin/bash
# Deploy Schema konczy proces kodem 42 (przeladowanie dynamicznych assembly).
#
# WAZNE: uruchamiamy DLL bezposrednio, NIE przez "dotnet run".
# "dotnet run" jest procesem-posrednikiem: przy Environment.Exit(42) nie oddaje
# czystego kodu wyjscia i potrafi zawisnac przy sprzataniu, przez co petla
# albo konczy prace, albo czeka w nieskonczonosc — a aplikacja jest juz martwa.
cd "$(dirname "$0")/bin/Debug/net8.0" || exit 1
export ASPNETCORE_URLS="https://localhost:5001;http://localhost:5000"
export ASPNETCORE_ENVIRONMENT=Development
export AI__DefaultProvider=openai
export AI__Model=gpt-5.6-luna
export AI__BaseUrl="https://polandcentral.api.cognitive.microsoft.com/openai"
if [ -z "$AI__ApiKeys__openai" ] && command -v az >/dev/null 2>&1; then
  export AI__ApiKeys__openai="$(az cognitiveservices account keys list \
    -n dbchatai-openai-pl -g openai-poland-rg --query key1 -o tsv 2>/dev/null)"
fi
fails=0
while true; do
  echo "[LOOP] start $(date '+%H:%M:%S')"
  dotnet XafXPODynAssem.Blazor.Server.dll
  code=$?
  echo "[LOOP] zakonczony kodem $code"
  if [ "$code" = "42" ]; then
    fails=0
    echo "[LOOP] restart po deploy schematu"
    sleep 2
    continue
  fi
  # kod inny niz 42 = awaria; chron przed petla crashowa
  fails=$((fails+1))
  if [ "$fails" -ge 3 ]; then
    echo "[LOOP] 3 awarie z rzedu (ostatni kod $code) — zatrzymuje petle"
    break
  fi
  echo "[LOOP] awaria $fails/3, ponawiam za 5 s"
  sleep 5
done
