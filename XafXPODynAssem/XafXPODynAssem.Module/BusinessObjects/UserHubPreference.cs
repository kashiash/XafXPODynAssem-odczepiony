using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;

namespace XafXPODynAssem.Module.BusinessObjects
{
    /// <summary>
    /// Przypiete kafelki pulpitu, osobno dla kazdego uzytkownika.
    /// XPO, nie EF — schemat zaklada <c>UpdateSchema</c>, zadnych migracji.
    /// Swiadomie BEZ [DefaultClassOptions]: to dane techniczne, nie chcemy dla nich
    /// pozycji w nawigacji ani widokow.
    /// </summary>
    public class UserHubPreference : BaseObject
    {
        public UserHubPreference(Session session) : base(session) { }

        Guid userId;
        public Guid UserId
        {
            get => userId;
            set => SetPropertyValue(nameof(UserId), ref userId, value);
        }

        string navigationItemId;
        public string NavigationItemId
        {
            get => navigationItemId;
            set => SetPropertyValue(nameof(NavigationItemId), ref navigationItemId, value);
        }

        int sortOrder;
        public int SortOrder
        {
            get => sortOrder;
            set => SetPropertyValue(nameof(SortOrder), ref sortOrder, value);
        }
    }
}
