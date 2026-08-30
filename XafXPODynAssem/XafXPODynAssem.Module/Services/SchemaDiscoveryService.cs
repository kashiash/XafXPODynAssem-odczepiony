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

            // Straznik schematu — zasada ustalona po awarii produkcyjnej.
            sb.AppendLine("## Changing field types — HARD RULE");
            sb.AppendLine("- **NEVER change the type of an existing field. ADD A NEW FIELD instead.** When the user asks to change a field's type (e.g. \"zmien StawkaVAT z liczby na referencje do StawkaVat\", \"niech Ilosc bedzie tekstem\"), do NOT send that field in `updateFields` with a new `type`. The tool refuses it anyway.");
            sb.AppendLine("- Propose this instead, in the user's language, and wait for confirmation: add a NEW field next to the old one (e.g. `StawkaVATRef` as a Reference to `StawkaVat`), Deploy, move the data over, then hide the old field by setting `IsVisibleInListView` and `IsVisibleInDetailView` to false. Never suggest deleting the old field.");
            sb.AppendLine("- Explain WHY, briefly and concretely: the column in PostgreSQL already has an SQL type matched to the old field type. After a metadata-only type change XPO tries on EVERY startup to rework that column (for a reference: to put a foreign key on it). PostgreSQL refuses with error 42804, the schema update blows up during XAF warm-up, the process dies and the application ends up in a restart loop — and then it cannot be fixed from the UI any more, only by a manual UPDATE on the metadata tables in the database. This actually happened in production.");
            sb.AppendLine("- **NEVER remove a field.** `removeFields` is refused. If the user wants a field gone, offer to hide it (`IsVisibleInListView=false`, `IsVisibleInDetailView=false`) — the data and the column stay untouched.");
            sb.AppendLine("- Adding new fields is completely unaffected: keep using `modify_entity` with `addFields` as usual.");
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

            // Przeplywy (maszyny stanow)
            sb.AppendLine("## Workflows (State Machines)");
            sb.AppendLine("- A workflow describes the states a record goes through and which moves between them are allowed. Reach for these tools when the user talks about przepływ / workflow / obieg dokumentu / stany / statusy / akceptacja, or dictates a chain like \"Robocza -> Wystawiona -> Zapłacona\".");
            sb.AppendLine("- Tools: `list_workflows`, `describe_workflow`, `create_workflow`, `add_workflow_state`, `add_workflow_transition`.");
            sb.AppendLine("- **NEVER guess the parts the user did not say.** Before calling `create_workflow` you must know all four: (1) which entity, (2) which System.String field stores the state, (3) which state a brand new record starts in, (4) which transitions are allowed. If any is missing, ask ONE specific question about it and wait for the answer. Do not invent a start state and do not invent transitions the user did not describe.");
            sb.AppendLine("- Transitions are directed. `A -> B` does not also give `B -> A`. A phrase like \"z każdego stanu poza Zapłaconą można anulować\" means one transition into the cancel state from EACH of the other states — list them out explicitly and read them back to the user before writing.");
            sb.AppendLine("- A state with no outgoing transition is terminal (the Change State action shows nothing on it). That is usually intended for the last state — confirm it rather than silently adding a way back.");
            sb.AppendLine("- `create_workflow` requires the entity to be DEPLOYED and to already have a System.String field for the state. If that field is missing the tool refuses and lists the entity's existing text fields. Ask the user whether to add a new one (and under what name); only then call `create_workflow` again with `createStatePropertyIfMissing=true`. That call ADDS THE FIELD AND STOPS — adding a field is a schema change, so the user must click Deploy (the application restarts) before you call `create_workflow` one more time with the same arguments.");
            sb.AppendLine("- States and transitions themselves are DATA, not schema: creating or extending a workflow needs no Deploy and no restart. Tell the user to refresh the page (F5) and open a record — the **Change State** action on the toolbar then offers the allowed transitions.");
            sb.AppendLine("- The state field becomes read-only in the UI on purpose; the state is meant to change only through the Change State action.");
            sb.AppendLine("- To change an existing workflow call `describe_workflow` first and extend it with `add_workflow_state` / `add_workflow_transition`. Never create a second workflow for the same entity.");
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
