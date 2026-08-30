using System.ComponentModel;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.StateMachine;
using DevExpress.ExpressApp.Xpo;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;

namespace XafXPODynAssem.Module.BusinessObjects
{
    /// <summary>
    /// Definicja przeplywu (maszyny stanow) dla encji runtime.
    ///
    /// Dlaczego wlasny magazyn zamiast wbudowanego XpoStateMachine:
    /// DevExpress `StateMachineLogic.GetAvailableMarkerObjects` obsluguje tylko wlasciwosci
    /// typu enum albo referencje do typu persystentnego, a `GetMarkerObjectFromMarkerValue`
    /// dla nie-enumow idzie przez `IObjectSpace.GetObjectByHandle`. Encje runtime maja pola
    /// `System.String`, wiec wbudowany XpoStateMachine nie potrafilby ustawic znacznika stanu.
    /// Tutaj znacznik (Marker) to zwykly string, dzieki czemu stany sa danymi, a nie schematem —
    /// dodanie stanu nie wymaga rekompilacji ani restartu procesu.
    ///
    /// Typ docelowy trzymamy jako pelna nazwe tekstowa i rozwiazujemy go leniwie przez
    /// XafTypesInfo — tak samo robi XpoStateMachine, i wlasnie dlatego przepływ mozna
    /// powiazac z typem tworzonym w runtime.
    /// </summary>
    [DefaultClassOptions]
    [NavigationItem("Zarządzanie schematem")]
    [DefaultProperty(nameof(Name))]
    [XafDisplayName("Przepływ (maszyna stanów)")]
    public class WorkflowDefinition : BaseObject, IStateMachine, IStateMachineUISettings
    {
        public WorkflowDefinition(Session session) : base(session) { }

        private IObjectSpace _objectSpace;

        protected override void OnLoaded()
        {
            base.OnLoaded();
            _objectSpace = XPObjectSpace.FindObjectSpaceByObject(this);
        }

        public override void AfterConstruction()
        {
            base.AfterConstruction();
            _objectSpace = XPObjectSpace.FindObjectSpaceByObject(this);
            IsActive = true;
        }

        string name;
        [XafDisplayName("Nazwa przepływu")]
        [Size(255)]
        public string Name
        {
            get => name;
            set => SetPropertyValue(nameof(Name), ref name, value);
        }

        string targetTypeName;
        [XafDisplayName("Typ docelowy (pełna nazwa)")]
        [Size(512)]
        public string TargetTypeName
        {
            get => targetTypeName;
            set => SetPropertyValue(nameof(TargetTypeName), ref targetTypeName, value);
        }

        string statePropertyName;
        [XafDisplayName("Właściwość sterująca stanem")]
        [Size(255)]
        public string StatePropertyName
        {
            get => statePropertyName;
            set => SetPropertyValue(nameof(StatePropertyName), ref statePropertyName, value);
        }

        bool isActive;
        [XafDisplayName("Aktywny")]
        public bool IsActive
        {
            get => isActive;
            set => SetPropertyValue(nameof(IsActive), ref isActive, value);
        }

        bool expandActionsInDetailView;
        [XafDisplayName("Pokaż przejścia na widoku szczegółów")]
        public bool ExpandActionsInDetailView
        {
            get => expandActionsInDetailView;
            set => SetPropertyValue(nameof(ExpandActionsInDetailView), ref expandActionsInDetailView, value);
        }

        WorkflowState startState;
        [DataSourceProperty(nameof(States))]
        [XafDisplayName("Stan początkowy")]
        public WorkflowState StartState
        {
            get => startState;
            set => SetPropertyValue(nameof(StartState), ref startState, value);
        }

        [Association("WorkflowDefinition-States"), DevExpress.Xpo.Aggregated]
        [XafDisplayName("Stany")]
        public XPCollection<WorkflowState> States => GetCollection<WorkflowState>(nameof(States));

        /// <summary>Krotka nazwa typu docelowego, np. "Faktura". Tylko do prezentacji.</summary>
        [NonPersistent]
        [XafDisplayName("Encja")]
        public string TargetEntityName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(targetTypeName)) return null;
                var dot = targetTypeName.LastIndexOf('.');
                return dot >= 0 ? targetTypeName.Substring(dot + 1) : targetTypeName;
            }
        }

        /// <summary>
        /// Leniwe rozwiazanie typu docelowego. Nigdy nie cache'ujemy wyniku — po hot-loadzie
        /// nowego assembly stary Type bylby martwy.
        /// </summary>
        public Type ResolveTargetType()
        {
            if (string.IsNullOrWhiteSpace(targetTypeName)) return null;
            try
            {
                var typesInfo = _objectSpace?.TypesInfo ?? XafTypesInfo.Instance;
                return typesInfo.FindTypeInfo(targetTypeName)?.Type;
            }
            catch
            {
                return null;
            }
        }

        // -- IStateMachine ----------------------------------------------------

        string IStateMachine.Name => Name;

        /// <summary>
        /// UWAGA: StateMachineCacheController robi `item.Active &amp;&amp; item.TargetObjectType.IsAssignableFrom(...)`.
        /// Gdyby Active bylo true przy nierozwiazanym typie, kazdy widok w aplikacji dostalby
        /// NullReferenceException. Dlatego "aktywny" znaczy tez "typ docelowy sie rozwiazuje".
        /// </summary>
        bool IStateMachine.Active =>
            IsActive
            && !string.IsNullOrWhiteSpace(StatePropertyName)
            && ResolveTargetType() != null;

        Type IStateMachine.TargetObjectType => ResolveTargetType();

        string IStateMachine.StatePropertyName => StatePropertyName ?? string.Empty;

        IList<IState> IStateMachine.States
        {
            get
            {
                var list = new List<IState>();
                foreach (WorkflowState s in States)
                    list.Add(s);
                return list;
            }
        }

        public IState FindCurrentState(object targetObject)
        {
            var type = ResolveTargetType();
            if (type == null || string.IsNullOrWhiteSpace(StatePropertyName))
                return null;

            var os = _objectSpace ?? XPObjectSpace.FindObjectSpaceByObject(this);
            var typesInfo = os?.TypesInfo ?? XafTypesInfo.Instance;
            // Brak wlasciwosci sterujacej => StateMachineLogic rzuca ArgumentException i wywraca widok.
            if (typesInfo.FindTypeInfo(type)?.FindMember(StatePropertyName) == null)
                return null;

            return new StateMachineLogic(os).FindCurrentState(this, targetObject, StartState);
        }

        public void ExecuteTransition(object targetObject, IState targetState)
        {
            var os = _objectSpace ?? XPObjectSpace.FindObjectSpaceByObject(this);
            new StateMachineLogic(os).ExecuteTransition(targetObject, targetState);
        }

        bool IStateMachineUISettings.ExpandActionsInDetailView => ExpandActionsInDetailView;
    }

    /// <summary>Pojedynczy stan przeplywu. Marker to wartosc wpisywana do pola tekstowego encji.</summary>
    [DefaultProperty(nameof(Caption))]
    [XafDisplayName("Stan przepływu")]
    public class WorkflowState : BaseObject, IState
    {
        public WorkflowState(Session session) : base(session) { }

        string caption;
        [XafDisplayName("Nazwa stanu")]
        [Size(255)]
        public string Caption
        {
            get => caption;
            set => SetPropertyValue(nameof(Caption), ref caption, value);
        }

        string markerValue;
        [XafDisplayName("Wartość w polu statusu")]
        [Size(255)]
        public string MarkerValue
        {
            get => string.IsNullOrEmpty(markerValue) ? caption : markerValue;
            set => SetPropertyValue(nameof(MarkerValue), ref markerValue, value);
        }

        string targetObjectCriteria;
        [XafDisplayName("Warunek wejścia w stan (kryterium)")]
        [Size(-1)]
        public string TargetObjectCriteria
        {
            get => targetObjectCriteria;
            set => SetPropertyValue(nameof(TargetObjectCriteria), ref targetObjectCriteria, value);
        }

        int sortOrder;
        [XafDisplayName("Kolejność")]
        public int SortOrder
        {
            get => sortOrder;
            set => SetPropertyValue(nameof(SortOrder), ref sortOrder, value);
        }

        WorkflowDefinition workflow;
        [Association("WorkflowDefinition-States")]
        [XafDisplayName("Przepływ")]
        public WorkflowDefinition Workflow
        {
            get => workflow;
            set => SetPropertyValue(nameof(Workflow), ref workflow, value);
        }

        [Association("WorkflowState-Transitions"), DevExpress.Xpo.Aggregated]
        [XafDisplayName("Przejścia")]
        public XPCollection<WorkflowTransition> Transitions => GetCollection<WorkflowTransition>(nameof(Transitions));

        // -- IState -----------------------------------------------------------

        object IState.Marker => MarkerValue;

        IStateMachine IState.StateMachine => Workflow;

        IList<ITransition> IState.Transitions
        {
            get
            {
                var list = new List<ITransition>();
                foreach (WorkflowTransition t in Transitions)
                    list.Add(t);
                return list;
            }
        }

        public override string ToString() => Caption;
    }

    /// <summary>Dozwolone przejscie ze stanu zrodlowego do docelowego.</summary>
    [DefaultProperty(nameof(Caption))]
    [XafDisplayName("Przejście")]
    public class WorkflowTransition : BaseObject, ITransition, ITransitionUISettings
    {
        public WorkflowTransition(Session session) : base(session) { }

        string caption;
        [XafDisplayName("Etykieta przejścia")]
        [Size(255)]
        public string Caption
        {
            get => string.IsNullOrEmpty(caption) ? TargetState?.Caption : caption;
            set => SetPropertyValue(nameof(Caption), ref caption, value);
        }

        WorkflowState sourceState;
        [Association("WorkflowState-Transitions")]
        [XafDisplayName("Stan źródłowy")]
        public WorkflowState SourceState
        {
            get => sourceState;
            set => SetPropertyValue(nameof(SourceState), ref sourceState, value);
        }

        WorkflowState targetState;
        [XafDisplayName("Stan docelowy")]
        public WorkflowState TargetState
        {
            get => targetState;
            set => SetPropertyValue(nameof(TargetState), ref targetState, value);
        }

        int sortIndex;
        [XafDisplayName("Kolejność")]
        public int SortIndex
        {
            get => sortIndex;
            set => SetPropertyValue(nameof(SortIndex), ref sortIndex, value);
        }

        bool saveAndCloseView;
        [XafDisplayName("Zapisz i zamknij widok")]
        public bool SaveAndCloseView
        {
            get => saveAndCloseView;
            set => SetPropertyValue(nameof(SaveAndCloseView), ref saveAndCloseView, value);
        }

        // -- ITransition / ITransitionUISettings -------------------------------

        IState ITransition.TargetState => TargetState;

        int ITransitionUISettings.Index => SortIndex;

        public override string ToString() => Caption;
    }
}
