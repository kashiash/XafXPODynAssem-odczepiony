using System.Text.RegularExpressions;
using DevExpress.Persistent.Base;
using XafXPODynAssem.Module.BusinessObjects;

namespace XafXPODynAssem.Module.Validation
{
    public static class CustomFieldValidation
    {
        private static readonly Regex ValidIdentifierRegex = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        private static readonly HashSet<string> ReservedFieldNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Oid", "ObjectType", "GCRecord", "OptimisticLockField"
        };

        public static bool IsValidIdentifier(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && ValidIdentifierRegex.IsMatch(name);
        }

        public static bool IsReservedFieldName(string name)
        {
            return ReservedFieldNames.Contains(name);
        }

        /// <summary>
        /// Ostatnia linia obrony przed zapisem metadanej, ktora kloci sie z rzeczywistym
        /// schematem bazy — lapie edycje z UI oraz import schematu. Nie porownujemy ze
        /// stara wartoscia, tylko z typem kolumny, ktora juz istnieje: to dokladnie ten
        /// warunek, ktory wywraca UpdateSchema. Szczegoly: <see cref="FieldTypeChangeGuard"/>.
        /// </summary>
        public static bool IsTypeChangeSafe(CustomField field)
        {
            if (field == null) return true;

            try
            {
                var className = field.CustomClass?.ClassName;
                if (string.IsNullOrWhiteSpace(className) || string.IsNullOrWhiteSpace(field.FieldName))
                    return true;

                var columnType = FieldTypeChangeGuard.GetColumnDataType(
                    className, field.FieldName, XafXPODynAssemModule.RuntimeConnectionString);

                if (columnType == null)
                    return true; // kolumny jeszcze nie ma — zwykle dodanie pola, przepuszczamy

                if (FieldTypeChangeGuard.IsColumnCompatible(field.TypeName, columnType))
                    return true;

                Tracing.Tracer.LogError(
                    $"[SchemaGuard] Zablokowany zapis: {className}.{field.FieldName} ma w metadanych " +
                    $"„{FieldTypeChangeGuard.Describe(field.TypeName, field.ReferencedClassName)}”, " +
                    $"a kolumna w bazie jest typu „{columnType}”.");
                return false;
            }
            catch (Exception ex)
            {
                // Nie blokujemy zapisu, gdy sama kontrola sie wywroci — zostaje straznik startu.
                Tracing.Tracer.LogError($"[SchemaGuard] Kontrola zmiany typu nie zadzialala: {ex.Message}");
                return true;
            }
        }
    }
}
