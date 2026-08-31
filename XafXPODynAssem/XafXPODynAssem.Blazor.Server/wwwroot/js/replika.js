// Pilnowanie czasu po stronie przegladarki. Dwie sprawy, jeden mechanizm, bo obie
// opieraja sie na tym samym: kiedy czlowiek ostatnio cos zrobil.
//
// 1. WYLOGOWANIE PO BEZCZYNNOSCI. Po WYLOGUJ_PO minutach bez ruchu pokazujemy
//    ostrzezenie z odliczaniem, a po nim wychodzimy przez /Logout. Samo skasowanie
//    ciasteczka nie wystarcza — sesja XAF zyje po stronie serwera.
//
// 2. UPRZEDZENIE O RESTARCIE. Kiedy replika szykuje sie do przejscia na nowy model,
//    czlowiek dostaje odliczanie zamiast dowiadywac sie o restarcie w chwili, gdy
//    traci wpisana tresc. To jedyne, co da sie dla niego zrobic: restartu nie unikniemy,
//    ale ostrzezenie zamienia strate w zapisanie.
//
// Przy okazji meldujemy serwerowi, czy ktos cos robi. Bez tego replika nie odroznia
// osoby wpisujacej dane od karty otwartej od rana — a to wlasnie ta roznica decyduje,
// czy restart jest przerwaniem, czy niezauwazalnym zdarzeniem.
(function () {
    var WYLOGUJ_PO = 10 * 60;      // sekund bez czynnosci do wylogowania
    var OSTRZEGAJ_PRZEZ = 60;      // ile sekund przed wylogowaniem ostrzegamy
    var PULS_CO = 15000;           // co ile meldujemy sie serwerowi

    var ostatniaCzynnosc = Date.now();
    var bylaCzynnosc = true;       // pierwszy puls melduje obecnosc
    var pasek, tresc;

    function czynnosc() {
        ostatniaCzynnosc = Date.now();
        bylaCzynnosc = true;
    }

    ['keydown', 'pointerdown', 'pointermove', 'wheel', 'touchstart', 'input', 'focus']
        .forEach(function (z) { document.addEventListener(z, czynnosc, { passive: true, capture: true }); });

    function dajPasek() {
        if (pasek) return pasek;
        pasek = document.createElement('div');
        pasek.id = 'pasek-replika';
        pasek.setAttribute('role', 'status');
        pasek.style.cssText = [
            'position:fixed', 'left:0', 'right:0', 'top:0', 'z-index:10000',
            'padding:10px 16px', 'text-align:center', 'font-size:15px',
            'font-family:system-ui,-apple-system,Segoe UI,sans-serif',
            'background:#8a4b00', 'color:#fff', 'box-shadow:0 2px 8px rgba(0,0,0,.25)'
        ].join(';');
        tresc = document.createElement('span');
        pasek.appendChild(tresc);
        document.body.appendChild(pasek);
        return pasek;
    }

    function pokaz(text, tlo) {
        var p = dajPasek();
        tresc.textContent = text;
        p.style.background = tlo;
        p.style.display = 'block';
    }

    function schowaj() {
        if (pasek) pasek.style.display = 'none';
    }

    // --- odliczanie do restartu repliki -------------------------------------
    var doRestartu = null;   // sekund, albo null gdy replika nie szykuje restartu

    function puls() {
        var byla = bylaCzynnosc;
        bylaCzynnosc = false;
        fetch('replika/puls?a=' + (byla ? '1' : '0'), { cache: 'no-store' })
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (d) {
                doRestartu = d && d.wygaszanie ? d.zostalo : null;
            })
            .catch(function () { /* replika moze wlasnie wstawac — nic nie robimy */ });
    }

    // --- co sekunde: co pokazac i czy juz wylogowac -------------------------
    setInterval(function () {
        var bezczynny = Math.floor((Date.now() - ostatniaCzynnosc) / 1000);
        var doWylogowania = WYLOGUJ_PO - bezczynny;

        if (doWylogowania <= 0) {
            location.href = 'Logout';
            return;
        }

        // Wylogowanie ma pierwszenstwo: dotyczy tej osoby wprost i konczy sie
        // wyrzuceniem z aplikacji, a restart tylko przerwa na kilka sekund.
        if (doWylogowania <= OSTRZEGAJ_PRZEZ) {
            pokaz('Brak aktywności. Wylogujemy Cię za ' + doWylogowania + ' s. Rusz myszą, żeby zostać.', '#7a1f1f');
            return;
        }

        if (doRestartu !== null) {
            if (doRestartu > 0) doRestartu--;
            pokaz('Aplikacja zaktualizuje się za ' + doRestartu + ' s. Zapisz pracę.', '#8a4b00');
            return;
        }

        schowaj();
    }, 1000);

    setInterval(puls, PULS_CO);
    puls();
})();
