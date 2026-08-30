namespace XafXPODynAssem.Module.Services
{
    /// <summary>
    /// Most „narzędzie AI → projektant raportów". Narzędzie AI nie ma dostępu do Frame/View,
    /// więc <c>build_report</c> publikuje tu KLUCZ świeżo zbudowanego ReportDataV2, a kontroler
    /// na widoku czatu (<c>AIChatReportDesignerController</c>) nasłuchuje i otwiera projektanta.
    ///
    /// Rejestrowany jako <c>scoped</c> — jedna instancja na obwód Blazor, dokładnie ten sam
    /// zasięg co czat (anti cross-user). Wzorzec przeniesiony z DataDrive
    /// (<c>ChatReportSink</c> / <c>DesignerReloadSink</c> / <c>ViewRequestSink</c>).
    /// </summary>
    public sealed class ReportDesignerSink
    {
        /// <summary>Klucz ostatnio zbudowanego raportu (ReportDataV2) — do diagnostyki i fallbacku.</summary>
        public object LastReportKey { get; private set; }

        public event Action<object> DesignerRequested;

        public void RequestDesigner(object reportKey)
        {
            LastReportKey = reportKey;
            DesignerRequested?.Invoke(reportKey);
        }

        /// <summary>Liczba nasłuchujących — bez tego nie da się odróżnić „sink nie wystrzelił"
        /// od „wystrzelił, ale nikt nie słuchał" (regresja diagnozowana tak w DataDrive).</summary>
        public int SubscriberCount => DesignerRequested?.GetInvocationList().Length ?? 0;
    }
}
