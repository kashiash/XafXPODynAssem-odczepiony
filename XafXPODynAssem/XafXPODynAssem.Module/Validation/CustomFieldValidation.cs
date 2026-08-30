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

                var connStr = XafXPODynAssemModule.RuntimeConnectionString;
                var columnType = FieldTypeChangeGuard.GetColumnDataType(className, field.FieldName, connStr);
                if (columnType == null)
                    return true; // kolumny jeszcze nie ma — zwykle dodanie pola, przepuszczamy

                var fkTarget = FieldTypeChangeGuard.GetForeignKeyTarget(className, field.FieldName, connStr);

                var mismatch = FieldTypeChangeGuard.FindDatabaseMismatch(
                    className, field.FieldName, field.TypeName, field.ReferencedClassName,
                    columnType, fkTarget,
                    () => FieldTypeChangeGuard.HasAnyValue(className, field.FieldName, connStr));

                if (mismatch == null)
                    return true;

                FieldTypeChangeGuard.Log($"Zablokowany zapis: {className}.{field.FieldName} — {mismatch}.");
                return false;
            }
            catch (Exception ex)
            {
                // Nie blokujemy zapisu, gdy sama kontrola sie wywroci — zostaje straznik startu.
                FieldTypeChangeGuard.Log($"Kontrola zmiany typu nie zadzialala: {ex.Message}");
                return true;
            }
        }
    }
}
