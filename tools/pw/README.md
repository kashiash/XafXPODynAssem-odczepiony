# Testy przegladarkowe (node + Playwright)

Wlasna instancja przegladarki zamiast wspoldzielonego serwera MCP — tamten jest
wspolny dla wszystkich sesji i inny agent potrafi przelaczyc karte w trakcie testu.

Playwright bierzemy z instalacji globalnej przez dowiazanie:

    ln -sfn "$(npm root -g)" tools/pw/node_modules

Aplikacja chodzi na certyfikacie deweloperskim, wiec kontekst ma
`ignoreHTTPSErrors: true`, a zrzuty zapisujemy sciezkami bezwzglednymi.

## chat.mjs — rozmowa z asystentem AI

    PW_MSGS="pierwsza wiadomosc||druga wiadomosc" node tools/pw/chat.mjs

## inplace-check.mjs — raport inplace na widoku faktur

    PW_REPORT="Faktura sprzedazy" PW_OUT=/sciezka/na/zrzuty node tools/pw/inplace-check.mjs

Bez `PW_REPORT` skrypt tylko wypisuje pozycje menu „Pokaz na raporcie".

## PW_BASE — inny adres niz lokalny :5031

Domyslnie skrypty celuja w `https://localhost:5031` (aplikacja lokalna). Instancje
zdalna testujemy przez tunel SSH, podajac adres w `PW_BASE`:

    ssh -N -L 8087:127.0.0.1:8087 -p 2221 kashiash@178.217.143.60 &
    PW_BASE=http://localhost:8087 node tools/pw/chat.mjs
