using DevExpress.ExpressApp;
using DevExpress.ExpressApp.StateMachine;
using DevExpress.ExpressApp.Utils;

namespace XafXPODynAssem.Module.Controllers
{
    /// <summary>
    /// Utrwala rekord zaraz po wykonaniu przejscia w akcji „Zmien stan".
    ///
    /// Dlaczego to w ogole jest potrzebne: <c>StateMachineController.ExecuteTransition</c>
    /// wola <c>IStateMachine.ExecuteTransition</c> (ustawia znacznik stanu), potem
    /// <c>ObjectSpace.SetModified(targetObject)</c> — i na tym koniec. Commit robi tylko w dwoch
    /// przypadkach: na widoku szczegolow, gdy przejscie ma <c>SaveAndCloseView = true</c>, albo na
    /// widoku listy, gdy <c>ModificationsController.ModificationsHandlingMode = AutoCommit</c>
    /// (domyslna wartosc to <c>Confirmation</c>, a sam tryb jest ustawieniem WinForms).
    /// W efekcie w Blazorze zmiana stanu zostawala w pamieci i bez recznego „Zapisz" pole statusu
    /// w bazie zostawalo puste — uzytkownik widzial, ze przeplyw zadzialal, a nie zadzialal.
    ///
    /// Zapisujemy w <c>TransitionExecuted</c>, a nie we wlasnym <c>WorkflowDefinition.ExecuteTransition</c>,
    /// bo to zdarzenie leci JUZ PO <c>SetModified</c> — commit z wnetrza maszyny stanow zostawilby
    /// widok brudny (aktywny „Zapisz", pytanie o niezapisane zmiany przy zamykaniu).
    ///
    /// Uwaga: <c>CommitChanges</c> zapisuje CALY ObjectSpace widoku, wiec razem ze zmiana stanu ida
    /// inne niezatwierdzone edycje formularza. To nie jest nowe zachowanie — wbudowana galaz
    /// „Zapisz i zamknij widok" robi dokladnie to samo. Jesli formularz nie przejdzie walidacji,
    /// modul Validation rzuci wyjatek na <c>Committing</c> i uzytkownik zobaczy komunikat.
    /// </summary>
    public class WorkflowCommitController : ViewController<ObjectView>
    {
        private StateMachineController stateMachineController;

        protected override void OnActivated()
        {
            base.OnActivated();
            stateMachineController = Frame?.GetController<StateMachineController>();
            if (stateMachineController != null)
                stateMachineController.TransitionExecuted += StateMachine_TransitionExecuted;
        }

        protected override void OnDeactivated()
        {
            if (stateMachineController != null)
            {
                stateMachineController.TransitionExecuted -= StateMachine_TransitionExecuted;
                stateMachineController = null;
            }
            base.OnDeactivated();
        }

        private void StateMachine_TransitionExecuted(object sender, ExecuteTransitionEventArgs e)
        {
            var objectSpace = View?.ObjectSpace;
            if (objectSpace == null || !objectSpace.IsModified) return;

            objectSpace.CommitChanges();

            // Tak samo jak wbudowana galaz AutoCommit: w trybie Server/InstantFeedback grid trzyma
            // wlasne zrodlo i bez przeladowania pokazalby stary stan mimo zapisanego rekordu.
            if (View is ListView listView
                && DataAccessModeHelper.IsLightMode(listView.CollectionSource.DataAccessMode))
                listView.CollectionSource.Reload();
        }
    }
}
