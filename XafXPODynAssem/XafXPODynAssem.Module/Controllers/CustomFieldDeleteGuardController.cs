using DevExpress.ExpressApp;
using DevExpress.ExpressApp.SystemModule;
using DevExpress.Persistent.Base;
using XafXPODynAssem.Module.BusinessObjects;
using XafXPODynAssem.Module.Validation;

namespace XafXPODynAssem.Module.Controllers
{
    /// <summary>
    /// Wylacza akcje „Usun” na polach uzytkownika, ktore sa juz wdrozone albo maja
    /// kolumne w bazie. Zasada: pol nie usuwamy — ukrywamy je (Widoczne na liscie /
    /// Widoczne w szczegolach = Nie). Miekkie kasowanie (GCRecord) zostaje nietkniete,
    /// blokujemy tylko wejscie do niego z UI.
    /// </summary>
    public class CustomFieldDeleteGuardController : ObjectViewController<ObjectView, CustomField>
    {
        private const string ReasonKey = "SchemaGuard_FieldRemovalBlocked";

        private const string ReasonText =
            "Pola uzytkownika nie usuwamy — jest juz wdrozone albo ma kolumne w bazie. " +
            "Usuniecie zostawiloby kolumne z danymi poza kontrola aplikacji i popsuloby raporty " +
            "oraz przeplywy, ktore sie do niej odwoluja. Zamiast usuwac, ukryj pole: " +
            "ustaw „Widoczne na liscie” i „Widoczne w szczegolach” na Nie.";

        private DeleteObjectsViewController _deleteController;

        protected override void OnActivated()
        {
            base.OnActivated();
            _deleteController = Frame.GetController<DeleteObjectsViewController>();
            View.SelectionChanged += View_SelectionChanged;
            UpdateDeleteAction();
        }

        protected override void OnDeactivated()
        {
            View.SelectionChanged -= View_SelectionChanged;
            _deleteController?.DeleteAction?.Enabled?.RemoveItem(ReasonKey);
            _deleteController = null;
            base.OnDeactivated();
        }

        private void View_SelectionChanged(object sender, EventArgs e) => UpdateDeleteAction();

        private void UpdateDeleteAction()
        {
            var action = _deleteController?.DeleteAction;
            if (action == null) return;

            try
            {
                var connStr = XafXPODynAssemModule.RuntimeConnectionString;
                var blocked = View.SelectedObjects?.OfType<CustomField>()
                    .Any(f => !FieldTypeChangeGuard.IsFieldRemovalSafe(
                        f.CustomClass?.ClassName, f.FieldName, connStr)) ?? false;

                action.Enabled.SetItemValue(ReasonKey, !blocked);
                if (blocked)
                    Tracing.Tracer.LogText($"[SchemaGuard] Akcja Usun zablokowana na widoku {View.Id}. {ReasonText}");
            }
            catch (Exception ex)
            {
                Tracing.Tracer.LogError($"[SchemaGuard] Nie udalo sie ocenic usuwania pola: {ex.Message}");
                action.Enabled.SetItemValue(ReasonKey, true);
            }
        }
    }
}
