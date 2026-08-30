# Wdrozenie XafXPODynAssem na LXC 200 (docker) — komendy

Srodowisko potwierdzone: Docker 29.7.2, Compose v5.5.0, x86_64, 4 vCPU, 4 GB RAM,
16 GB wolnego. PostgreSQL 18.6 dziala w kontenerze `postgres`.

## 1. Przeslanie plikow (do wykonania recznie — transfer jest blokowany dla agenta)

Na Macu gotowe jest `/tmp/xpo-publish.tgz` (223 MB, zawiera 103 polskie satelity DX).

    scp -i ~/.ssh/mac16 /tmp/xpo-publish.tgz root@192.168.88.25:/tmp/
    scp -i ~/.ssh/mac16 -r <ten katalog deploy> root@192.168.88.25:/tmp/xpo-deploy
    ssh -i ~/.ssh/mac16 root@192.168.88.25 \
      'pct push 200 /tmp/xpo-publish.tgz /tmp/xpo-publish.tgz; \
       tar cf - -C /tmp/xpo-deploy . | pct exec 200 -- tar xf - -C /opt/xpodyn'

## 2. Rozpakowanie i baza (w LXC 200)

    pct exec 200 -- bash -lc '
      mkdir -p /opt/xpodyn/publish
      tar xzf /tmp/xpo-publish.tgz -C /opt/xpodyn/publish
      docker exec postgres psql -U postgres -c "CREATE DATABASE \"XafXPODynAssem\";"
    '

## 3. Schemat bazy (jednorazowo, przed pierwszym startem)

XAF nie utworzy schematu sam — trzeba uruchomic updater:

    pct exec 200 -- bash -lc '
      cd /opt/xpodyn
      docker compose run --rm --entrypoint dotnet xpodyn \
        XafXPODynAssem.Blazor.Server.dll --updateDatabase --forceUpdate --silent
    '

## 4. Start

    pct exec 200 -- bash -lc '
      cd /opt/xpodyn
      export PGPASSWORD="<haslo postgresa z playbooka>"
      export AZURE_OPENAI_KEY="$(...)"   # klucz Azure, nie zapisywac do pliku
      docker compose up -d --build
    '

Aplikacja: `http://10.10.10.10:8080` z Miniaka. Zeby wystawic w LAN, dodaj
przekierowanie portu na hoscie — wzorzec jest w playbooku (sekcja "Docker i baza").

## Ryzyko do sprawdzenia przy pierwszym uruchomieniu

Roslyn kompiluje encje w runtime. Obraz `aspnet:8.0` nie ma SDK — jesli
`RuntimeAssemblyBuilder` szuka referencji przez sciezki SDK zamiast przez
`Assembly.Location`, Deploy Schema padnie. Wtedy podmien bazowy obraz na
`mcr.microsoft.com/dotnet/sdk:8.0` (wiekszy, ale ma komplet referencji).
