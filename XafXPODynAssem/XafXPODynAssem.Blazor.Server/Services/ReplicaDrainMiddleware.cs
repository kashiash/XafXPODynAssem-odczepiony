namespace XafXPODynAssem.Blazor.Server.Services
{
    /// <summary>
    /// Wygaszanie repliki przed restartem: nowych nie wpuszczamy, obecnym pozwalamy skonczyc.
    ///
    /// Kiedy replika ma przejsc na nowy model, restart jest nieunikniony, ale moment
    /// restartu juz nie. Chcemy go przesunac na chwile, gdy nikt na tej replice nie
    /// pracuje — inaczej ktos traci niezapisana tresc formularza.
    ///
    /// Samo czekanie nie wystarcza. Rozdzielacz kieruje po adresie klienta i nie wie,
    /// ze ta replika sie szykuje do restartu; w miejsce osoby, ktora skonczyla,
    /// przysylalby kolejna i liczba pracujacych nigdy nie spadlaby do zera.
    ///
    /// Dlatego w trakcie wygaszania oddajemy 503 na zadania, ktore ZACZYNAJA prace:
    /// wejscie na strone. Rozdzielacz po dwoch takich odpowiedziach odstawia replike
    /// (max_fails=2) i kieruje nowych do pozostalych, a osoba, ktora dostala 503,
    /// nawet tego nie zauwazy — nginx w obrebie tego samego zadania przekaze je zywej
    /// replice, tak jak przy ubitej replice (zmierzone: 210 zadan, 210 odpowiedzi 200).
    ///
    /// Ruchu juz trwajacych obwodow NIE dotykamy: `/_blazor` to gniazdo, ktorym plynie
    /// praca osob obecnych na replice. Odciecie go byloby dokladnie tym zerwaniem,
    /// ktoremu chcemy zapobiec.
    /// </summary>
    public class ReplicaDrainMiddleware
    {
        private readonly RequestDelegate _dalej;

        public ReplicaDrainMiddleware(RequestDelegate dalej) => _dalej = dalej;

        public Task InvokeAsync(HttpContext kontekst)
        {
            // Podglad stanu repliki — ile osob na niej pracuje i czy sie wygasza.
            // Bez tego jedynym sposobem sprawdzenia licznika obwodow bylo czekanie
            // na restart i czytanie logow po fakcie.
            kontekst.Response.Headers["X-Obwody"] = CircuitHandlerProxy.ZywePolaczenia.ToString();
            if (ReplicaSyncService.Wygaszamy) kontekst.Response.Headers["X-Wygaszanie"] = "1";

            if (ReplicaSyncService.Wygaszamy && ZaczynaPrace(kontekst))
            {
                kontekst.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                kontekst.Response.Headers["Retry-After"] = "5";
                return Task.CompletedTask;
            }
            return _dalej(kontekst);
        }

        private static bool ZaczynaPrace(HttpContext kontekst)
        {
            var sciezka = kontekst.Request.Path.Value ?? "/";

            // gniazdo obwodu — tedy plynie praca osob juz obecnych
            if (sciezka.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase)) return false;
            // pliki statyczne obwodu, ktory juz dziala
            if (sciezka.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase)) return false;
            if (sciezka.StartsWith("/_content", StringComparison.OrdinalIgnoreCase)) return false;
            if (sciezka.StartsWith("/schemaUpdateHub", StringComparison.OrdinalIgnoreCase)) return false;

            return true;
        }
    }
}
