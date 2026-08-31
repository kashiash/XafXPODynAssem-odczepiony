using System.Collections.Concurrent;
using DevExpress.ExpressApp.Blazor.Services;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace XafXPODynAssem.Blazor.Server.Services
{
    internal class CircuitHandlerProxy : CircuitHandler
    {
        // Kto w tej chwili pracuje na TEJ replice. Potrzebne przy przechodzeniu na nowy
        // model: restart zrywa obwod i niezapisana tresc formularza przepada, wiec
        // replika najpierw czeka, az nikogo nie bedzie.
        //
        // Trzymamy ZBIOR identyfikatorow obwodow, a nie licznik par zdarzen. Pierwsza
        // wersja liczyla "w gore na polaczeniu, w dol na rozlaczeniu" i pokazywala -1
        // przy jednej zywej sesji: rozlaczenia przychodzily, odpowiadajace im polaczenia
        // nie. Zbior jest na to odporny — dopisanie tego samego obwodu drugi raz nic nie
        // zmienia, a usuniecie nieznanego nie zbija stanu ponizej zera.
        private static readonly ConcurrentDictionary<string, byte> _obwody = new();

        internal static int ZywePolaczenia => _obwody.Count;

        readonly IScopedCircuitHandler scopedCircuitHandler;
        public CircuitHandlerProxy(IScopedCircuitHandler scopedCircuitHandler)
        {
            this.scopedCircuitHandler = scopedCircuitHandler;
        }
        public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            _obwody[circuit.Id] = 1;
            return scopedCircuitHandler.OnCircuitOpenedAsync(cancellationToken);
        }
        public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            _obwody[circuit.Id] = 1;
            return scopedCircuitHandler.OnConnectionUpAsync(cancellationToken);
        }
        public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            _obwody.TryRemove(circuit.Id, out _);
            return scopedCircuitHandler.OnCircuitClosedAsync(cancellationToken);
        }
        public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            // Rozlaczenie to jeszcze nie koniec pracy — przegladarka potrafi wrocic do
            // tego samego obwodu po mrugnieciu sieci. Obwod usuwamy dopiero przy
            // zamknieciu, zeby nie uznac kogos za nieobecnego w trakcie ponownego laczenia.
            return scopedCircuitHandler.OnConnectionDownAsync(cancellationToken);
        }
    }
}
