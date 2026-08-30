using DevExpress.Data.Filtering;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.SystemModule;
using DevExpress.ExpressApp.Utils;
using XafXPODynAssem.Module.BusinessObjects;
using XafXPODynAssem.Module.Model;

namespace XafXPODynAssem.Module.Controllers
{
    /// <summary>
    /// Rdzen pulpitu kafelkowego. Sklada dane dla kontrolki platformowej: kategorie statyczne
    /// z modelu aplikacji oraz — i to jest sedno w tej aplikacji — kategorie DYNAMICZNE
    /// budowane z metadanych <see cref="CustomClass"/>, bo encje powstaja tu w runtime
    /// i nie da sie ich wpisac z gory do .xafml.
    /// </summary>
    public class NavigationHubController : WindowController
    {
        /// <summary>Kategoria dla encji runtime bez ustawionej grupy nawigacji.</summary>
        const string DefaultRuntimeCategory = "Encje runtime";

        /// <summary>Kolory kafelkow encji runtime — cykl, zeby kategorie dalo sie odroznic.</summary>
        static readonly string[] RuntimePalette =
            { "#0288D1", "#388E3C", "#F57C00", "#7B1FA2", "#C2185B", "#00796B" };

        public NavigationHubController()
        {
            TargetWindowType = WindowType.Main;
        }

        public List<HubCategoryData> GetHubData()
        {
            var permittedItemIds = CollectPermittedItemIds();
            var categories = new List<HubCategoryData>();

            categories.AddRange(BuildStaticCategories(permittedItemIds));
            categories.AddRange(BuildRuntimeCategories(permittedItemIds, categories));

            return categories;
        }

        // -- Kategorie statyczne (model aplikacji) --------------------------------

        List<HubCategoryData> BuildStaticCategories(HashSet<string> permittedItemIds)
        {
            var result = new List<HubCategoryData>();
            if (Application?.Model is not IModelNavigationHubExtension model) return result;

            var hubModel = model.NavigationHub;
            if (hubModel == null) return result;

            foreach (IModelHubCategory category in hubModel.OrderBy(c => c.SortOrder))
            {
                var buttons = new List<HubButtonData>();
                foreach (IModelHubButton button in category.Buttons.OrderBy(b => b.SortOrder))
                {
                    var isExternal = !string.IsNullOrEmpty(button.ExternalUrl);
                    if (!isExternal
                        && !string.IsNullOrEmpty(button.NavigationItemId)
                        && !permittedItemIds.Contains(button.NavigationItemId))
                        continue;

                    buttons.Add(new HubButtonData
                    {
                        Id = button.Id,
                        Caption = button.Caption,
                        ImageName = button.ImageName,
                        ImageUrl = ResolveImageUrl(button.ImageName),
                        NavigationItemId = button.NavigationItemId,
                        Color = button.Color,
                        ExternalUrl = button.ExternalUrl ?? string.Empty
                    });
                }
                if (buttons.Count > 0)
                {
                    result.Add(new HubCategoryData
                    {
                        Id = category.Id,
                        Caption = category.Caption,
                        Buttons = buttons
                    });
                }
            }
            return result;
        }

        // -- Kategorie dynamiczne (encje runtime) ---------------------------------

        /// <summary>
        /// Buduje kafelki z <see cref="CustomClass"/> o statusie Runtime, grupujac po
        /// <c>NavigationGroup</c>. ID widoku skladamy jako <c>{ClassName}_ListView</c>, ale
        /// kafelek powstaje TYLKO wtedy, gdy takie ID naprawde jest wsrod pozycji nawigacji
        /// dostepnych uzytkownikowi. Dzieki temu nie da sie wyprodukowac kafelka prowadzacego
        /// donikad — nawet gdyby XAF zmienil konwencje nazywania widokow.
        /// </summary>
        List<HubCategoryData> BuildRuntimeCategories(
            HashSet<string> permittedItemIds, List<HubCategoryData> alreadyBuilt)
        {
            var result = new List<HubCategoryData>();

            // Nie powtarzamy kafelka, ktory jest juz w kategorii statycznej.
            var alreadyShown = new HashSet<string>(
                alreadyBuilt.SelectMany(c => c.Buttons).Select(b => b.NavigationItemId),
                StringComparer.OrdinalIgnoreCase);

            List<CustomClass> runtimeClasses;
            try
            {
                using var os = Application.CreateObjectSpace(typeof(CustomClass));
                runtimeClasses = os.GetObjects<CustomClass>()
                    .Where(c => c.Status == CustomClassStatus.Runtime)
                    .ToList();
            }
            catch
            {
                // Brak dostepu do metadanych nie moze wywrocic calego pulpitu.
                return result;
            }

            var groups = runtimeClasses
                .Where(c => !string.IsNullOrWhiteSpace(c.ClassName))
                .GroupBy(c => string.IsNullOrWhiteSpace(c.NavigationGroup)
                    ? DefaultRuntimeCategory
                    : c.NavigationGroup.Trim())
                .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var colorIndex = 0;
            foreach (var group in groups)
            {
                var buttons = new List<HubButtonData>();
                foreach (var cc in group.OrderBy(c => c.ClassName, StringComparer.CurrentCultureIgnoreCase))
                {
                    var viewId = $"{cc.ClassName}_ListView";
                    if (!permittedItemIds.Contains(viewId)) continue;   // samowalidacja
                    if (!alreadyShown.Add(viewId)) continue;            // juz jest statycznie

                    var image = ResolveRuntimeImageName(cc.ClassName);
                    buttons.Add(new HubButtonData
                    {
                        Id = $"Runtime_{cc.ClassName}",
                        Caption = cc.ClassName,
                        ImageName = image,
                        ImageUrl = ResolveImageUrl(image),
                        NavigationItemId = viewId,
                        Color = RuntimePalette[colorIndex % RuntimePalette.Length],
                        ExternalUrl = string.Empty
                    });
                }

                if (buttons.Count > 0)
                {
                    result.Add(new HubCategoryData
                    {
                        Id = $"Runtime_{group.Key}",
                        Caption = group.Key,
                        Buttons = buttons
                    });
                    colorIndex++;
                }
            }
            return result;
        }

        /// <summary>
        /// Dobiera ikone kafelka encji runtime po nazwie klasy. Encje powstaja w runtime,
        /// wiec nikt nie przypisze im ikony recznie — zgadujemy po nazwie, a gdy nic nie pasuje,
        /// wracamy do generycznej ikony klasy. Nazwy obrazow sa z DevExpress.Images.
        /// </summary>
        static string ResolveRuntimeImageName(string className)
        {
            var n = (className ?? string.Empty).ToLowerInvariant();
            if (n.Contains("faktur") || n.Contains("invoice")) return "BO_Invoice";
            if (n.Contains("produkt") || n.Contains("product") || n.Contains("towar")) return "BO_Product";
            if (n.Contains("klient") || n.Contains("customer") || n.Contains("kontrahent")) return "BO_Customer";
            if (n.Contains("pracownik") || n.Contains("employee")) return "BO_Employee";
            if (n.Contains("zamowien") || n.Contains("order")) return "BO_Order";
            if (n.Contains("kontakt") || n.Contains("contact") || n.Contains("osoba")) return "BO_Contact";
            if (n.Contains("dzial") || n.Contains("department")) return "BO_Department";
            if (n.Contains("kategor") || n.Contains("category") || n.Contains("stawka")) return "BO_Category";
            if (n.Contains("raport") || n.Contains("report")) return "BO_Report";
            if (n.Contains("notat") || n.Contains("note")) return "BO_Note";
            return "ModelEditor_Class_Object";
        }

        // -- Pozycje nawigacji ----------------------------------------------------

        HashSet<string> CollectPermittedItemIds()
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var navAction = Frame?.GetController<ShowNavigationItemController>()?.ShowNavigationItemAction;
            if (navAction != null) CollectPermittedItems(navAction.Items, ids);
            return ids;
        }

        void CollectPermittedItems(ChoiceActionItemCollection items, HashSet<string> ids)
        {
            foreach (var item in items)
            {
                if (item.Enabled && item.Active && item.Data is ViewShortcut shortcut)
                {
                    ids.Add(item.GetIdPath());
                    if (!string.IsNullOrEmpty(shortcut.ViewId))
                        ids.Add(shortcut.ViewId);
                }
                if (item.Items.Count > 0) CollectPermittedItems(item.Items, ids);
            }
        }

        public void NavigateToItem(string navigationItemId)
        {
            var navAction = Frame?.GetController<ShowNavigationItemController>()?.ShowNavigationItemAction;
            if (navAction == null) return;

            var item = FindItemById(navAction.Items, navigationItemId);
            if (item != null && item.Enabled && item.Active)
                navAction.DoExecute(item);
        }

        ChoiceActionItem FindItemById(ChoiceActionItemCollection items, string id)
        {
            foreach (var item in items)
            {
                if (item.GetIdPath() == id || (item.Data is ViewShortcut vs && vs.ViewId == id))
                    return item;
                if (item.Items.Count > 0)
                {
                    var found = FindItemById(item.Items, id);
                    if (found != null) return found;
                }
            }
            return null;
        }

        // -- Przypiecia -----------------------------------------------------------

        /// <summary>
        /// Zwraca przypiecia uzytkownika, po drodze SPRZATAJAC martwe — czyli takie, ktorych
        /// kafelka juz nie ma (encja skasowana z CustomClass albo pozycja znikla z nawigacji).
        /// Zapis wraca do bazy tylko wtedy, gdy cos faktycznie umarlo.
        /// </summary>
        public List<string> GetPinnedItemIds()
        {
            if (SecuritySystem.CurrentUserId is not Guid userId) return new List<string>();

            List<string> stored;
            using (var os = Application.CreateObjectSpace(typeof(UserHubPreference)))
            {
                stored = os.GetObjects<UserHubPreference>(CriteriaOperator.Parse("UserId = ?", userId))
                    .OrderBy(p => p.SortOrder)
                    .Select(p => p.NavigationItemId)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .ToList();
            }
            if (stored.Count == 0) return stored;

            var live = new HashSet<string>(
                GetHubData().SelectMany(c => c.Buttons).Select(b => b.NavigationItemId),
                StringComparer.OrdinalIgnoreCase);

            var alive = stored.Where(id => live.Contains(id)).ToList();
            if (alive.Count != stored.Count)
                SetPinnedItems(alive);   // jeden dodatkowy zapis, tylko gdy cos umarlo

            return alive;
        }

        public void SetPinnedItems(List<string> navigationItemIds)
        {
            if (navigationItemIds == null) return;
            if (SecuritySystem.CurrentUserId is not Guid userId) return;

            using var os = Application.CreateObjectSpace(typeof(UserHubPreference));
            var existing = os.GetObjects<UserHubPreference>(
                CriteriaOperator.Parse("UserId = ?", userId)).ToList();
            foreach (var e in existing) os.Delete(e);

            for (int i = 0; i < navigationItemIds.Count; i++)
            {
                var pref = os.CreateObject<UserHubPreference>();
                pref.UserId = userId;
                pref.NavigationItemId = navigationItemIds[i];
                pref.SortOrder = i;
            }
            os.CommitChanges();
        }

        static string ResolveImageUrl(string imageName)
        {
            if (string.IsNullOrEmpty(imageName)) return string.Empty;
            var imageInfo = ImageLoader.Instance.GetLargeImageInfo(imageName);
            if (imageInfo.IsEmpty) imageInfo = ImageLoader.Instance.GetImageInfo(imageName);
            if (imageInfo.IsEmpty) return string.Empty;
            if (!imageInfo.IsUrlEmpty) return imageInfo.ImageUrl;
            if (imageInfo.ImageBytes is { Length: > 0 } bytes)
            {
                var mime = imageInfo.IsSvgImage ? "image/svg+xml" : "image/png";
                return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
            }
            return string.Empty;
        }
    }

    public class HubCategoryData
    {
        public string Id { get; set; } = string.Empty;
        public string Caption { get; set; } = string.Empty;
        public List<HubButtonData> Buttons { get; set; } = new();
    }

    public class HubButtonData
    {
        public string Id { get; set; } = string.Empty;
        public string Caption { get; set; } = string.Empty;
        public string ImageName { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string NavigationItemId { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public string ExternalUrl { get; set; } = string.Empty;
    }
}
