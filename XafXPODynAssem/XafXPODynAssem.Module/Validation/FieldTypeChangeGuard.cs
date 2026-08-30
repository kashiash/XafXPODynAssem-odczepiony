using Npgsql;
using XafXPODynAssem.Module.Services;

namespace XafXPODynAssem.Module.Validation
{
    /// <summary>
    /// Blokada zmiany typu wdrozonego pola.
    ///
    /// Powod: kolumna w bazie ma juz konkretny typ SQL. Gdy metadana zmieni sie
    /// np. z System.Decimal na Reference, XPO przy kazdym starcie probuje zalozyc
    /// klucz obcy na kolumnie numeric. PostgreSQL odrzuca to bledem 42804,
    /// UpdateSchema wybucha w trakcie rozgrzewki XAF-a i proces ginie — aplikacja
    /// wpada w petle restartow i nie da sie jej juz naprawic z UI.
    ///
    /// Zasada: dodajemy nowe pole obok, nie przerabiamy istniejacego.
    /// Zmiane typu dopuszczamy tylko wtedy, gdy da sie wykazac, ze jest bezpieczna:
    /// kolumna jeszcze nie istnieje albo nie ma w niej ani jednej niepustej wartosci.
    /// </summary>
    public static class FieldTypeChangeGuard
    {
        /// <summary>Typy kolumn PostgreSQL (information_schema.data_type) akceptowalne dla danego TypeName.</summary>
        private static readonly Dictionary<string, string[]> ExpectedColumnTypes =
            new(StringComparer.Ordinal)
            {
                ["Reference"] = new[] { "uuid" },
                ["System.Guid"] = new[] { "uuid" },
                ["System.String"] = new[] { "character varying", "text", "character" },
                ["System.Int32"] = new[] { "integer", "smallint" },
                ["System.Int64"] = new[] { "bigint", "integer" },
                ["System.Decimal"] = new[] { "numeric", "money" },
                ["System.Double"] = new[] { "double precision", "real" },
                ["System.Single"] = new[] { "real", "double precision" },
                ["System.Boolean"] = new[] { "boolean" },
                ["System.DateTime"] = new[] { "timestamp without time zone", "timestamp with time zone", "date" },
                ["System.Byte[]"] = new[] { "bytea" },
            };

        /// <summary>
        /// Komunikaty straznika ida i do sladu XAF-a, i na konsole — konsola trafia do
        /// logu petli uruchomieniowej, wiec problem widac bez wchodzenia do aplikacji.
        /// </summary>
        public static void Log(string message)
        {
            Console.Error.WriteLine($"[SchemaGuard] {message}");
            try { DevExpress.Persistent.Base.Tracing.Tracer.LogError($"[SchemaGuard] {message}"); } catch { }
        }

        /// <summary>Nazwa typu widziana przez uzytkownika (Reference -> "Referencja do X").</summary>
        public static string Describe(string typeName, string referencedClassName)
        {
            if (typeName == "Reference")
                return string.IsNullOrWhiteSpace(referencedClassName)
                    ? "Referencja"
                    : $"Referencja do {referencedClassName}";
            return typeName ?? "(brak)";
        }

        /// <summary>
        /// Czy metadana pola pasuje do kolumny, ktora juz istnieje w bazie.
        /// Nieznany typ metadanej albo nieznany typ kolumny traktujemy jako zgodne —
        /// nie chcemy blokowac czegos, czego nie rozumiemy.
        /// </summary>
        public static bool IsColumnCompatible(string typeName, string columnDataType)
        {
            if (string.IsNullOrWhiteSpace(typeName) || string.IsNullOrWhiteSpace(columnDataType))
                return true;
            if (!ExpectedColumnTypes.TryGetValue(typeName, out var accepted))
                return true;
            return accepted.Contains(columnDataType, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Czy zmiana typu pola <paramref name="className"/>.<paramref name="fieldName"/> jest bezpieczna.
        /// Zwraca true, gdy da sie to wykazac (brak kolumny albo brak danych).
        /// W <paramref name="reasonPl"/> wraca gotowy komunikat dla uzytkownika.
        /// </summary>
        public static bool IsTypeChangeSafe(
            string className,
            string fieldName,
            string oldTypeName,
            string oldReferencedClassName,
            string newTypeName,
            string newReferencedClassName,
            string connectionString,
            out string reasonPl)
        {
            reasonPl = null;

            var oldDesc = Describe(oldTypeName, oldReferencedClassName);
            var newDesc = Describe(newTypeName, newReferencedClassName);
            if (string.Equals(oldDesc, newDesc, StringComparison.Ordinal))
                return true; // nic sie nie zmienia

            // Nazwy trafiaja do zapytania SQL — bez poprawnego identyfikatora nie sprawdzamy.
            if (!CustomFieldValidation.IsValidIdentifier(className) ||
                !CustomFieldValidation.IsValidIdentifier(fieldName))
                return true; // inne reguly walidacji to wylapia

            var deployed = IsPropertyDeployed(className, fieldName);

            long rowsWithData;
            try
            {
                rowsWithData = CountRowsWithValue(className, fieldName, connectionString);
            }
            catch (Exception ex)
            {
                // Nie potrafimy wykazac, ze jest bezpiecznie. Jesli pole jest wdrozone — odmawiamy.
                if (!deployed)
                    return true;
                reasonPl = BuildRefusalMessage(className, fieldName, oldDesc, newDesc,
                    $"nie udalo sie sprawdzic danych w bazie ({ex.Message}), a pole jest juz wdrozone");
                return false;
            }

            if (rowsWithData < 0)
                return true; // tabela albo kolumna jeszcze nie istnieje — XPO dolozy ja od zera
            if (rowsWithData == 0 && !deployed)
                return true; // brak danych i pole niewdrozone

            if (rowsWithData == 0)
            {
                // Kolumna istnieje i jest pusta, ale typ juz zyje w aplikacji.
                // XPO nie przerabia typu istniejacej kolumny — FK/ALTER i tak polegnie.
                reasonPl = BuildRefusalMessage(className, fieldName, oldDesc, newDesc,
                    "kolumna jest juz wdrozona w bazie (choc pusta), a XPO nie potrafi zmienic typu istniejacej kolumny");
                return false;
            }

            reasonPl = BuildRefusalMessage(className, fieldName, oldDesc, newDesc,
                $"w kolumnie sa dane ({rowsWithData} wierszy z wartoscia)");
            return false;
        }

        private static string BuildRefusalMessage(
            string className, string fieldName, string oldDesc, string newDesc, string why)
        {
            var suggested = SuggestNewFieldName(fieldName, newDesc);
            return
                $"ODMOWA: nie zmieniam typu pola {className}.{fieldName} " +
                $"z „{oldDesc}” na „{newDesc}”.\n" +
                $"Dlaczego: {why}. Kolumna „{fieldName}” w tabeli „{className}” ma juz typ SQL " +
                $"dopasowany do „{oldDesc}”. Po takiej zmianie XPO przy KAZDYM starcie probowalby " +
                $"przerobic te kolumne (przy referencji — zalozyc na niej klucz obcy). PostgreSQL " +
                $"odmawia bledem 42804, aktualizacja schematu wybucha w trakcie rozgrzewki i proces " +
                $"ginie. Aplikacja wpada w petle restartow i NIE DA SIE jej juz naprawic z poziomu UI — " +
                $"trzeba recznego UPDATE na metadanych w bazie.\n" +
                $"Co zrobic zamiast tego: DODAJ NOWE POLE obok, np. „{suggested}” typu „{newDesc}”, " +
                $"przenies do niego dane (recznie albo importem), a stare pole „{fieldName}” zostaw " +
                $"i tylko ukryj (IsVisibleInListView = false, IsVisibleInDetailView = false). " +
                $"Zasada w tej aplikacji: dodajemy nowe pola zamiast zmieniac typ istniejacych.";
        }

        private static string SuggestNewFieldName(string fieldName, string newDesc)
        {
            if (newDesc.StartsWith("Referencja", StringComparison.Ordinal))
                return fieldName + "Ref";
            return fieldName + "Nowe";
        }

        /// <summary>Komunikat odmowy usuniecia pola.</summary>
        public static string BuildFieldRemovalRefusal(string className, string fieldName)
        {
            return
                $"ODMOWA: nie usuwam pola {className}.{fieldName}.\n" +
                $"Dlaczego: usuniecie pola z metadanych kasuje wlasciwosc z klasy runtime, ale kolumna " +
                $"z danymi zostaje w bazie i wypada spod kontroli aplikacji. Raporty, przeplywy i widoki, " +
                $"ktore sie do niej odwoluja, przestaja dzialac. W tej aplikacji pol NIE USUWAMY.\n" +
                $"Co zrobic zamiast tego: ukryj pole — ustaw „Widoczne na liscie” i „Widoczne w szczegolach” " +
                $"na Nie (IsVisibleInListView = false, IsVisibleInDetailView = false). Pole znika z interfejsu, " +
                $"a dane i schemat zostaja nietkniete.";
        }

        /// <summary>
        /// Czy usuniecie pola jest bezpieczne — tylko wtedy, gdy pole nie zostalo jeszcze
        /// wdrozone i nie ma po nim kolumny w bazie. Wszystko inne odmawiamy: kolumna z danymi
        /// zostalaby w bazie poza kontrola aplikacji, a raporty i przeplywy przestalyby dzialac.
        /// </summary>
        public static bool IsFieldRemovalSafe(string className, string fieldName, string connectionString)
        {
            if (!CustomFieldValidation.IsValidIdentifier(className) ||
                !CustomFieldValidation.IsValidIdentifier(fieldName))
                return true; // nowy, jeszcze niedokonczony rekord

            if (IsPropertyDeployed(className, fieldName))
                return false;

            try
            {
                return CountRowsWithValue(className, fieldName, connectionString) < 0; // brak kolumny
            }
            catch
            {
                return false; // nie potrafimy wykazac bezpieczenstwa — odmawiamy
            }
        }

        /// <summary>
        /// Czy skompilowana klasa runtime ma juz taka wlasciwosc (czyli pole jest wdrozone).
        /// </summary>
        public static bool IsPropertyDeployed(string className, string fieldName)
        {
            try
            {
                var type = XafXPODynAssemModule.AssemblyManager.RuntimeTypes
                    .FirstOrDefault(t => t.Name == className);
                return type?.GetProperty(fieldName) != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Typ kolumny w bazie (information_schema.data_type) albo null, gdy kolumny nie ma.
        /// </summary>
        public static string GetColumnDataType(string className, string fieldName, string connectionString)
        {
            if (!CustomFieldValidation.IsValidIdentifier(className) ||
                !CustomFieldValidation.IsValidIdentifier(fieldName))
                return null;

            var connStr = connectionString ?? XafXPODynAssemModule.RuntimeConnectionString;
            if (string.IsNullOrWhiteSpace(connStr)) return null;

            using var conn = new NpgsqlConnection(XafXPODynAssemModule.StripXpoProvider(connStr));
            conn.Open();
            using var cmd = new NpgsqlCommand(
                @"SELECT data_type FROM information_schema.columns
                  WHERE table_schema = current_schema() AND table_name = @t AND column_name = @c", conn);
            cmd.Parameters.AddWithValue("t", className);
            cmd.Parameters.AddWithValue("c", fieldName);
            return cmd.ExecuteScalar() as string;
        }

        /// <summary>
        /// Liczba wierszy z niepusta wartoscia w kolumnie.
        /// -1 oznacza, ze tabela albo kolumna jeszcze nie istnieje.
        /// </summary>
        private static long CountRowsWithValue(string className, string fieldName, string connectionString)
        {
            var connStr = connectionString ?? XafXPODynAssemModule.RuntimeConnectionString;
            if (string.IsNullOrWhiteSpace(connStr))
                throw new InvalidOperationException("brak connection stringa");

            using var conn = new NpgsqlConnection(XafXPODynAssemModule.StripXpoProvider(connStr));
            conn.Open();

            using (var check = new NpgsqlCommand(
                @"SELECT count(*) FROM information_schema.columns
                  WHERE table_schema = current_schema()
                    AND table_name = @t AND column_name = @c", conn))
            {
                check.Parameters.AddWithValue("t", className);
                check.Parameters.AddWithValue("c", fieldName);
                if (Convert.ToInt64(check.ExecuteScalar()) == 0)
                    return -1;
            }

            // Nazwy sa juz zweryfikowane jako identyfikatory C#, wiec bezpieczne w cudzyslowie.
            using var cmd = new NpgsqlCommand(
                $@"SELECT count(*) FROM ""{className}"" WHERE ""{fieldName}"" IS NOT NULL", conn);
            return Convert.ToInt64(cmd.ExecuteScalar());
        }

        /// <summary>
        /// Zabezpieczenie startu: wyrzuca z metadanych pola, ktorych nie da sie bezpiecznie
        /// zmigrowac (metadana mowi co innego niz typ kolumny w bazie). Dzieki temu Roslyn
        /// nie wygeneruje takiej wlasciwosci, a UpdateSchema nie sprobuje przerabiac kolumny.
        /// Zwraca liste komunikatow o pominietych polach.
        /// </summary>
        public static List<string> SanitizeMetadata(List<RuntimeClassMetadata> classes, NpgsqlConnection conn)
        {
            var problems = new List<string>();
            if (classes == null || classes.Count == 0) return problems;

            // Jedno zapytanie o wszystkie kolumny wszystkich tabel runtime.
            var tableNames = classes.Select(c => c.ClassName).Where(n => !string.IsNullOrWhiteSpace(n)).ToArray();
            if (tableNames.Length == 0) return problems;

            var columnTypes = new Dictionary<(string Table, string Column), string>();
            using (var cmd = new NpgsqlCommand(
                @"SELECT table_name, column_name, data_type FROM information_schema.columns
                  WHERE table_schema = current_schema() AND table_name = ANY(@tables)", conn))
            {
                cmd.Parameters.AddWithValue("tables", tableNames);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    columnTypes[(reader.GetString(0), reader.GetString(1))] = reader.GetString(2);
            }

            foreach (var cc in classes)
            {
                foreach (var field in cc.Fields.ToList())
                {
                    if (!columnTypes.TryGetValue((cc.ClassName, field.FieldName), out var dataType))
                        continue; // kolumny nie ma — XPO ja dolozy, to normalne dodanie pola

                    if (IsColumnCompatible(field.TypeName, dataType))
                        continue;

                    cc.Fields.Remove(field);
                    problems.Add(
                        $"{cc.ClassName}.{field.FieldName}: metadana mowi „{Describe(field.TypeName, field.ReferencedClassName)}”, " +
                        $"a kolumna w bazie jest typu „{dataType}”. Pole POMINIETE przy budowie klasy runtime, " +
                        $"zeby aktualizacja schematu nie wywrocila startu aplikacji. " +
                        $"Napraw metadana (przywroc poprzedni typ) albo dodaj nowe pole obok i przenies dane.");
                }
            }

            return problems;
        }
    }
}
