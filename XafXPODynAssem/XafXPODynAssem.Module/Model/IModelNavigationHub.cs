using System.ComponentModel;
using DevExpress.ExpressApp.Model;

namespace XafXPODynAssem.Module.Model
{
    /// <summary>
    /// Rozszerza model aplikacji o wezel <c>NavigationHub</c>, w ktorym konfiguruje sie
    /// kategorie i kafelki pulpitu. Kategorie STATYCZNE pochodza stad; kategorie dla encji
    /// tworzonych w runtime dokłada <c>NavigationHubController</c> z metadanych CustomClass,
    /// bo klasy powstajacej dopiero w trakcie dzialania nie da sie wpisac z gory do .xafml.
    /// </summary>
    public interface IModelNavigationHubExtension : IModelNode
    {
        IModelNavigationHub NavigationHub { get; }
    }

    public interface IModelNavigationHub : IModelNode, IModelList<IModelHubCategory> { }

    [KeyProperty(nameof(Id))]
    public interface IModelHubCategory : IModelNode
    {
        string Id { get; set; }
        [Localizable(true)]
        string Caption { get; set; }
        int SortOrder { get; set; }
        IModelHubButtons Buttons { get; }
    }

    public interface IModelHubButtons : IModelNode, IModelList<IModelHubButton> { }

    [KeyProperty(nameof(Id))]
    public interface IModelHubButton : IModelNode
    {
        string Id { get; set; }
        [Localizable(true)]
        string Caption { get; set; }
        string ImageName { get; set; }
        string NavigationItemId { get; set; }
        string Color { get; set; }
        int SortOrder { get; set; }
        string ExternalUrl { get; set; }
    }
}
