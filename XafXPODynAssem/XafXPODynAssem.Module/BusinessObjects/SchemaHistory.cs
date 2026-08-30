using System.ComponentModel;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.Model;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;

namespace XafXPODynAssem.Module.BusinessObjects
{
    public enum SchemaChangeAction
    {
        Import,
        Export,
        Deploy
    }

    [DefaultClassOptions]
    [NavigationItem("Zarządzanie schematem")]
    [DefaultProperty(nameof(Summary))]
    [XafDisplayName("Historia zmian schematu")]
    public class SchemaHistory : BaseObject
    {
        public SchemaHistory(Session session) : base(session) { }

        public override void AfterConstruction()
        {
            base.AfterConstruction();
            Timestamp = DateTime.UtcNow;
        }

        DateTime timestamp;
        [XafDisplayName("Data i czas")]
        public DateTime Timestamp
        {
            get => timestamp;
            set => SetPropertyValue(nameof(Timestamp), ref timestamp, value);
        }

        string userName;
        [XafDisplayName("Użytkownik")]
        public string UserName
        {
            get => userName;
            set => SetPropertyValue(nameof(UserName), ref userName, value);
        }

        SchemaChangeAction action;
        [XafDisplayName("Akcja")]
        public SchemaChangeAction Action
        {
            get => action;
            set => SetPropertyValue(nameof(Action), ref action, value);
        }

        string summary;
        [XafDisplayName("Podsumowanie")]
        public string Summary
        {
            get => summary;
            set => SetPropertyValue(nameof(Summary), ref summary, value);
        }

        string details;
        [VisibleInListView(false)]
        [Size(SizeAttribute.Unlimited)]
        [ModelDefault("RowCount", "20")]
        [XafDisplayName("Szczegóły")]
        public string Details
        {
            get => details;
            set => SetPropertyValue(nameof(Details), ref details, value);
        }

        string schemaJson;
        [VisibleInListView(false)]
        [Size(SizeAttribute.Unlimited)]
        [ModelDefault("RowCount", "25")]
        [XafDisplayName("Schemat JSON")]
        public string SchemaJson
        {
            get => schemaJson;
            set => SetPropertyValue(nameof(SchemaJson), ref schemaJson, value);
        }
    }
}
