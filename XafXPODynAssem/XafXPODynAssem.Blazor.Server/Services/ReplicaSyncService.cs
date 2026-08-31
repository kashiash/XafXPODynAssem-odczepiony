using DevExpress.Persistent.Base;
using Microsoft.Extensions.Hosting;

namespace XafXPODynAssem.Blazor.Server.Services
{
    /// <summary>
    /// Pilnuje, zeby wszystkie repliki chodzily na tym samym modelu runtime.
    ///
    /// Problem: Deploy Schema konczy proces kodem 42, ale robi to TYLKO ta replika,
    /// z ktorej wdrozenie wywolano. Pozostale chodza dalej ze starym modelem, wiec
    /// uzytkownik trafiajacy na nie nie widzi nowej encji. Rozdzielacz kieruje ruch
    /// po adresie klienta, wiec kto trafil na stara replike, ten zostaje na niej.
    ///
    /// Rozwiazanie: kazda replika co kilkanascie sekund liczy odcisk metadanych.
    /// Gdy odcisk sie zmieni — inna replika wdrozyla schemat — ta tez sie restartuje.
    ///
    /// Trzy warunki, bez ktorych to by szkodzilo zamiast pomagac:
    ///
    /// 1. NIE naraz. Gdyby wszystkie trzy zobaczyly zmiane w tej samej sekundzie,
    ///    wszystkie trzy zgaslyby jednoczesnie — czyli dokladnie ta przerwa, dla ktorej
    ///    trzymamy trzy repliki. Kazda czeka wiec swoja kolej: numer razy ODSTEP.
    /// 2. Tylko przy zywym sasiedzie. Tuz przed restartem replika sprawdza, czy ktoras
    ///    z pozostalych odpowiada. Jesli nie — czeka, zamiast dolozyc sie do przerwy.
    /// 3. Tylko przy metadanych, ktore sie kompiluja. Zepsute metadane po restarcie
    ///    beda tak samo zepsute; restart zamienilby jedna zla replike w petle restartow.
    /// 4. Nie w srodku czyjejs pracy. Restart zrywa obwod Blazora — strona wraca sama
    ///    i zalogowana, ale niezapisana tresc formularza przepada. Replika wiec najpierw
    ///    przestaje przyjmowac nowych, a potem czeka na SPOKOJNA CHWILE: moment, gdy
    ///    nikogo nie ma albo nikt nic nie robi od BEZCZYNNOSC sekund.
    ///
    ///    Czekanie na "az wszyscy skoncza" nie dziala: kto pracuje godzine, ten pracuje
    ///    godzine, a modelu nie mozna trzymac rozjechanego bez konca. Liczy sie wiec
    ///    ostatnia czynnosc, a nie sama obecnosc — otwarta karta, w ktorej nikt nic nie
    ///    robi, restartu nie blokuje, a osoba w trakcie wpisywania go odsuwa.
    ///
    ///    Gdy spokojna chwila nie nadejdzie do CIERPLIWOSC sekund, restartujemy mimo to.
    ///    Ale nie po cichu: od poczatku wygaszania strona pokazuje odliczanie, wiec
    ///    czlowiek wie, ile ma czasu na zapisanie, zamiast dowiadywac sie o restarcie
    ///    po stracie tresci.
    ///
    /// Wlacza sie wylacznie, gdy ustawiono REPLIKA_INDEKS — pojedyncza instancja
    /// (dev, jeden kontener) dziala jak dotad, bez zadnego odpytywania.
    ///
    /// Zmienne srodowiska:
    ///   REPLIKA_INDEKS  numer w kolejce restartow, od 0 (wlacza mechanizm)
    ///   REPLIKA_PEERS   adresy wszystkich replik po przecinku, np.
    ///                   http://127.0.0.1:8101,http://127.0.0.1:8102,http://127.0.0.1:8103
    ///   REPLIKA_ODSTEP  sekundy miedzy kolejnymi replikami (domyslnie 90)
    ///   REPLIKA_SONDA   co ile sekund liczymy odcisk (domyslnie 15)
    ///   REPLIKA_ROZBIEG sekundy zwloki po starcie, zanim zaczniemy pilnowac (domyslnie 60)
    ///   REPLIKA_CIERPLIWOSC do ilu sekund czekamy na spokojna chwile (domyslnie 300)
    ///   REPLIKA_BEZCZYNNOSC po ilu sekundach bez czynnosci uznajemy chwile za spokojna (domyslnie 45)
    /// </summary>
    public static class ReplicaSyncService
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };

        private static volatile bool _wygaszamy;
        private static long _ostatniaCzynnosc = DateTime.UtcNow.Ticks;
        private static long _terminRestartu;

        /// <summary>
        /// Zglasza, ze ktos wlasnie cos na tej replice zrobil. Wola to strona,
        /// bo praca w Blazorze idzie gniazdem, ktorego serwer HTTP nie widzi.
        /// </summary>
        public static void ZglosCzynnosc()
            => Interlocked.Exchange(ref _ostatniaCzynnosc, DateTime.UtcNow.Ticks);

        /// <summary>Ile sekund od ostatniej czynnosci kogokolwiek na tej replice.</summary>
        public static int SekundOdCzynnosci
            => (int)(DateTime.UtcNow - new DateTime(Interlocked.Read(ref _ostatniaCzynnosc), DateTimeKind.Utc)).TotalSeconds;

        /// <summary>
        /// Ile sekund zostalo do restartu, ktorego juz nie odwlekamy. Zero, gdy nie
        /// szykujemy restartu. Strona pokazuje to jako odliczanie, zeby czlowiek
        /// zdazyl zapisac prace zamiast dowiadywac sie o restarcie po jej stracie.
        /// </summary>
        public static int SekundDoRestartu
        {
            get
            {
                var termin = Interlocked.Read(ref _terminRestartu);
                if (termin == 0) return 0;
                var zostalo = (int)(new DateTime(termin, DateTimeKind.Utc) - DateTime.UtcNow).TotalSeconds;
                return zostalo > 0 ? zostalo : 0;
            }
        }

        /// <summary>
        /// Czy replika szykuje sie do restartu i nie chce juz nowych osob.
        /// Czyta to <see cref="ReplicaDrainMiddleware"/>.
        /// </summary>
        public static bool Wygaszamy => _wygaszamy;

        public static void Start()
        {
            if (!int.TryParse(Environment.GetEnvironmentVariable("REPLIKA_INDEKS"), out var indeks))
                return;   // pojedyncza instancja — nie ma czego pilnowac

            var peers = (Environment.GetEnvironmentVariable("REPLIKA_PEERS") ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim().TrimEnd('/'))
                .Where(a => a.Length > 0)
                .ToArray();

            var odstep = Sekundy("REPLIKA_ODSTEP", 90);
            var sonda = Sekundy("REPLIKA_SONDA", 15);
            var rozbieg = Sekundy("REPLIKA_ROZBIEG", 60);
            var cierpliwosc = Sekundy("REPLIKA_CIERPLIWOSC", 300);
            var bezczynnosc = Sekundy("REPLIKA_BEZCZYNNOSC", 45);

            _ = Task.Run(() => Pilnuj(indeks, peers, odstep, sonda, rozbieg, cierpliwosc, bezczynnosc));
        }

        private static int Sekundy(string zmienna, int domyslnie)
            => int.TryParse(Environment.GetEnvironmentVariable(zmienna), out var v) && v > 0 ? v : domyslnie;

        private static async Task Pilnuj(int indeks, string[] peers, int odstep, int sonda, int rozbieg, int cierpliwosc, int bezczynnosc)
        {
            var connStr = XafXPODynAssem.Module.XafXPODynAssemModule.RuntimeConnectionString;
            if (string.IsNullOrEmpty(connStr)) return;

            await Task.Delay(TimeSpan.FromSeconds(rozbieg));

            string wzorzec;
            try
            {
                wzorzec = XafXPODynAssem.Module.XafXPODynAssemModule.GetMetadataFingerprint(connStr);
            }
            catch (Exception ex)
            {
                Log($"nie udalo sie odczytac odcisku na starcie: {ex.Message} — rezygnuje z pilnowania");
                return;
            }

            Log($"pilnuje modelu; numer w kolejce {indeks}, odstep {odstep}s, odcisk {Skrot(wzorzec)}");

            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(sonda));

                string teraz;
                try { teraz = XafXPODynAssem.Module.XafXPODynAssemModule.GetMetadataFingerprint(connStr); }
                catch (Exception ex) { Log($"odczyt odcisku nieudany: {ex.Message}"); continue; }

                if (teraz == wzorzec) continue;

                Log($"metadane zmienione przez inna replike ({Skrot(wzorzec)} -> {Skrot(teraz)})");

                // 3. czy nowe metadane w ogole sie kompiluja
                bool zdatne;
                List<string> bledy;
                try
                {
                    zdatne = XafXPODynAssem.Module.XafXPODynAssemModule
                        .ValidateRuntimeMetadata(connStr, out _, out bledy);
                }
                catch (Exception ex) { Log($"walidacja nieudana: {ex.Message} — nie restartuje"); continue; }

                if (!zdatne)
                {
                    Log("nowe metadane sie nie kompiluja — NIE restartuje, zostaje na starym modelu");
                    foreach (var b in bledy.Take(5)) Log("  " + b);
                    wzorzec = teraz;   // nie probujemy w kolko tego samego zepsucia
                    continue;
                }

                // 1. kazda replika czeka swoja kolej
                if (indeks > 0)
                {
                    Log($"czekam {indeks * odstep}s na swoja kolej");
                    await Task.Delay(TimeSpan.FromSeconds(indeks * odstep));
                }

                // 2. restart tylko wtedy, gdy jest kto przejac ruch
                if (!await JestZywySasiad(peers))
                {
                    Log("zadna inna replika nie odpowiada — odkladam restart do nastepnej sondy");
                    continue;
                }

                await PoczekajNaDobryMoment(cierpliwosc, bezczynnosc);

                Log("restartuje, zeby przejsc na nowy model (kod 42)");
                RestartService.RequestRestart();
                return;
            }
        }

        /// <summary>
        /// Czeka, az na tej replice nikt nie pracuje. Restart zrywa obwod Blazora:
        /// strona wraca sama i uzytkownik zostaje zalogowany, ale niezapisana praca
        /// w otwartym formularzu przepada. Czekanie przenosi restart na moment, gdy
        /// nikogo to nie kosztuje.
        ///
        /// Czekamy do skutku, ale nie bez konca: jedna zapomniana karta w przegladarce
        /// trzymalaby replike na starym modelu w nieskonczonosc. Po CIERPLIWOSC sekund
        /// restartujemy mimo wszystko — lepiej przerwac jednej osobie, niz zostawic
        /// replike, ktora nie zna nowych encji.
        /// </summary>
        private static async Task PoczekajNaDobryMoment(int cierpliwosc, int bezczynnosc)
        {
            // Od tej chwili nie przyjmujemy nowych osob: rozdzielacz po dwoch nieudanych
            // probach odstawia te replike i kieruje nowych do pozostalych. Bez tego
            // czekanie moglo by nie skonczyc sie nigdy, bo w miejsce osoby, ktora
            // skonczyla, rozdzielacz przysylalby kolejna.
            _wygaszamy = true;
            Interlocked.Exchange(ref _terminRestartu, DateTime.UtcNow.AddSeconds(cierpliwosc).Ticks);

            if (CircuitHandlerProxy.ZywePolaczenia == 0) return;

            Log($"na replice jest {CircuitHandlerProxy.ZywePolaczenia} os. — wygaszam, czekam na spokojna chwile (do {cierpliwosc}s)");
            var koniec = DateTime.UtcNow.AddSeconds(cierpliwosc);
            while (DateTime.UtcNow < koniec)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));

                if (CircuitHandlerProxy.ZywePolaczenia == 0)
                {
                    Log("nikogo juz nie ma — restartuje bez przerywania komukolwiek");
                    return;
                }
                if (SekundOdCzynnosci >= bezczynnosc)
                {
                    Log($"nikt nic nie robi od {SekundOdCzynnosci}s — restartuje w spokojnej chwili");
                    return;
                }
            }
            Log($"po {cierpliwosc}s ktos nadal pracuje — restartuje mimo to (byl uprzedzony odliczaniem)");
        }

        /// <summary>
        /// Czy poza nami odpowiada jeszcze ktos. Wlasny adres rozpoznajemy po porcie
        /// z ASPNETCORE_URLS — bez tego replika uznalaby sama siebie za sasiada.
        /// Gdy lista sasiadow jest pusta, nie blokujemy restartu: to znaczy, ze nikt
        /// nie skonfigurowal ukladu wielu replik i nie ma czego chronic.
        /// </summary>
        private static async Task<bool> JestZywySasiad(string[] peers)
        {
            if (peers.Length == 0) return true;

            var mojPort = (Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "")
                .Split(':').LastOrDefault()?.Trim('/');

            foreach (var adres in peers)
            {
                if (!string.IsNullOrEmpty(mojPort) && adres.EndsWith(":" + mojPort, StringComparison.Ordinal))
                    continue;
                try
                {
                    using var odp = await _http.GetAsync(adres + "/", HttpCompletionOption.ResponseHeadersRead);
                    if (odp.IsSuccessStatusCode) return true;
                }
                catch { /* ta replika nie odpowiada — probujemy kolejnej */ }
            }
            return false;
        }

        private static string Skrot(string odcisk)
            => string.IsNullOrEmpty(odcisk) ? "-" : odcisk.Substring(0, Math.Min(8, odcisk.Length));

        private static void Log(string tresc)
        {
            var kto = Environment.GetEnvironmentVariable("REPLIKA") ?? "replika";
            Console.WriteLine($"[ReplicaSync/{kto}] {tresc}");
            Tracing.Tracer.LogText($"[ReplicaSync/{kto}] {tresc}");
        }
    }
}
