using System.Text;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using XafXPODynAssem.Module.BusinessObjects;

namespace XafXPODynAssem.Module.Services
{
    public class CustomClassSummary
    {
        public string ClassName { get; set; }
        public int FieldCount { get; set; }
        public CustomClassStatus Status { get; set; }
        public bool IsDeployed { get; set; }
    }

    public class SchemaInfo
    {
        public List<string> CompiledEntities { get; set; } = new();
    }

    public class SchemaDiscoveryService
    {
        private static readonly HashSet<string> MetadataTypeNames = new()
        {
            nameof(CustomClass),
            nameof(CustomField),
            nameof(SchemaHistory),
            "ApplicationUser",
            "ApplicationUserLoginInfo",
        };

        private readonly object _cacheLock = new();
        private SchemaInfo _cachedSchema;

        public void InvalidateCache()
        {
            lock (_cacheLock)
            {
                _cachedSchema = null;
            }
        }

        public SchemaInfo GetSchema()
        {
            lock (_cacheLock)
            {
                if (_cachedSchema != null)
                    return _cachedSchema;

                _cachedSchema = DiscoverSchema();
                return _cachedSchema;
            }
        }

        public List<string> GetCompiledEntityNames()
        {
            return GetSchema().CompiledEntities;
        }

        public string BuildSystemPrompt(IObjectSpace os)
        {
            var schema = GetSchema();

            // Query current runtime entities from metadata
            var runtimeEntities = new List<CustomClassSummary>();
            var customClasses = os.GetObjectsQuery<CustomClass>().ToList();
            foreach (var cc in customClasses)
            {
                runtimeEntities.Add(new CustomClassSummary
                {
                    ClassName = cc.ClassName,
                    FieldCount = cc.Fields.Count,
                    Status = cc.Status,
                    IsDeployed = cc.Status == CustomClassStatus.Runtime
                        && XafXPODynAssemModule.AssemblyManager.RuntimeTypes
                            .Any(t => t.Name == cc.ClassName),
                });
            }

            return GenerateSystemPrompt(runtimeEntities, schema);
        }

        public string GenerateSystemPrompt(List<CustomClassSummary> runtimeEntities)
        {
            var schema = GetSchema();
            return GenerateSystemPrompt(runtimeEntities, schema);
        }

        private static string GenerateSystemPrompt(List<CustomClassSummary> runtimeEntities, SchemaInfo schema)
        {
            var sb = new StringBuilder();

            // Role
            sb.AppendLine("You are a schema design assistant for an XAF application with runtime entity support.");
            sb.AppendLine("You help users create, modify, and manage business object types and their fields at runtime.");
            sb.AppendLine();

            // Rules
            sb.AppendLine("## Rules");
            sb.AppendLine("- Always confirm with the user before executing any schema change (create, modify, delete).");
            sb.AppendLine("- After making changes, remind the user to Deploy so changes take effect.");
            sb.AppendLine("- Infer appropriate field types from natural language descriptions (e.g., \"price\" -> System.Decimal, \"active\" -> System.Boolean).");
            sb.AppendLine("- Use PascalCase for class names and field names.");
            sb.AppendLine("- Class names must be valid C# identifiers and cannot be C# keywords or reserved type names.");
            sb.AppendLine("- Field names must be valid C# identifiers and cannot be reserved (Oid, ObjectType, GCRecord, OptimisticLockField).");
            sb.AppendLine();

            // Raporty
            sb.AppendLine("## Reports");
            sb.AppendLine("- When the user dictates a report or document (invoice, order, protocol), NEVER guess the parts they did not specify.");
            sb.AppendLine("- **Decide the branch FIRST. If the words invoice / faktura / rachunek / bill / order confirmation appear, or the user names an invoice entity, this is the INVOICE branch: go straight to `build_invoice_report`. Do NOT call `validate_report_spec`, `build_report` or `preview_report` — they are for plain list reports and they will send you down the wrong path.**");
            sb.AppendLine("- For `build_invoice_report`, `entityName` is the INVOICE HEADER entity — the one where one record is one invoice, e.g. 'Faktura'. If the user names that entity, use it; do NOT translate it to the line-item entity. The tool finds the line-item entity by itself (the runtime entity that references the header) and reports which one it picked.");
            sb.AppendLine("- Header slots (CustomerName, InvoiceNumber, InvoiceDate, InvoiceDueDate, Vendor*, Subtotal, TaxTotal, Total) take paths relative to the HEADER entity, e.g. 'InvoiceNumber=NumerFaktury;CustomerName=Customer.NazwaKlienta'. Line-item slots (ProductName, Quantity, UnitPrice, LineTotal, Tax, ...) take paths relative to the LINE ITEM entity, e.g. 'ProductName=OpisPozycji;Ilosc'. Seller details the user typed go into `literals`, e.g. 'VendorName=Moja Firma;VendorCity=Katowice'.");
            sb.AppendLine("- Add the VAT-rate summary table by passing all four of `vatRateField`, `vatNetField`, `vatAmountField`, `vatGrossField` — paths on the LINE ITEM entity, e.g. 'StawkaVat.SymbolStawki', 'WartoscNetto', 'WartoscVat', 'WartoscBrutto'. That table is the only place totals appear, so do not map Subtotal/TaxTotal/Total when you use it.");
            sb.AppendLine("- The resulting report is saved with the HEADER entity as its data type, so it shows up in the „Pokaż na raporcie” action on the invoice views and can be printed from a single invoice record. Passing the line-item entity still works (the tool walks up the reference), but always prefer the header entity.");
            sb.AppendLine("- LIST-REPORT branch (everything that is not a document with line items): call `validate_report_spec` first. If it returns a line starting with MISSING, ask the user ONE specific question about that item and wait for the answer. Do not fill the gap with a default.");
            sb.AppendLine("- Typical gaps worth asking about: which entity the rows come from, which fields become columns, which field holds the date, whether an amount should be net or gross, which field separates one document from the next.");
            sb.AppendLine("- Only call `build_report` once the spec has no MISSING items left. To show the user how it looks, call `preview_report` with render=true, a documentKeyField and headerLines — it returns image files, so the user does not have to open the report designer.");
            sb.AppendLine();

            // Supported field types
            sb.AppendLine("## Supported Field Types");
            foreach (var typeName in SupportedTypes.AllTypeNames)
            {
                sb.AppendLine($"- {typeName}");
            }
            sb.AppendLine();

            // Runtime entities (metadata)
            sb.AppendLine("## Runtime Entities (Metadata-Defined)");
            if (runtimeEntities.Count == 0)
            {
                sb.AppendLine("No runtime entities defined yet.");
            }
            else
            {
                foreach (var entity in runtimeEntities)
                {
                    var deployed = entity.IsDeployed ? "deployed" : "not deployed";
                    sb.AppendLine($"- **{entity.ClassName}**: {entity.FieldCount} fields, status={entity.Status}, {deployed}");
                }
            }
            sb.AppendLine();

            // Compiled entities (available for references)
            sb.AppendLine("## Compiled Entities (Available for References)");
            if (schema.CompiledEntities.Count == 0)
            {
                sb.AppendLine("No compiled entities discovered.");
            }
            else
            {
                foreach (var name in schema.CompiledEntities.OrderBy(n => n))
                {
                    sb.AppendLine($"- {name}");
                }
            }

            return sb.ToString();
        }

        private SchemaInfo DiscoverSchema()
        {
            var info = new SchemaInfo();

            var runtimeTypeNames = new HashSet<string>(
                XafXPODynAssemModule.AssemblyManager.RuntimeTypes.Select(t => t.Name));

            try
            {
                foreach (var typeInfo in XafTypesInfo.Instance.PersistentTypes)
                {
                    if (typeInfo.Type == null)
                        continue;

                    var name = typeInfo.Name;
                    var ns = typeInfo.Type.Namespace ?? "";

                    // Skip DevExpress internal types
                    if (ns.StartsWith("DevExpress", StringComparison.Ordinal))
                        continue;

                    // Skip runtime entities (already tracked via CustomClass metadata)
                    if (runtimeTypeNames.Contains(name))
                        continue;

                    // Skip metadata types
                    if (MetadataTypeNames.Contains(name))
                        continue;

                    info.CompiledEntities.Add(name);
                }
            }
            catch (InvalidOperationException)
            {
                // XafTypesInfo not yet initialized — return empty
            }

            return info;
        }
    }
}
