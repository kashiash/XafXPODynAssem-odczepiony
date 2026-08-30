using System.ComponentModel;
using System.Text;
using System.Text.Json;
using DevExpress.Data.Filtering;
using DevExpress.ExpressApp;
using DevExpress.Persistent.Base.ReportsV2;
using DevExpress.XtraReports.UI;
using DevExpress.XtraReports.Wizards;
using DevExpress.XtraReports.Wizards.Templates;
using LlmTornado.Chat;
using LlmTornado.Common;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XafXPODynAssem.Module.BusinessObjects;

namespace XafXPODynAssem.Module.Services;

/// <summary>
/// Provides AI tool functions for schema management — listing, creating, modifying,
/// and deleting runtime entities, plus role permission management.
/// </summary>
public sealed class SchemaAIToolsProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SchemaDiscoveryService _discoveryService;
    private readonly ILogger<SchemaAIToolsProvider> _logger;
    private List<AIFunction> _tools;

    public SchemaAIToolsProvider(IServiceProvider serviceProvider, SchemaDiscoveryService discoveryService)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _discoveryService = discoveryService ?? throw new ArgumentNullException(nameof(discoveryService));
        _logger = serviceProvider.GetRequiredService<ILogger<SchemaAIToolsProvider>>();
    }

    public IReadOnlyList<AIFunction> Tools => _tools ??= CreateTools();

    private List<AIFunction> CreateTools()
    {
        return new List<AIFunction>
        {
            // Read tools
            AIFunctionFactory.Create(ListEntities, "list_entities"),
            AIFunctionFactory.Create(DescribeEntity, "describe_entity"),
            AIFunctionFactory.Create(GetActiveSchema, "get_active_schema"),
            AIFunctionFactory.Create(GetPendingChanges, "get_pending_changes"),
            AIFunctionFactory.Create(ValidateSchema, "validate_schema"),
            // Write tools
            AIFunctionFactory.Create(CreateEntity, "create_entity"),
            AIFunctionFactory.Create(ModifyEntity, "modify_entity"),
            AIFunctionFactory.Create(DeleteEntity, "delete_entity"),
            // Role tools
            AIFunctionFactory.Create(ListRoles, "list_roles"),
            AIFunctionFactory.Create(SetRolePermissions, "set_role_permissions"),
            // Report tools
            AIFunctionFactory.Create(ValidateReportSpec, "validate_report_spec"),
            AIFunctionFactory.Create(BuildReport, "build_report"),
            AIFunctionFactory.Create(PreviewReport, "preview_report"),
            AIFunctionFactory.Create(BuildInvoiceReport, "build_invoice_report"),
        };
    }

    /// <summary>
    /// Converts AIFunction definitions to LLMTornado Tool format for sending to the LLM.
    /// </summary>
    public IReadOnlyList<Tool> GetTornadoTools()
    {
        var tornadoTools = new List<Tool>();
        foreach (var fn in Tools)
        {
            var toolFunction = new ToolFunction(fn.Name, fn.Description, fn.JsonSchema);
            tornadoTools.Add(new Tool(toolFunction));
        }
        return tornadoTools;
    }

    // -- Helpers ---------------------------------------------------------------

    private sealed class ScopedObjectSpace : IDisposable
    {
        public IObjectSpace Os { get; }
        private readonly IServiceScope _scope;

        /// <summary>Dostawca uslug TEGO zakresu. ReportDataProvider.GetReportStorage() rozwiazuje
        /// IReportStorage, ktory w XAF jest scoped — z roota rzucilby wyjatek o zasiegu.</summary>
        public IServiceProvider ServiceProvider => _scope.ServiceProvider;

        public ScopedObjectSpace(IObjectSpace os, IServiceScope scope)
        {
            Os = os;
            _scope = scope;
        }

        public void Dispose()
        {
            Os.Dispose();
            _scope?.Dispose();
        }
    }

    private ScopedObjectSpace CreateObjectSpace()
    {
        var scope = _serviceProvider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<INonSecuredObjectSpaceFactory>();
        var os = factory.CreateNonSecuredObjectSpace<CustomClass>();
        return new ScopedObjectSpace(os, scope);
    }

    private ScopedObjectSpace CreateObjectSpaceForType(Type type)
    {
        var scope = _serviceProvider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<INonSecuredObjectSpaceFactory>();
        var os = factory.CreateNonSecuredObjectSpace(type);
        return new ScopedObjectSpace(os, scope);
    }

    /// <summary>
    /// Converts a CustomClass (XPO persistent object) to RuntimeClassMetadata for compilation.
    /// </summary>
    private static RuntimeClassMetadata ToMetadata(CustomClass cc)
    {
        var meta = new RuntimeClassMetadata
        {
            ClassName = cc.ClassName,
            NavigationGroup = cc.NavigationGroup,
            Description = cc.Description,
            IsApiExposed = cc.IsApiExposed,
        };

        foreach (var f in cc.Fields.Cast<CustomField>().OrderBy(f => f.SortOrder).ThenBy(f => f.FieldName))
        {
            meta.Fields.Add(new RuntimeFieldMetadata
            {
                FieldName = f.FieldName,
                TypeName = f.TypeName ?? "System.String",
                IsRequired = f.IsRequired,
                IsDefaultField = f.IsDefaultField,
                Description = f.Description,
                ReferencedClassName = f.ReferencedClassName,
                SortOrder = f.SortOrder,
                IsImmediatePostData = f.IsImmediatePostData,
                StringMaxLength = f.StringMaxLength,
                IsVisibleInListView = f.IsVisibleInListView,
                IsVisibleInDetailView = f.IsVisibleInDetailView,
                IsEditable = f.IsEditable,
                ToolTip = f.ToolTip,
                DisplayName = f.DisplayName,
            });
        }

        return meta;
    }

    // ==========================================================================
    // READ TOOLS
    // ==========================================================================

    [Description("List all runtime entities (CustomClasses) with their field count, status, and API exposure. Returns a markdown table.")]
    private string ListEntities()
    {
        _logger.LogInformation("[Tool:list_entities] Called");
        try
        {
            using var scope = CreateObjectSpace();
            var classes = scope.Os.GetObjectsQuery<CustomClass>()
                .OrderBy(c => c.ClassName)
                .ToList();

            if (classes.Count == 0)
                return "No runtime entities defined yet. Use `create_entity` to create one.";

            var sb = new StringBuilder();
            sb.AppendLine("| Class Name | Fields | Status | API Exposed |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var cc in classes)
            {
                var fieldCount = cc.Fields?.Cast<CustomField>().Count() ?? 0;
                sb.AppendLine($"| {cc.ClassName} | {fieldCount} | {cc.Status} | {(cc.IsApiExposed ? "Yes" : "No")} |");
            }
            sb.AppendLine();
            sb.AppendLine($"Total: {classes.Count} entities");

            var result = sb.ToString();
            _logger.LogInformation("[Tool:list_entities] Returning {Count} entities", classes.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Tool:list_entities] Error");
            return $"Error listing entities: {ex.Message}";
        }
    }

    [Description("Get full details for a single runtime entity — all fields with types, required flags, references, and descriptions.")]
    private string DescribeEntity(
        [Description("The class name of the entity to describe (e.g. 'Employee', 'Product').")] string entityName)
    {
        _logger.LogInformation("[Tool:describe_entity] Called with entity={Entity}", entityName);
        try
        {
            if (string.IsNullOrWhiteSpace(entityName))
                return "Error: entityName parameter is required.";

            using var scope = CreateObjectSpace();
            var cc = scope.Os.GetObjectsQuery<CustomClass>()
                .FirstOrDefault(c => c.ClassName == entityName);

            if (cc == null)
            {
                var available = string.Join(", ", scope.Os.GetObjectsQuery<CustomClass>()
                    .Select(c => c.ClassName).OrderBy(n => n));
                return $"Entity '{entityName}' not found. Available entities: {(string.IsNullOrEmpty(available) ? "none" : available)}";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"## {cc.ClassName}");
            if (!string.IsNullOrWhiteSpace(cc.Description))
                sb.AppendLine(cc.Description);
            sb.AppendLine();
            sb.AppendLine($"- **Status:** {cc.Status}");
            sb.AppendLine($"- **Navigation Group:** {cc.NavigationGroup ?? "(none)"}");
            sb.AppendLine($"- **API Exposed:** {(cc.IsApiExposed ? "Yes" : "No")}");
            sb.AppendLine();

            var fields = cc.Fields?.Cast<CustomField>().OrderBy(f => f.SortOrder).ThenBy(f => f.FieldName).ToList();
            if (fields == null || fields.Count == 0)
            {
                sb.AppendLine("No fields defined.");
            }
            else
            {
                sb.AppendLine("| Field Name | Type | Required | Default | Reference | Description |");
                sb.AppendLine("|---|---|---|---|---|---|");
                foreach (var f in fields)
                {
                    var typeName = !string.IsNullOrWhiteSpace(f.ReferencedClassName)
                        ? $"Reference({f.ReferencedClassName})"
                        : (f.TypeName ?? "System.String");
                    var required = f.IsRequired ? "Yes" : "No";
                    var isDefault = f.IsDefaultField ? "Yes" : "";
                    var refClass = f.ReferencedClassName ?? "";
                    var desc = f.Description ?? "";
                    sb.AppendLine($"| {f.FieldName} | {typeName} | {required} | {isDefault} | {refClass} | {desc} |");
                }
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Tool:describe_entity] Error");
            return $"Error describing entity: {ex.Message}";
        }
    }

    [Description("Show the currently live runtime types (loaded in memory) and compiled entities. Useful to see what is actually deployed vs. what is only in metadata.")]
    private string GetActiveSchema()
    {
        _logger.LogInformation("[Tool:get_active_schema] Called");
        try
        {
            var sb = new StringBuilder();

            // Runtime types currently loaded
            var runtimeTypes = XafXPODynAssemModule.AssemblyManager.RuntimeTypes;
            sb.AppendLine("## Live Runtime Types");
            if (runtimeTypes.Length == 0)
            {
                sb.AppendLine("No runtime types currently loaded.");
            }
            else
            {
                foreach (var type in runtimeTypes.OrderBy(t => t.Name))
                {
                    var props = type.GetProperties()
                        .Where(p => p.DeclaringType == type)
                        .Select(p => $"{p.Name}: {p.PropertyType.Name}");
                    sb.AppendLine($"- **{type.Name}**: {string.Join(", ", props)}");
                }
            }
            sb.AppendLine();

            // Compiled entities from SchemaDiscoveryService
            var schema = _discoveryService.GetSchema();
            sb.AppendLine("## Compiled Entities");
            if (schema.CompiledEntities.Count == 0)
            {
                sb.AppendLine("No compiled entities discovered.");
            }
            else
            {
                foreach (var name in schema.CompiledEntities.OrderBy(n => n))
                    sb.AppendLine($"- {name}");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Tool:get_active_schema] Error");
            return $"Error getting active schema: {ex.Message}";
        }
    }

    [Description("Compare metadata (CustomClass definitions) against the currently live runtime types to show what changes are pending deployment.")]
    private string GetPendingChanges()
    {
        _logger.LogInformation("[Tool:get_pending_changes] Called");
        try
        {
            using var scope = CreateObjectSpace();
            var metadataClasses = scope.Os.GetObjectsQuery<CustomClass>()
                .Where(c => c.Status == CustomClassStatus.Runtime)
                .ToList();

            var liveTypes = XafXPODynAssemModule.AssemblyManager.RuntimeTypes;
            var liveTypeNames = new HashSet<string>(liveTypes.Select(t => t.Name));
            var metadataNames = new HashSet<string>(metadataClasses.Select(c => c.ClassName));

            var newEntities = metadataNames.Except(liveTypeNames).OrderBy(n => n).ToList();
            var removedEntities = liveTypeNames.Except(metadataNames).OrderBy(n => n).ToList();
            var existingEntities = metadataNames.Intersect(liveTypeNames).OrderBy(n => n).ToList();

            var sb = new StringBuilder();

            if (newEntities.Count == 0 && removedEntities.Count == 0 && existingEntities.Count == 0)
            {
                sb.AppendLine("No runtime entities in metadata and no live runtime types. Nothing pending.");
                return sb.ToString();
            }

            if (newEntities.Count > 0)
            {
                sb.AppendLine("## New (will be created on Deploy)");
                foreach (var name in newEntities)
                    sb.AppendLine($"- {name}");
                sb.AppendLine();
            }

            if (removedEntities.Count > 0)
            {
                sb.AppendLine("## Removed (live but no longer in metadata)");
                foreach (var name in removedEntities)
                    sb.AppendLine($"- {name}");
                sb.AppendLine();
            }

            if (existingEntities.Count > 0)
            {
                sb.AppendLine("## Existing (may have field changes)");
                foreach (var name in existingEntities)
                {
                    var cc = metadataClasses.First(c => c.ClassName == name);
                    var liveType = liveTypes.First(t => t.Name == name);
                    var liveProps = new HashSet<string>(
                        liveType.GetProperties()
                            .Where(p => p.DeclaringType == liveType)
                            .Select(p => p.Name));
                    var metaFields = new HashSet<string>(
                        cc.Fields?.Cast<CustomField>().Select(f => f.FieldName) ?? Enumerable.Empty<string>());

                    var addedFields = metaFields.Except(liveProps).ToList();
                    var removedFields = liveProps.Except(metaFields).ToList();

                    if (addedFields.Count > 0 || removedFields.Count > 0)
                    {
                        sb.AppendLine($"- **{name}**: ");
                        if (addedFields.Count > 0)
                            sb.AppendLine($"  - Added fields: {string.Join(", ", addedFields)}");
                        if (removedFields.Count > 0)
                            sb.AppendLine($"  - Removed fields: {string.Join(", ", removedFields)}");
                    }
                    else
                    {
                        sb.AppendLine($"- **{name}**: no field changes detected");
                    }
                }
                sb.AppendLine();
            }

            if (newEntities.Count == 0 && removedEntities.Count == 0)
                sb.AppendLine("_No structural changes detected. Deploy will still recompile and restart._");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Tool:get_pending_changes] Error");
            return $"Error checking pending changes: {ex.Message}";
        }
    }

    [Description("Validate the current schema by running a test compilation via Roslyn. Reports any compilation errors or warnings without actually deploying.")]
    private string ValidateSchema()
    {
        _logger.LogInformation("[Tool:validate_schema] Called");
        try
        {
            using var scope = CreateObjectSpace();
            var classes = scope.Os.GetObjectsQuery<CustomClass>()
                .Where(c => c.Status == CustomClassStatus.Runtime)
                .ToList();

            if (classes.Count == 0)
                return "No runtime entities to validate. Create some entities first.";

            // Convert to RuntimeClassMetadata for the compiler
            var metadata = classes.Select(ToMetadata).ToList();
            var result = RuntimeAssemblyBuilder.ValidateCompilation(metadata);

            var sb = new StringBuilder();
            if (result.Success)
            {
                sb.AppendLine("Compilation **successful**.");
                sb.AppendLine($"- {classes.Count} class(es) compiled");
                if (result.Warnings.Count > 0)
                {
                    sb.AppendLine($"- {result.Warnings.Count} warning(s):");
                    foreach (var w in result.Warnings.Take(10))
                        sb.AppendLine($"  - {w}");
                }
                else
                {
                    sb.AppendLine("- No warnings");
                }
            }
            else
            {
                sb.AppendLine("Compilation **failed**.");
                sb.AppendLine($"- {result.Errors.Count} error(s):");
                foreach (var e in result.Errors.Take(20))
                    sb.AppendLine($"  - {e}");
                if (result.Warnings.Count > 0)
                {
                    sb.AppendLine($"- {result.Warnings.Count} warning(s):");
                    foreach (var w in result.Warnings.Take(10))
                        sb.AppendLine($"  - {w}");
                }
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Tool:validate_schema] Error");
            return $"Error validating schema: {ex.Message}";
        }
    }

    // ==========================================================================
    // WRITE TOOLS
    // ==========================================================================

    [Description("Create a new runtime entity (CustomClass) with fields. After creation, call validate_schema to check for errors, then the user must Deploy to make it live.")]
    private string CreateEntity(
        [Description("PascalCase class name for the entity (e.g. 'Employee', 'ProductCategory').")] string className,
        [Description("XAF navigation group name (e.g. 'HR', 'Inventory'). Optional.")] string navigationGroup,
        [Description("Description of what this entity represents. Optional.")] string description,
        [Description("JSON array of field definitions. Each object: {\"name\": \"FieldName\", \"type\": \"System.String\", \"required\": false, \"referencedClass\": null, \"description\": \"...\"}. Type defaults to System.String if omitted.")] string fieldsJson)
    {
        _logger.LogInformation("[Tool:create_entity] Creating {Name}", className);
        try
        {
            if (string.IsNullOrWhiteSpace(className))
                return "Error: className is required.";

            using var scope = CreateObjectSpace();
            var existing = scope.Os.GetObjectsQuery<CustomClass>()
                .FirstOrDefault(c => c.ClassName == className);
            if (existing != null)
                return $"Error: Entity '{className}' already exists. Use modify_entity to change it.";

            var cc = scope.Os.CreateObject<CustomClass>();
            cc.ClassName = className;
            cc.NavigationGroup = navigationGroup;
            cc.Description = description;
            cc.Status = CustomClassStatus.Runtime;

            if (!string.IsNullOrWhiteSpace(fieldsJson))
            {
                var fieldDefs = JsonSerializer.Deserialize<List<FieldDefinition>>(fieldsJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (fieldDefs != null)
                {
                    int sortOrder = 0;
                    foreach (var fd in fieldDefs)
                    {
                        var field = scope.Os.CreateObject<CustomField>();
                        field.CustomClass = cc;
                        field.FieldName = fd.Name;
                        field.TypeName = string.IsNullOrWhiteSpace(fd.ReferencedClass)
                            ? (fd.Type ?? "System.String")
                            : "Reference";
                        field.IsRequired = fd.Required;
                        field.ReferencedClassName = fd.ReferencedClass;
                        field.Description = fd.Description;
                        field.SortOrder = sortOrder++;
                        if (sortOrder == 1)
                            field.IsDefaultField = true;
                    }
                }
            }

            scope.Os.CommitChanges();

            var fieldCount = cc.Fields?.Cast<CustomField>().Count() ?? 0;
            _logger.LogInformation("[Tool:create_entity] Created {Name} with {Fields} fields", className, fieldCount);
            return $"Entity '{className}' created with {fieldCount} field(s). Run `validate_schema` to check for compilation errors, then Deploy to make it live.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Tool:create_entity] Error");
            return $"Error creating entity: {ex.Message}";
        }
    }

    [Description("Modify an existing runtime entity — add fields, remove fields, update fields, or change class-level properties. After modification, call validate_schema then Deploy.")]
    private string ModifyEntity(
        [Description("The class name of the entity to modify.")] string entityName,
        [Description("JSON object with modifications: {\"addFields\": [{\"name\": \"...\", \"type\": \"...\", \"required\": false, \"referencedClass\": null}], \"removeFields\": [\"FieldName\"], \"updateFields\": [{\"name\": \"ExistingField\", \"type\": \"System.Int32\", \"required\": true}], \"navigationGroup\": \"NewGroup\", \"description\": \"New desc\", \"isApiExposed\": true}")] string modificationsJson)
    {
        _logger.LogInformation("[Tool:modify_entity] Modifying {Name}", entityName);
        try
        {
            if (string.IsNullOrWhiteSpace(entityName))
                return "Error: entityName is required.";
            if (string.IsNullOrWhiteSpace(modificationsJson))
                return "Error: modificationsJson is required.";

            using var scope = CreateObjectSpace();
            var cc = scope.Os.GetObjectsQuery<CustomClass>()
                .FirstOrDefault(c => c.ClassName == entityName);

            if (cc == null)
            {
                var available = string.Join(", ", scope.Os.GetObjectsQuery<CustomClass>()
                    .Select(c => c.ClassName).OrderBy(n => n));
                return $"Entity '{entityName}' not found. Available: {(string.IsNullOrEmpty(available) ? "none" : available)}";
            }

            if (cc.Status == CustomClassStatus.Compiled)
                return $"Error: Entity '{entityName}' has been graduated (Status=Compiled) and cannot be modified at runtime.";

            var mods = JsonSerializer.Deserialize<ModificationsPayload>(modificationsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (mods == null)
                return "Error: Could not parse modificationsJson.";

            var changes = new List<string>();

            // Update class-level properties
            if (mods.NavigationGroup != null)
            {
                cc.NavigationGroup = mods.NavigationGroup;
                changes.Add($"NavigationGroup -> '{mods.NavigationGroup}'");
            }
            if (mods.Description != null)
            {
                cc.Description = mods.Description;
                changes.Add($"Description updated");
            }
            if (mods.IsApiExposed.HasValue)
            {
                cc.IsApiExposed = mods.IsApiExposed.Value;
                changes.Add($"IsApiExposed -> {mods.IsApiExposed.Value}");
            }

            // Remove fields
            if (mods.RemoveFields != null)
            {
                foreach (var fieldName in mods.RemoveFields)
                {
                    var field = cc.Fields?.Cast<CustomField>().FirstOrDefault(f => f.FieldName == fieldName);
                    if (field != null)
                    {
                        scope.Os.Delete(field);
                        changes.Add($"Removed field '{fieldName}'");
                    }
                    else
                    {
                        changes.Add($"Field '{fieldName}' not found (skipped)");
                    }
                }
            }

            // Add fields
            if (mods.AddFields != null)
            {
                var maxSort = cc.Fields?.Cast<CustomField>().Max(f => (int?)f.SortOrder) ?? -1;
                foreach (var fd in mods.AddFields)
                {
                    var existing = cc.Fields?.Cast<CustomField>().FirstOrDefault(f => f.FieldName == fd.Name);
                    if (existing != null)
                    {
                        changes.Add($"Field '{fd.Name}' already exists (skipped add)");
                        continue;
                    }

                    var field = scope.Os.CreateObject<CustomField>();
                    field.CustomClass = cc;
                    field.FieldName = fd.Name;
                    field.TypeName = string.IsNullOrWhiteSpace(fd.ReferencedClass)
                        ? (fd.Type ?? "System.String")
                        : "Reference";
                    field.IsRequired = fd.Required;
                    field.ReferencedClassName = fd.ReferencedClass;
                    field.Description = fd.Description;
                    field.SortOrder = ++maxSort;
                    changes.Add($"Added field '{fd.Name}' ({field.TypeName})");
                }
            }

            // Update existing fields
            if (mods.UpdateFields != null)
            {
                foreach (var fd in mods.UpdateFields)
                {
                    var field = cc.Fields?.Cast<CustomField>().FirstOrDefault(f => f.FieldName == fd.Name);
                    if (field == null)
                    {
                        changes.Add($"Field '{fd.Name}' not found for update (skipped)");
                        continue;
                    }

                    if (fd.Type != null)
                    {
                        field.TypeName = string.IsNullOrWhiteSpace(fd.ReferencedClass)
                            ? fd.Type
                            : "Reference";
                    }
                    if (fd.ReferencedClass != null)
                        field.ReferencedClassName = fd.ReferencedClass;
                    field.IsRequired = fd.Required;
                    if (fd.Description != null)
                        field.Description = fd.Description;
                    changes.Add($"Updated field '{fd.Name}'");
                }
            }

            scope.Os.CommitChanges();

            var summary = changes.Count > 0
                ? string.Join("\n- ", changes)
                : "No changes applied";
            _logger.LogInformation("[Tool:modify_entity] Modified {Name}: {Changes}", entityName, changes.Count);
            return $"Entity '{entityName}' modified:\n- {summary}\n\nRun `validate_schema` to check, then Deploy.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Tool:modify_entity] Error");
            return $"Error modifying entity: {ex.Message}";
        }
    }

    [Description("Delete a runtime entity and all its fields. Cannot delete entities with Status=Compiled (graduated). After deletion, Deploy to remove the live type.")]
    private string DeleteEntity(
        [Description("The class name of the entity to delete.")] string entityName)
    {
        _logger.LogInformation("[Tool:delete_entity] Deleting {Name}", entityName);
        try
        {
            if (string.IsNullOrWhiteSpace(entityName))
                return "Error: entityName is required.";

            using var scope = CreateObjectSpace();
            var cc = scope.Os.GetObjectsQuery<CustomClass>()
                .FirstOrDefault(c => c.ClassName == entityName);

            if (cc == null)
            {
                var available = string.Join(", ", scope.Os.GetObjectsQuery<CustomClass>()
                    .Select(c => c.ClassName).OrderBy(n => n));
                return $"Entity '{entityName}' not found. Available: {(string.IsNullOrEmpty(available) ? "none" : available)}";
            }

            if (cc.Status == CustomClassStatus.Compiled)
                return $"Error: Entity '{entityName}' has been graduated (Status=Compiled) and cannot be deleted. Remove it from the codebase instead.";

            var fieldCount = cc.Fields?.Cast<CustomField>().Count() ?? 0;

            // Delete fields first, then the class
            if (cc.Fields != null)
            {
                foreach (var field in cc.Fields.Cast<CustomField>().ToList())
                    scope.Os.Delete(field);
            }
            scope.Os.Delete(cc);
            scope.Os.CommitChanges();

            _logger.LogInformation("[Tool:delete_entity] Deleted {Name} with {Fields} fields", entityName, fieldCount);
            return $"Entity '{entityName}' and its {fieldCount} field(s) deleted. Deploy to remove the live type.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Tool:delete_entity] Error");
            return $"Error deleting entity: {ex.Message}";
        }
    }

    // ==========================================================================
    // ROLE TOOLS
    // ==========================================================================

    [Description("List all security roles in the application. Returns role names and whether they are admin roles.")]
    private string ListRoles()
    {
        _logger.LogInformation("[Tool:list_roles] Called");
        try
        {
            // XPO uses PermissionPolicyRole from the Xpo namespace
            Type roleType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                roleType = asm.GetType("DevExpress.Persistent.BaseImpl.PermissionPolicy.PermissionPolicyRole");
                if (roleType != null) break;
            }

            if (roleType == null)
                return "Security module is not configured in this application. Role management is not available.";

            var scope = _serviceProvider.CreateScope();
            IObjectSpace os;
            try
            {
                var factory = scope.ServiceProvider.GetRequiredService<INonSecuredObjectSpaceFactory>();
                os = factory.CreateNonSecuredObjectSpace(roleType);
            }
            catch
            {
                scope.Dispose();
                return "Security module is not configured or INonSecuredObjectSpaceFactory is not available for role types.";
            }

            using (new ScopedObjectSpace(os, scope))
            {
                var roles = os.GetObjects(roleType).Cast<object>().ToList();

                if (roles.Count == 0)
                    return "No roles found in the application.";

                var sb = new StringBuilder();
                sb.AppendLine("| Role Name | Is Admin |");
                sb.AppendLine("|---|---|");
                foreach (dynamic role in roles)
                {
                    try
                    {
                        string name = role.Name;
                        bool isAdmin = role.IsAdministrative;
                        sb.AppendLine($"| {name} | {(isAdmin ? "Yes" : "No")} |");
                    }
                    catch
                    {
                        sb.AppendLine($"| (error reading role) | ? |");
                    }
                }

                return sb.ToString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Tool:list_roles] Error");
            return $"Error listing roles: {ex.Message}";
        }
    }

    [Description("Set type-level permissions for a role on a specific entity. Configures read, write, create, and delete access.")]
    private string SetRolePermissions(
        [Description("The name of the role to modify (e.g. 'Users', 'Managers').")] string roleName,
        [Description("The entity name to set permissions for (e.g. 'Employee'). Can be a runtime or compiled entity.")] string entityName,
        [Description("Allow read access. Defaults to true.")] bool allowRead,
        [Description("Allow write/update access. Defaults to true.")] bool allowWrite,
        [Description("Allow creating new records. Defaults to true.")] bool allowCreate,
        [Description("Allow deleting records. Defaults to false.")] bool allowDelete)
    {
        _logger.LogInformation("[Tool:set_role_permissions] Role={Role}, Entity={Entity}", roleName, entityName);
        try
        {
            if (string.IsNullOrWhiteSpace(roleName))
                return "Error: roleName is required.";
            if (string.IsNullOrWhiteSpace(entityName))
                return "Error: entityName is required.";

            // Find the role type (XPO version)
            Type roleType = null;
            Type permissionType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                roleType ??= asm.GetType("DevExpress.Persistent.BaseImpl.PermissionPolicy.PermissionPolicyRole");
                permissionType ??= asm.GetType("DevExpress.Persistent.BaseImpl.PermissionPolicy.PermissionPolicyTypePermissionObject");
                if (roleType != null && permissionType != null) break;
            }

            if (roleType == null || permissionType == null)
                return "Security module is not configured in this application. Role management is not available.";

            // Find the target entity type
            Type targetType = null;
            // Check runtime types first
            targetType = XafXPODynAssemModule.AssemblyManager.RuntimeTypes
                .FirstOrDefault(t => t.Name == entityName);
            // Check compiled types
            if (targetType == null)
            {
                foreach (var typeInfo in XafTypesInfo.Instance.PersistentTypes)
                {
                    if (typeInfo.Name == entityName)
                    {
                        targetType = typeInfo.Type;
                        break;
                    }
                }
            }

            if (targetType == null)
                return $"Error: Entity '{entityName}' not found among runtime or compiled types.";

            using var scopedOs = CreateObjectSpaceForType(roleType);
            var os = scopedOs.Os;

            // Find the role
            dynamic role = os.GetObjects(roleType).Cast<object>()
                .FirstOrDefault(r => ((dynamic)r).Name == roleName);

            if (role == null)
            {
                var availableRoles = string.Join(", ",
                    os.GetObjects(roleType).Cast<object>().Select(r => ((dynamic)r).Name?.ToString()));
                return $"Role '{roleName}' not found. Available roles: {(string.IsNullOrEmpty(availableRoles) ? "none" : availableRoles)}";
            }

            // Use XAF's permission policy API
            try
            {
                // Build operations string
                var ops = new List<string>();
                if (allowRead) ops.Add("Read");
                if (allowWrite) ops.Add("Write");
                if (allowCreate) ops.Add("Create");
                if (allowDelete) ops.Add("Delete");
                var operationsStr = string.Join(";", ops);

                // Use the AddTypePermission approach via reflection
                var addMethod = roleType.GetMethods()
                    .FirstOrDefault(m => m.Name == "AddTypePermissionsRecursively" && m.GetParameters().Length == 3 && !m.IsGenericMethod);

                if (addMethod != null)
                {
                    // Get SecurityPermissionState.Allow
                    Type stateEnum = null;
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        stateEnum = asm.GetType("DevExpress.Persistent.Base.SecurityPermissionState");
                        if (stateEnum != null) break;
                    }

                    if (stateEnum != null)
                    {
                        var allowState = Enum.Parse(stateEnum, "Allow");
                        var denyState = Enum.Parse(stateEnum, "Deny");

                        // Set allowed operations
                        if (ops.Count > 0)
                            addMethod.Invoke(role, new object[] { targetType, operationsStr, allowState });

                        // Set denied operations
                        var denyOps = new List<string>();
                        if (!allowRead) denyOps.Add("Read");
                        if (!allowWrite) denyOps.Add("Write");
                        if (!allowCreate) denyOps.Add("Create");
                        if (!allowDelete) denyOps.Add("Delete");

                        if (denyOps.Count > 0)
                            addMethod.Invoke(role, new object[] { targetType, string.Join(";", denyOps), denyState });
                    }
                }
                else
                {
                    return "Error: Could not find AddTypePermissionsRecursively method on the role type. Security API may have changed.";
                }
            }
            catch (Exception ex)
            {
                return $"Error setting permissions via API: {ex.Message}";
            }

            os.CommitChanges();

            var summary = new StringBuilder();
            summary.AppendLine($"Permissions set for role '{roleName}' on entity '{entityName}':");
            summary.AppendLine($"- Read: {(allowRead ? "Allow" : "Deny")}");
            summary.AppendLine($"- Write: {(allowWrite ? "Allow" : "Deny")}");
            summary.AppendLine($"- Create: {(allowCreate ? "Allow" : "Deny")}");
            summary.AppendLine($"- Delete: {(allowDelete ? "Allow" : "Deny")}");
            return summary.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Tool:set_role_permissions] Error");
            return $"Error setting role permissions: {ex.Message}";
        }
    }

    // ==========================================================================
    // REPORT TOOLS
    // ==========================================================================

    /// <summary>
    /// Wynik walidacji zadania raportu — braki jako dane, nie jako wyjatek.
    /// Rozroznia DWIE rzeczy, bo prowadza do roznych zachowan modelu:
    /// <see cref="Problems"/> to bledy (uzytkownik podal cos, czego nie ma — popraw),
    /// <see cref="Missing"/> to luki (uzytkownik czegos NIE podal — dopytaj, nie zgaduj).
    /// </summary>
    private sealed record ReportRequestValidation(
        Type RuntimeType,
        IReadOnlyList<ReportColumnSpec> Columns,
        string GroupByPath,
        string SortByPath,
        IReadOnlyList<string> Problems,
        IReadOnlyList<string> Missing)
    {
        public bool IsValid => Problems.Count == 0 && Missing.Count == 0;
    }

    /// <summary>Wlasciwosci runtime'owego typu, ktore da sie pokazac w komorce raportu.</summary>
    private static IReadOnlyList<System.Reflection.PropertyInfo> GetReportableProperties(Type type)
        => type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Where(p => p.PropertyType.IsPrimitive
                        || p.PropertyType.IsEnum
                        || p.PropertyType == typeof(string)
                        || p.PropertyType == typeof(decimal)
                        || p.PropertyType == typeof(DateTime)
                        || p.PropertyType == typeof(Guid)
                        || Nullable.GetUnderlyingType(p.PropertyType) != null)
            .Where(p => p.Name != "Oid" && p.Name != "GCRecord" && p.Name != "OptimisticLockField"
                        && p.Name != "IsDeleted" && p.Name != "Session" && p.Name != "ClassInfo"
                        && p.Name != "Loading" && p.Name != "IsLoading")
            .ToList();

    /// <summary>Wlasciwosci referencyjne — po nich da sie zejsc sciezka „Faktura.Customer.NazwaKlienta".</summary>
    private static IReadOnlyList<System.Reflection.PropertyInfo> GetReferenceProperties(Type type)
        => type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Where(p => XafXPODynAssemModule.AssemblyManager.RuntimeTypes.Contains(p.PropertyType))
            .ToList();

    /// <summary>
    /// Rozwiazuje sciezke pola — plaska („NumerFaktury") albo przez referencje
    /// („Faktura.Customer.NazwaKlienta"). Naprawia wielkosc liter na kazdym odcinku.
    /// Zwraca null i wypelnia <paramref name="error"/>, gdy ktorys odcinek nie istnieje.
    /// </summary>
    private static string ResolvePath(Type root, string path, out string error)
    {
        error = null;
        var segments = (path ?? string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0) { error = "empty field path"; return null; }

        var current = root;
        var canonical = new List<string>();
        for (var i = 0; i < segments.Length; i++)
        {
            var isLast = i == segments.Length - 1;
            var candidates = isLast
                ? GetReportableProperties(current).Concat(GetReferenceProperties(current)).ToList()
                : GetReferenceProperties(current).ToList();

            var prop = candidates.FirstOrDefault(p =>
                string.Equals(p.Name, segments[i], StringComparison.OrdinalIgnoreCase));

            if (prop == null)
            {
                var available = isLast
                    ? string.Join(", ", GetReportableProperties(current).Select(p => p.Name)
                        .Concat(GetReferenceProperties(current).Select(p => p.Name + " (reference)")))
                    : string.Join(", ", GetReferenceProperties(current).Select(p => p.Name));
                error = $"'{segments[i]}' is not a {(isLast ? "field" : "reference")} of '{current.Name}'. "
                        + $"Available on '{current.Name}': {available}";
                return null;
            }
            canonical.Add(prop.Name);
            current = prop.PropertyType;
        }
        return string.Join(".", canonical);
    }

    /// <summary>Odnajduje typ encji runtime po nazwie klasy (bez rozroznienia wielkosci liter).</summary>
    private Type ResolveRuntimeType(string entityName, out string error)
    {
        error = null;
        var runtimeTypes = XafXPODynAssemModule.AssemblyManager.RuntimeTypes;

        if (string.IsNullOrWhiteSpace(entityName))
        {
            error = "Error: entityName is required. Available runtime entities: "
                    + string.Join(", ", runtimeTypes.Select(t => t.Name).OrderBy(n => n));
            return null;
        }

        var match = runtimeTypes.FirstOrDefault(t =>
            string.Equals(t.Name, entityName.Trim(), StringComparison.OrdinalIgnoreCase));

        if (match == null)
        {
            error = $"Error: runtime entity '{entityName}' not found. Available: "
                    + string.Join(", ", runtimeTypes.Select(t => t.Name).OrderBy(n => n))
                    + ". Only deployed (Runtime) entities can be reported on — "
                    + "if the entity was just created, deploy the schema first.";
            return null;
        }
        return match;
    }

    /// <summary>
    /// Wspolna walidacja dla validate_report_spec i build_report — jedno zrodlo prawdy,
    /// zeby narzedzia nie mogly sie rozjechac. Naprawia wielkosc liter w nazwach pol.
    /// </summary>
    private ReportRequestValidation ValidateReportRequest(
        string entityName, string fieldPaths, string groupByField, string sortByField,
        string headerLines = null, string summaryFields = null)
    {
        var problems = new List<string>();
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(entityName))
        {
            missing.Add("MISSING entity: the user has not said which entity the report reads from. "
                        + "Ask them. Available runtime entities: "
                        + string.Join(", ", XafXPODynAssemModule.AssemblyManager.RuntimeTypes
                            .Select(t => t.Name).OrderBy(n => n)));
            return new ReportRequestValidation(null, Array.Empty<ReportColumnSpec>(), null, null, problems, missing);
        }

        var type = ResolveRuntimeType(entityName, out var typeError);
        if (type == null)
        {
            problems.Add(typeError);
            return new ReportRequestValidation(null, Array.Empty<ReportColumnSpec>(), null, null, problems, missing);
        }

        var available = GetReportableProperties(type);

        // Puste fieldPaths NIE jest uzupelniane po cichu — to brak informacji, o ktory model
        // ma dopytac uzytkownika, a nie dziura do zalatania domyslna wartoscia.
        var requested = ReportSpecBuilder.ParseList(fieldPaths).ToList();
        if (requested.Count == 0)
        {
            missing.Add($"MISSING columns: the user has not said which fields of '{type.Name}' the report should show. "
                        + $"Ask them which of these to include: {string.Join(", ", available.Select(p => p.Name))}");
        }

        var columns = new List<ReportColumnSpec>();
        foreach (var raw in requested)
        {
            var resolved = ResolvePath(type, raw, out var pathError);
            if (resolved != null)
                columns.Add(new ReportColumnSpec(resolved, resolved.Split('.').Last()));
            else
                problems.Add($"Unknown column '{raw}': {pathError}");
        }

        if (requested.Count > 0 && columns.Count == 0 && problems.Count == 0)
            problems.Add($"Entity '{type.Name}' has no reportable scalar fields.");

        string ResolveOptional(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var resolved = ResolvePath(type, value, out var pathError);
            if (resolved != null) return resolved;
            problems.Add($"Unknown {label} field '{value}': {pathError}");
            return null;
        }

        var groupPath = ResolveOptional(groupByField, "group-by");
        var sortPath = ResolveOptional(sortByField, "sort-by");

        // Naglowek dokumentu: kazde [Pole] musi istniec, inaczej wyrazenie wybuchnie przy renderze.
        foreach (var line in ReportSpecBuilder.ParseHeaderLines(headerLines))
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(line, @"\[([^\]]+)\]"))
            {
                var resolved = ResolvePath(type, m.Groups[1].Value, out var pathError);
                if (resolved == null)
                    problems.Add($"Header line references unknown field '{m.Groups[1].Value}': {pathError}");
            }

        foreach (var field in ReportSpecBuilder.ParseList(summaryFields))
        {
            var resolved = ResolvePath(type, field, out var pathError);
            if (resolved == null) problems.Add($"Summary field '{field}' is unknown: {pathError}");
        }

        return new ReportRequestValidation(type, columns, groupPath, sortPath, problems, missing);
    }

    [Description("Validate a report request WITHOUT building anything. Returns two different kinds of feedback " +
                 "that you MUST treat differently: 'PROBLEM' means the user named something that does not exist — " +
                 "correct it. 'MISSING' means the user never specified something — ASK THE USER ONE SPECIFIC QUESTION " +
                 "about it and wait for the answer. Never invent a value for a MISSING item and never silently fall back " +
                 "to a default. Call this before `build_report` whenever the user's request was vague.")]
    private string ValidateReportSpec(
        [Description("Runtime entity class name the rows come from, e.g. 'Produkt', 'PozycjaFaktury'.")] string entityName,
        [Description("Comma-separated column paths, e.g. 'OpisPozycji,Ilosc,WartoscBrutto'. Dotted paths across references are allowed, e.g. 'Faktura.NumerFaktury'.")] string fieldPaths = null,
        [Description("Field path to group rows by. Optional.")] string groupByField = null,
        [Description("Field path to sort rows by. Optional.")] string sortByField = null,
        [Description("Document header lines, separated by '|'. Use [FieldPath] to insert data, e.g. 'Faktura nr [Faktura.NumerFaktury]|Nabywca: [Faktura.Customer.NazwaKlienta]'. Optional.")] string headerLines = null,
        [Description("Comma-separated numeric field paths to total below the table, e.g. 'WartoscNetto,WartoscBrutto'. Optional.")] string summaryFields = null)
    {
        _logger.LogInformation("[Tool:validate_report_spec] Called with entity={Entity}, fields={Fields}, header={Header}",
            entityName, fieldPaths, headerLines);
        try
        {
            var validation = ValidateReportRequest(entityName, fieldPaths, groupByField, sortByField,
                headerLines, summaryFields);

            var sb = new StringBuilder();
            if (!validation.IsValid)
            {
                foreach (var p in validation.Problems) sb.AppendLine($"PROBLEM: {p}");
                foreach (var m in validation.Missing) sb.AppendLine(m);
                sb.AppendLine();
                if (validation.Missing.Count > 0)
                    sb.AppendLine("Ask the user about the MISSING item(s) — one clear question at a time. "
                                  + "Do NOT guess and do NOT call `build_report` yet.");
                else
                    sb.AppendLine("Fix the PROBLEM(s) above, then validate again.");
                _logger.LogInformation("[Tool:validate_report_spec] Invalid — {Problems} problem(s), {Missing} missing",
                    validation.Problems.Count, validation.Missing.Count);
                return sb.ToString();
            }

            sb.AppendLine($"Report spec is valid for entity '{validation.RuntimeType.Name}'.");
            sb.AppendLine($"- Columns ({validation.Columns.Count}): {string.Join(", ", validation.Columns.Select(c => c.Path))}");
            sb.AppendLine($"- Group by: {validation.GroupByPath ?? "(none)"}");
            sb.AppendLine($"- Sort by: {validation.SortByPath ?? "(none)"}");
            sb.AppendLine($"- Default margins: {ReportSpec.DefaultMarginMm} mm on every side, A4 portrait.");
            sb.AppendLine("Call `build_report` with the same arguments to create it.");
            _logger.LogInformation("[Tool:validate_report_spec] Valid, {Count} column(s)", validation.Columns.Count);
            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Tool:validate_report_spec] Error");
            return $"Error validating report spec: {ex.Message}";
        }
    }

    [Description("Build an XtraReport over a runtime entity and save it into ReportDataV2, so the user can open it " +
                 "in the XAF report designer (Reports navigation item) and run a preview. " +
                 "Refuses to build when the entity or any field name is unknown — call `validate_report_spec` first if unsure. " +
                 "Only works for entities already deployed to runtime; a freshly created entity must be deployed first.")]
    private string BuildReport(
        [Description("Runtime entity class name the rows come from, e.g. 'Produkt', 'PozycjaFaktury'.")] string entityName,
        [Description("Comma-separated column paths, e.g. 'OpisPozycji,Ilosc,WartoscBrutto'. Dotted paths across references are allowed, e.g. 'Faktura.NumerFaktury'.")] string fieldPaths = null,
        [Description("Report title, shown at the top and as the report name in the Reports list.")] string title = null,
        [Description("Field path to group rows by. Optional.")] string groupByField = null,
        [Description("Field path to sort rows by. Optional.")] string sortByField = null,
        [Description("Sort descending instead of ascending.")] bool sortDescending = false,
        [Description("DevExpress criteria filter applied to the data source, e.g. \"CenaJednostkowa > 100\". Optional.")] string filterCriteria = null,
        [Description("Page orientation: 'Portrait' (default) or 'Landscape'.")] string orientation = "Portrait",
        [Description("Document header lines, separated by '|'. Use [FieldPath] to insert data from the record, " +
                     "e.g. 'Faktura nr [Faktura.NumerFaktury]|Data: [Faktura.DataWystawienia]|Nabywca: [Faktura.Customer.NazwaKlienta]'. " +
                     "This is what turns a plain list into a document (invoice, order, protocol). Optional.")] string headerLines = null,
        [Description("Comma-separated numeric field paths totalled in a summary band below the table, e.g. 'WartoscNetto,WartoscBrutto'. Optional.")] string summaryFields = null)
    {
        _logger.LogInformation(
            "[Tool:build_report] Called with entity={Entity}, fields={Fields}, title={Title}, groupBy={Group}, sortBy={Sort}, header={Header}, summary={Summary}",
            entityName, fieldPaths, title, groupByField, sortByField, headerLines, summaryFields);
        try
        {
            var validation = ValidateReportRequest(entityName, fieldPaths, groupByField, sortByField,
                headerLines, summaryFields);
            if (!validation.IsValid)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Refusing to build the report — the spec is not complete:");
                foreach (var p in validation.Problems) sb.AppendLine($"PROBLEM: {p}");
                foreach (var m in validation.Missing) sb.AppendLine(m);
                sb.AppendLine();
                sb.AppendLine(validation.Missing.Count > 0
                    ? "Ask the user about the MISSING item(s) — one clear question at a time — then call this tool again. Do NOT guess."
                    : "Fix the PROBLEM(s) and call this tool again.");
                _logger.LogWarning("[Tool:build_report] Refused — {Problems} problem(s), {Missing} missing",
                    validation.Problems.Count, validation.Missing.Count);
                return sb.ToString();
            }

            var runtimeType = validation.RuntimeType;
            var reportTitle = string.IsNullOrWhiteSpace(title) ? $"Raport — {runtimeType.Name}" : title.Trim();

            // Specyfikacja zyje tylko w pamieci — zwykly obiekt, bez ObjectSpace i bez tabeli.
            var spec = new ReportSpec
            {
                Title = reportTitle,
                SortDescending = sortDescending,
                FilterCriteria = string.IsNullOrWhiteSpace(filterCriteria) ? null : filterCriteria.Trim(),
                HeaderLines = string.IsNullOrWhiteSpace(headerLines) ? null : headerLines.Trim(),
                SummaryFields = string.IsNullOrWhiteSpace(summaryFields) ? null : summaryFields.Trim(),
                Orientation = string.Equals(orientation?.Trim(), "Landscape", StringComparison.OrdinalIgnoreCase)
                    ? ReportOrientation.Landscape
                    : ReportOrientation.Portrait,
            };

            XtraReport report = ReportSpecBuilder.Build(
                spec, runtimeType.FullName, validation.Columns,
                validation.GroupByPath, validation.SortByPath);
            report.Name = reportTitle;

            // Materializacja w ReportDataV2 przez kanoniczny magazyn raportow XAF.
            using var scope = CreateObjectSpaceForType(typeof(DevExpress.Persistent.BaseImpl.ReportDataV2));
            var reportData = scope.Os.CreateObject<DevExpress.Persistent.BaseImpl.ReportDataV2>();
            reportData.DisplayName = reportTitle;

            var storage = DevExpress.ExpressApp.ReportsV2.ReportDataProvider.GetReportStorage(scope.ServiceProvider);
            if (storage == null)
                return "Error: report storage is not available (ReportsModuleV2 not configured?).";

            storage.SaveReport(reportData, report);
            // Bez tej flagi raport NIE trafia do akcji „Pokaz na raporcie" na widokach encji —
            // InplaceReportCacheHelperService pobiera wylacznie ReportDataV2 z IsInplaceReport = true.
            reportData.IsInplaceReport = true;
            scope.Os.CommitChanges();

            var key = scope.Os.GetKeyValue(reportData)?.ToString();
            var savedDataType = reportData.DataTypeName;

            _logger.LogInformation(
                "[Tool:build_report] Saved ReportDataV2 key={Key}, DisplayName='{Name}', DataTypeName='{DataType}', columns={Columns}",
                key, reportTitle, savedDataType, validation.Columns.Count);

            if (string.IsNullOrWhiteSpace(savedDataType))
            {
                _logger.LogWarning("[Tool:build_report] DataTypeName is empty — the designer will have nothing to bind.");
                return $"Report '{reportTitle}' was saved (key {key}), but its data type could not be determined. "
                       + "Open it in the report designer and set the data source manually.";
            }

            var result = new StringBuilder();
            result.AppendLine($"Report **{reportTitle}** built and saved.");
            result.AppendLine();
            result.AppendLine($"- Entity: `{runtimeType.Name}` (`{savedDataType}`)");
            result.AppendLine($"- Columns ({validation.Columns.Count}): {string.Join(", ", validation.Columns.Select(c => c.Path))}");
            if (validation.GroupByPath != null) result.AppendLine($"- Grouped by: {validation.GroupByPath}");
            if (validation.SortByPath != null)
                result.AppendLine($"- Sorted by: {validation.SortByPath} {(sortDescending ? "descending" : "ascending")}");
            if (!string.IsNullOrWhiteSpace(filterCriteria)) result.AppendLine($"- Filter: `{filterCriteria}`");
            result.AppendLine($"- Page: A4 {(string.Equals(orientation?.Trim(), "Landscape", StringComparison.OrdinalIgnoreCase) ? "landscape" : "portrait")}, "
                              + $"{ReportSpec.DefaultMarginMm} mm margins on every side");
            result.AppendLine($"- Report key: `{key}`");
            result.AppendLine();
            result.AppendLine("Open the **Reports** navigation item to preview it or adjust the layout in the designer.");
            result.AppendLine("The report is marked as inplace. Tell the user to refresh the page (F5) — "
                              + "the inplace report cache is built once per Blazor circuit, so the "
                              + "**Pokaz na raporcie** action on the entity's views picks it up only after a reload.");
            return result.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Tool:build_report] Error");
            return $"Error building report: {ex.Message}";
        }
    }

    /// <summary>Katalog na wyrenderowane dokumenty — poza repozytorium, kasowalny.</summary>
    private static string RenderOutputDirectory
    {
        get
        {
            var dir = Path.Combine(Path.GetTempPath(), "xaf-report-render");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Czyta wartosc po sciezce „Faktura.Customer.NazwaKlienta" przez refleksje.</summary>
    private static object ReadPath(object root, string path)
    {
        var current = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current == null) return null;
            var prop = current.GetType().GetProperty(segment,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.IgnoreCase);
            if (prop == null) return null;
            current = prop.GetValue(current);
        }
        return current;
    }

    private static string FormatCell(object value, int maxWidth)
    {
        var text = value switch
        {
            null => "",
            DateTime dt => dt.ToString("yyyy-MM-dd"),
            decimal d => d.ToString("N2"),
            double d => d.ToString("N2"),
            _ => value.ToString(),
        };
        text = text.Replace("|", "\\|").Replace("\n", " ").Trim();
        return text.Length > maxWidth ? text.Substring(0, maxWidth - 1) + "…" : text;
    }

    [Description("Show the ACTUAL DATA of a report as a text table in the chat, and optionally RENDER sample " +
                 "documents to image files so the user can see what the layout looks like without opening the designer. " +
                 "Use render=true together with headerLines/summaryFields to preview a document layout (invoice, order). " +
                 "When renderSamples is used, one separate document is produced per distinct value of documentKeyField, " +
                 "so the user can check the layout holds across different records — not just one lucky row.")]
    private string PreviewReport(
        [Description("Runtime entity class name the rows come from, e.g. 'Produkt', 'PozycjaFaktury'.")] string entityName,
        [Description("Comma-separated column paths. Dotted paths across references are allowed, e.g. 'Faktura.NumerFaktury'.")] string fieldPaths = null,
        [Description("DevExpress criteria filter, e.g. \"CenaJednostkowa > 1000\" or \"Miejscowosc = 'Katowice'\". Translate the user's plain-language filter into this syntax. Optional.")] string filterCriteria = null,
        [Description("Maximum rows to show in the text table. Default 10.")] int maxRows = 10,
        [Description("Field path to sort by. Optional.")] string sortByField = null,
        [Description("Sort descending instead of ascending.")] bool sortDescending = false,
        [Description("Render sample documents to image files in addition to the text table.")] bool render = false,
        [Description("How many sample documents to render. Default 3.")] int sampleCount = 3,
        [Description("Field path whose distinct values separate one document from the next, e.g. 'Faktura.NumerFaktury'. Required when rendering more than one sample.")] string documentKeyField = null,
        [Description("Report title shown at the top of the rendered document.")] string title = null,
        [Description("Document header lines separated by '|', with [FieldPath] placeholders. Optional.")] string headerLines = null,
        [Description("Comma-separated numeric field paths totalled below the table. Optional.")] string summaryFields = null)
    {
        _logger.LogInformation(
            "[Tool:preview_report] Called with entity={Entity}, fields={Fields}, filter={Filter}, maxRows={MaxRows}, render={Render}, samples={Samples}, key={Key}, header={Header}, summary={Summary}",
            entityName, fieldPaths, filterCriteria, maxRows, render, sampleCount, documentKeyField, headerLines, summaryFields);
        try
        {
            var validation = ValidateReportRequest(entityName, fieldPaths, null, sortByField,
                headerLines, summaryFields);
            if (!validation.IsValid)
            {
                var refusal = new StringBuilder();
                refusal.AppendLine("Cannot preview — the spec is not complete:");
                foreach (var p in validation.Problems) refusal.AppendLine($"PROBLEM: {p}");
                foreach (var m in validation.Missing) refusal.AppendLine(m);
                refusal.AppendLine();
                refusal.AppendLine(validation.Missing.Count > 0
                    ? "Ask the user about the MISSING item(s) — one clear question at a time. Do NOT guess."
                    : "Fix the PROBLEM(s) and call this tool again.");
                _logger.LogWarning("[Tool:preview_report] Refused — {P} problem(s), {M} missing",
                    validation.Problems.Count, validation.Missing.Count);
                return refusal.ToString();
            }

            var type = validation.RuntimeType;
            var columns = validation.Columns;

            // Filtr uzytkownika: blad parsowania i blad wykonania traktujemy tak samo —
            // oddajemy liste pol, zeby model mial z czego poprawic, zamiast rzucac wyjatkiem.
            CriteriaOperator criteria = null;
            if (!string.IsNullOrWhiteSpace(filterCriteria))
            {
                try { criteria = CriteriaOperator.Parse(filterCriteria); }
                catch (Exception ex)
                {
                    _logger.LogWarning("[Tool:preview_report] Bad criteria: {Msg}", ex.Message);
                    return $"The filter `{filterCriteria}` could not be parsed: {ex.Message}\n"
                           + $"Available fields on '{type.Name}': "
                           + string.Join(", ", GetReportableProperties(type).Select(p => p.Name))
                           + "\nReferences you can go through: "
                           + string.Join(", ", GetReferenceProperties(type).Select(p => p.Name));
                }
            }

            using var scope = CreateObjectSpaceForType(type);
            int totalCount;
            System.Collections.IList all;
            try
            {
                totalCount = scope.Os.GetObjectsCount(type, criteria);
                all = scope.Os.GetObjects(type, criteria);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Tool:preview_report] Query failed: {Msg}", ex.Message);
                return $"The filter `{filterCriteria}` could not be executed: {ex.Message}\n"
                       + $"Available fields on '{type.Name}': "
                       + string.Join(", ", GetReportableProperties(type).Select(p => p.Name))
                       + "\nReferences you can go through: "
                       + string.Join(", ", GetReferenceProperties(type).Select(p => p.Name));
            }

            var rows = all.Cast<object>().ToList();
            if (!string.IsNullOrWhiteSpace(validation.SortByPath))
                rows = (sortDescending
                    ? rows.OrderByDescending(r => ReadPath(r, validation.SortByPath))
                    : rows.OrderBy(r => ReadPath(r, validation.SortByPath))).ToList();

            var shown = rows.Take(Math.Max(1, maxRows)).ToList();

            var output = new StringBuilder();
            output.AppendLine($"**{title ?? $"Podgląd — {type.Name}"}**");
            if (!string.IsNullOrWhiteSpace(filterCriteria))
                output.AppendLine($"Filtr: `{filterCriteria}`");
            output.AppendLine();

            output.AppendLine("| " + string.Join(" | ", columns.Select(c => c.Caption)) + " |");
            output.AppendLine("|" + string.Join("|", columns.Select(_ => "---")) + "|");
            foreach (var row in shown)
                output.AppendLine("| " + string.Join(" | ",
                    columns.Select(c => FormatCell(ReadPath(row, c.Path), 30))) + " |");

            output.AppendLine();
            output.AppendLine($"Zwrócono **{shown.Count}** z **{totalCount}** pasujących rekordów.");

            if (render)
            {
                output.AppendLine();
                output.AppendLine(RenderSamples(type, validation, rows, sampleCount, documentKeyField,
                    title, headerLines, summaryFields, sortDescending, filterCriteria));
            }

            _logger.LogInformation("[Tool:preview_report] Returned {Shown} of {Total} row(s), rendered={Render}, summaryFields={Summary}",
                shown.Count, totalCount, render, string.IsNullOrWhiteSpace(summaryFields) ? "(none)" : summaryFields);
            return output.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Tool:preview_report] Error");
            return $"Error previewing report: {ex.Message}";
        }
    }

    /// <summary>
    /// Renderuje N przykladowych dokumentow — po jednym na kolejna wartosc klucza dokumentu.
    /// Dane podstawiamy JAWNIE jako liste obiektow (report.DataSource = lista), zamiast liczyc
    /// na to, ze CollectionDataSource sam sie rozwiaze poza potokiem zadania XAF. Zapisany
    /// layout (ReportDataV2) dalej trzyma CollectionDataSource, wiec projektant dziala.
    /// </summary>
    private string RenderSamples(
        Type type, ReportRequestValidation validation, List<object> rows, int sampleCount,
        string documentKeyField, string title, string headerLines, string summaryFields,
        bool sortDescending, string filterCriteria)
    {
        var sb = new StringBuilder();

        string keyPath = null;
        if (!string.IsNullOrWhiteSpace(documentKeyField))
        {
            keyPath = ResolvePath(type, documentKeyField, out var keyError);
            if (keyPath == null) return $"Nie wyrenderowano: {keyError}";
        }

        // Grupujemy wiersze na dokumenty. Bez klucza — jeden dokument ze wszystkich wierszy.
        var groups = keyPath == null
            ? new List<(string Key, List<object> Rows)> { ("wszystkie", rows) }
            : rows.GroupBy(r => FormatCell(ReadPath(r, keyPath), 60))
                  .Select(g => (Key: g.Key, Rows: g.ToList()))
                  .ToList();

        var take = Math.Max(1, sampleCount);
        var selected = groups.Take(take).ToList();

        sb.AppendLine($"### Wyrenderowane dokumenty ({selected.Count} z {groups.Count})");
        sb.AppendLine();

        foreach (var (key, groupRows) in selected)
        {
            var spec = new ReportSpec
            {
                Title = title ?? $"Dokument — {type.Name}",
                SortDescending = sortDescending,
                HeaderLines = headerLines,
                SummaryFields = summaryFields,
            };

            XtraReport report = ReportSpecBuilder.Build(spec, type.FullName, validation.Columns,
                null, validation.SortByPath);

            // Podmiana zrodla na konkretna liste — to gwarantuje, ze dokument pokazuje
            // dokladnie te wiersze, ktore wyzej policzylismy i ktore widac w tabelce.
            report.DataSource = groupRows;
            report.DataMember = null;

            var safeKey = string.Concat((key ?? "dokument").Select(c => char.IsLetterOrDigit(c) ? c : '-'));
            var file = Path.Combine(RenderOutputDirectory,
                $"{type.Name}-{safeKey}-{DateTime.Now:HHmmss}.png");

            try
            {
                report.CreateDocument();
                var pages = report.Pages.Count;
                if (pages == 0)
                {
                    sb.AppendLine($"- **{key}** — dokument pusty (0 stron), nic nie zapisano.");
                    continue;
                }
                report.ExportToImage(file, DevExpress.Drawing.DXImageFormat.Png);
                var size = new FileInfo(file).Length;
                sb.AppendLine($"- **{key}** — {groupRows.Count} pozycji, {pages} str., {size / 1024} kB → `{file}`");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Tool:preview_report] Render failed for key {Key}", key);
                sb.AppendLine($"- **{key}** — render nie powiódł się: {ex.Message}");
            }
            finally
            {
                report.Dispose();
            }
        }

        // Tekstowy opis ukladu — uzytkownik czyta czat, nie otwiera plikow.
        sb.AppendLine();
        sb.AppendLine("**Układ dokumentu:**");
        foreach (var line in ReportSpecBuilder.ParseHeaderLines(headerLines))
            sb.AppendLine($"- Nagłówek: {line}");
        sb.AppendLine($"- Tabela pozycji: {string.Join(" | ", validation.Columns.Select(c => c.Caption))}");
        foreach (var f in ReportSpecBuilder.ParseList(summaryFields))
            sb.AppendLine($"- Podsumowanie: suma {f}");
        sb.AppendLine("- Stopka: numer strony");

        return sb.ToString();
    }

    // ==========================================================================
    // INVOICE TEMPLATE TOOL
    // ==========================================================================

    /// <summary>Sloty, ktore MUSZA dostac pole liczbowe — inaczej szablon policzy smiec.</summary>
    private static readonly HashSet<TemplateFieldKind> NumericSlots = new()
    {
        TemplateFieldKind.Quantity, TemplateFieldKind.UnitPrice, TemplateFieldKind.UnitDiscount,
        TemplateFieldKind.UnitTax, TemplateFieldKind.Discount, TemplateFieldKind.Tax,
        TemplateFieldKind.DiscountLineTotal, TemplateFieldKind.TaxLineTotal, TemplateFieldKind.LineTotal,
        TemplateFieldKind.Subtotal, TemplateFieldKind.DiscountTotal, TemplateFieldKind.TaxTotal,
        TemplateFieldKind.Total,
    };

    /// <summary>Sloty, ktore MUSZA dostac date.</summary>
    private static readonly HashSet<TemplateFieldKind> DateSlots = new()
    {
        TemplateFieldKind.InvoiceDate, TemplateFieldKind.InvoiceDueDate,
    };

    private static bool IsNumericType(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t == typeof(decimal) || t == typeof(double) || t == typeof(float)
               || t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte);
    }

    /// <summary>Kategoria slotu wynika z jego nazwy — deterministycznie, bez zgadywania.</summary>
    private static TemplateFieldCategory CategoryFor(TemplateFieldKind kind)
    {
        var n = kind.ToString();
        if (n.StartsWith("Vendor", StringComparison.Ordinal)) return TemplateFieldCategory.Vendor;
        if (n.StartsWith("Customer", StringComparison.Ordinal)) return TemplateFieldCategory.Customer;
        if (n.StartsWith("Invoice", StringComparison.Ordinal)) return TemplateFieldCategory.InvoiceInfo;
        return TemplateFieldCategory.OrderDetails;
    }

    /// <summary>Typ konczacy sciezke — potrzebny do walidacji dopasowania slotu do pola.</summary>
    private static Type ResolvePathType(Type root, string canonicalPath)
    {
        var current = root;
        foreach (var seg in canonicalPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var prop = current.GetProperty(seg,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.IgnoreCase);
            if (prop == null) return null;
            current = prop.PropertyType;
        }
        return current;
    }

    private static XtraReport CreateInvoiceTemplate(string name) => (name ?? "Invoice1").Trim().ToLowerInvariant() switch
    {
        "invoice1" or "1" => new InvoiceTemplate1(),
        "invoice2" or "2" => new InvoiceTemplate2(),
        "invoice3" or "3" => new InvoiceTemplate3(),
        "invoice4" or "4" => new InvoiceTemplate4(),
        "invoice5" or "5" => new InvoiceTemplate5(),
        "invoice6" or "6" => new InvoiceTemplate6(),
        "invoice7" or "7" => new InvoiceTemplate7(),
        "invoice8" or "8" => new InvoiceTemplate8(),
        "invoice9" or "9" => new InvoiceTemplate9(),
        _ => null,
    };

    /// <summary>Rozbija "Slot=wartosc;Slot2=wartosc2" na pary, tolerujac nowe linie.</summary>
    private static List<(string Slot, string Value)> ParsePairs(string raw)
    {
        var result = new List<(string, string)>();
        foreach (var part in (raw ?? string.Empty).Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0) continue;
            result.Add((part.Substring(0, idx).Trim(), part.Substring(idx + 1).Trim()));
        }
        return result;
    }

    /// <summary>Rozstrzygniecie, ktora encja jest naglowkiem, a ktora pozycja faktury.</summary>
    private sealed record InvoiceShape(
        Type HeaderType,
        Type LineType,
        string BackReferencePath,
        List<InvoiceSlot> Slots,
        List<string> Problems)
    {
        public bool IsValid => Problems.Count == 0;
    }

    /// <summary>Encje runtime majace referencje do <paramref name="parent"/> — kandydaci na pozycje.</summary>
    private static List<(Type Child, string BackRef)> FindChildEntities(Type parent)
        => XafXPODynAssemModule.AssemblyManager.RuntimeTypes
            .SelectMany(t => GetReferenceProperties(t)
                .Where(p => p.PropertyType == parent)
                .Select(p => (Child: t, BackRef: p.Name)))
            .ToList();

    /// <summary>
    /// Probuje zlozyc mapowanie w model naglowek + pozycje. Sloty z <see cref="InvoiceReportBuilder.LineSlots"/>
    /// rozwiazuja sie wzgledem encji pozycji, cala reszta wzgledem naglowka.
    /// Tolerowany jest prefiks nazwy naglowka w sciezce naglowkowej („Faktura.NumerFaktury" na Fakturze).
    /// </summary>
    private static InvoiceShape TryShape(
        Type headerType, Type lineType, string backRef,
        List<(TemplateFieldKind Kind, string Value, bool IsLiteral)> pairs)
    {
        var slots = new List<InvoiceSlot>();
        var problems = new List<string>();

        foreach (var (kind, value, isLiteral) in pairs)
        {
            if (isLiteral) { slots.Add(new InvoiceSlot(kind, value, true)); continue; }

            var isLine = InvoiceReportBuilder.LineSlots.Contains(kind);
            var root = isLine ? lineType : headerType;
            var canonical = ResolvePath(root, value, out var error);

            // „Faktura.NumerFaktury" podane przy encji Faktura — zdejmujemy zbedny prefiks.
            if (canonical == null && !isLine)
            {
                var segments = value.Split('.', 2);
                if (segments.Length == 2
                    && string.Equals(segments[0].Trim(), headerType.Name, StringComparison.OrdinalIgnoreCase))
                    canonical = ResolvePath(headerType, segments[1], out error);
            }

            if (canonical == null)
            {
                problems.Add($"Slot '{kind}' ({(isLine ? "line item" : "header")}) — path '{value}' "
                             + $"does not resolve on '{root.Name}': {error}");
                continue;
            }

            var fieldType = ResolvePathType(root, canonical) ?? typeof(string);
            if (NumericSlots.Contains(kind) && !IsNumericType(fieldType))
                problems.Add($"Slot '{kind}' needs a numeric field, but '{canonical}' on '{root.Name}' is {fieldType.Name}.");
            else if (DateSlots.Contains(kind) && (Nullable.GetUnderlyingType(fieldType) ?? fieldType) != typeof(DateTime))
                problems.Add($"Slot '{kind}' needs a DateTime field, but '{canonical}' on '{root.Name}' is {fieldType.Name}.");
            else
                slots.Add(new InvoiceSlot(kind, canonical, false));
        }

        return new InvoiceShape(headerType, lineType, backRef, slots, problems);
    }

    [Description("Build a PROFESSIONAL INVOICE document. The report is built OVER THE INVOICE HEADER entity " +
                 "(one record = one invoice), so it can be printed straight from an invoice record and shows up " +
                 "in the 'Show in Report' action on the invoice views. Layout: document header (seller, buyer, " +
                 "number, dates) with a page break before every invoice but the first, then a subreport with the " +
                 "line items, then a VAT-rate summary table. " +
                 "Prefer this over `build_report` whenever the user asks for an invoice, bill or order confirmation. " +
                 "You map entity fields onto named slots; you do NOT design a layout. " +
                 "Header slots (paths relative to the HEADER entity): VendorName, VendorContactName, VendorAddress, " +
                 "VendorCity, VendorCountry, VendorWebsite, VendorEmail, VendorPhone, CustomerName, " +
                 "CustomerContactName, CustomerAddress, CustomerCity, CustomerCountry, InvoiceNumber, InvoiceDate, " +
                 "InvoiceDueDate, Subtotal, DiscountTotal, TaxTotal, Total. " +
                 "Line-item slots (paths relative to the LINE ITEM entity, which the tool finds by itself): " +
                 "ProductName, ProductDescription, Quantity, UnitPrice, UnitDiscount, UnitTax, Discount, Tax, " +
                 "DiscountLineTotal, TaxLineTotal, LineTotal. " +
                 "Note: `Tax` and `Discount` are RATES (23 meaning 23%) and print without a currency symbol; " +
                 "for the VAT or discount AMOUNT of a line use `TaxLineTotal` / `DiscountLineTotal`.")]
    private string BuildInvoiceReport(
        [Description("The INVOICE HEADER entity, e.g. 'Faktura'. One record = one invoice. " +
                     "The tool finds the line-item entity by itself (a runtime entity referencing this one). " +
                     "Passing the line-item entity instead still works — the tool then walks up the reference — " +
                     "but the header entity is what makes the report printable from an invoice record.")] string entityName,
        [Description("Slot-to-field mapping, 'Slot=FieldPath' separated by ';'. Header slots use paths relative to " +
                     "the header entity, line-item slots paths relative to the line-item entity. Dotted paths across " +
                     "references are allowed. Example: 'InvoiceNumber=NumerFaktury;InvoiceDate=DataWystawienia;" +
                     "CustomerName=Customer.NazwaKlienta;ProductName=OpisPozycji;Quantity=Ilosc;UnitPrice=CenaJednostkowa;" +
                     "LineTotal=WartoscNetto'.")] string mapping,
        [Description("Fixed text for slots that are not in the data, 'Slot=text' separated by ';'. " +
                     "Typically the seller: 'VendorName=Moja Firma Sp. z o.o.;VendorCity=Katowice'. Optional.")] string literals = null,
        [Description("Ignored — kept for backwards compatibility. The layout no longer comes from the DevExpress " +
                     "invoice templates, because those build a flat report over the line items and cannot carry " +
                     "a header-entity data source.")] string templateName = "Invoice1",
        [Description("DevExpress criteria limiting which records are rendered as samples, evaluated against the " +
                     "entity given in entityName, e.g. \"NumerFaktury = 'FV/2026/08/001'\". It is NOT stored in the " +
                     "saved report. Optional.")] string filterCriteria = null,
        [Description("Currency symbol shown next to amounts. Default 'zl'.")] string currencySymbol = "zl",
        [Description("Report name saved to the Reports list. Optional.")] string title = null,
        [Description("Also render sample documents to image files so the user can see the result.")] bool render = true,
        [Description("Header field the invoices are ordered by on the printout, e.g. 'NumerFaktury'. Optional.")] string documentKeyField = null,
        [Description("How many sample invoices to render. Default 1.")] int sampleCount = 1,
        [Description("VAT summary table — path on the LINE ITEM entity to the VAT rate label, " +
                     "e.g. 'StawkaVat.SymbolStawki'. Give all four vat* paths to get the table. Optional.")] string vatRateField = null,
        [Description("VAT summary table — path on the LINE ITEM entity to the net amount, e.g. 'WartoscNetto'.")] string vatNetField = null,
        [Description("VAT summary table — path on the LINE ITEM entity to the VAT amount, e.g. 'WartoscVat'.")] string vatAmountField = null,
        [Description("VAT summary table — path on the LINE ITEM entity to the gross amount, e.g. 'WartoscBrutto'.")] string vatGrossField = null)
    {
        _logger.LogInformation(
            "[Tool:build_invoice_report] Called with entity={Entity}, mapping={Mapping}, literals={Literals}, filter={Filter}, render={Render}, order={Key}, samples={Samples}, vat={Vat}",
            entityName, mapping, literals, filterCriteria, render, documentKeyField, sampleCount, vatRateField);
        try
        {
            var given = ResolveRuntimeType(entityName, out var typeError);
            if (given == null) { _logger.LogWarning("[Tool:build_invoice_report] Unknown entity"); return typeError; }

            // --- mapowanie na sloty -------------------------------------------------
            var pairs = new List<(TemplateFieldKind Kind, string Value, bool IsLiteral)>();
            var slotProblems = new List<string>();
            foreach (var (slotName, fieldPath) in ParsePairs(mapping))
            {
                if (!Enum.TryParse<TemplateFieldKind>(slotName, ignoreCase: true, out var kind) || kind == TemplateFieldKind.None)
                    slotProblems.Add($"Unknown slot '{slotName}'. Valid slots: "
                                     + string.Join(", ", Enum.GetNames<TemplateFieldKind>().Where(n => n != "None")));
                else pairs.Add((kind, fieldPath, false));
            }
            foreach (var (slotName, text) in ParsePairs(literals))
            {
                if (!Enum.TryParse<TemplateFieldKind>(slotName, ignoreCase: true, out var kind) || kind == TemplateFieldKind.None)
                    slotProblems.Add($"Unknown literal slot '{slotName}'.");
                else pairs.Add((kind, text, true));
            }

            if (pairs.Count == 0)
                return "Refusing to build the invoice:\n"
                       + "MISSING mapping: the user has not said which field feeds which slot.\n"
                       + DescribeShapeCandidates(given)
                       + "\nAsk the user about the MISSING item(s) — one clear question at a time. Do NOT guess.";
            if (slotProblems.Count > 0)
                return "Refusing to build the invoice:\n"
                       + string.Join("\n", slotProblems.Select(p => "PROBLEM: " + p))
                       + "\nFix the PROBLEM(s) and call this tool again.";

            // --- czy podano naglowek, czy pozycje? ----------------------------------
            var attempts = new List<InvoiceShape>();

            // A) entityName to NAGLOWEK — szukamy encji dzieci wsrod typow runtime.
            foreach (var (child, backRef) in FindChildEntities(given))
                attempts.Add(TryShape(given, child, backRef, pairs));

            // B) entityName to POZYCJA (stary kontrakt) — naglowkiem jest jego referencja.
            foreach (var reference in GetReferenceProperties(given))
                attempts.Add(TryShape(reference.PropertyType, given, reference.Name, pairs));

            var shape = attempts.FirstOrDefault(a => a.IsValid);
            if (shape == null)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Refusing to build the invoice — the mapping does not fit any header/line-item pair.");
                sb.AppendLine();
                sb.AppendLine(DescribeShapeCandidates(given));
                sb.AppendLine();
                foreach (var attempt in attempts.Take(6))
                {
                    sb.AppendLine($"Tried header='{attempt.HeaderType.Name}', line items='{attempt.LineType.Name}' "
                                  + $"(joined by {attempt.LineType.Name}.{attempt.BackReferencePath}):");
                    foreach (var p in attempt.Problems) sb.AppendLine($"  PROBLEM: {p}");
                }
                if (attempts.Count == 0)
                    sb.AppendLine($"PROBLEM: '{given.Name}' has no child entity and no reference to a parent, "
                                  + "so it cannot be one half of an invoice.");
                sb.AppendLine();
                sb.AppendLine("Fix the field paths and call this tool again — header slots take paths on the header "
                              + "entity, line-item slots paths on the line-item entity. Do NOT switch to `build_report`.");
                _logger.LogWarning("[Tool:build_invoice_report] Refused — no shape fits");
                return sb.ToString();
            }

            var headerType = shape.HeaderType;
            var lineType = shape.LineType;
            _logger.LogInformation("[Tool:build_invoice_report] Shape: header={Header}, lines={Line}, backRef={BackRef}",
                headerType.Name, lineType.Name, shape.BackReferencePath);

            // --- tabelka stawek VAT -------------------------------------------------
            VatSummarySpec vat = null;
            var vatNote = (string)null;
            var vatPaths = new[] { vatRateField, vatNetField, vatAmountField, vatGrossField };
            if (vatPaths.All(p => !string.IsNullOrWhiteSpace(p)))
            {
                var resolved = new string[4];
                var vatProblems = new List<string>();
                for (var i = 0; i < 4; i++)
                {
                    resolved[i] = ResolvePath(lineType, vatPaths[i], out var err);
                    if (resolved[i] == null) vatProblems.Add($"vat field '{vatPaths[i]}': {err}");
                }
                if (vatProblems.Count > 0)
                    return "Refusing to build the invoice:\n"
                           + string.Join("\n", vatProblems.Select(p => "PROBLEM: " + p))
                           + $"\nThe VAT summary paths must resolve on the LINE ITEM entity '{lineType.Name}'.";
                vat = new VatSummarySpec(resolved[0], resolved[1], resolved[2], resolved[3]);
            }
            else if (vatPaths.Any(p => !string.IsNullOrWhiteSpace(p)))
            {
                return "Refusing to build the invoice:\n"
                       + "PROBLEM: the VAT summary table needs all four paths — vatRateField, vatNetField, "
                       + "vatAmountField, vatGrossField — or none of them.\n"
                       + $"Fields on '{lineType.Name}': "
                       + string.Join(", ", GetReportableProperties(lineType).Select(p => p.Name))
                       + "; references: " + string.Join(", ", GetReferenceProperties(lineType).Select(p => p.Name));
            }
            else
            {
                vatNote = $"No VAT summary table — pass vatRateField, vatNetField, vatAmountField and vatGrossField "
                          + $"(paths on '{lineType.Name}') to add one. Fields available: "
                          + string.Join(", ", GetReportableProperties(lineType).Select(p => p.Name))
                          + "; references: " + string.Join(", ", GetReferenceProperties(lineType).Select(p => p.Name)) + ".";
            }

            string orderByPath = null;
            if (!string.IsNullOrWhiteSpace(documentKeyField))
            {
                orderByPath = ResolvePath(headerType, documentKeyField, out var orderError);
                if (orderByPath == null)
                {
                    var stripped = documentKeyField.Split('.', 2);
                    if (stripped.Length == 2 && string.Equals(stripped[0].Trim(), headerType.Name, StringComparison.OrdinalIgnoreCase))
                        orderByPath = ResolvePath(headerType, stripped[1], out orderError);
                }
                if (orderByPath == null) return $"documentKeyField: {orderError}";
            }

            var reportTitle = string.IsNullOrWhiteSpace(title) ? $"Faktura — {headerType.Name}" : title.Trim();
            var currency = string.IsNullOrWhiteSpace(currencySymbol) ? "zl" : currencySymbol.Trim();

            var output = new StringBuilder();
            output.AppendLine($"**{reportTitle}**");
            output.AppendLine();
            output.AppendLine($"- Header entity (report data source): `{headerType.Name}`");
            output.AppendLine($"- Line items: `{lineType.Name}`, joined by `{lineType.Name}.{shape.BackReferencePath}`");
            output.AppendLine();
            output.AppendLine("Mapowanie slotów:");
            foreach (var s in shape.Slots)
                output.AppendLine(s.IsLiteral ? $"- {s.Kind} = \"{s.Value}\"" : $"- {s.Kind} <- {s.Value}");
            output.AppendLine();

            // --- zapis layoutu ------------------------------------------------------
            // Zrodlem jest CollectionDataSource (master i OBA podraporty), nie zywa lista —
            // inaczej zapisany blob niesie typy CLR, ktorych deserializacja jest zabroniona.
            string savedKey;
            {
                var forSave = InvoiceReportBuilder.Build(
                    new CollectionDataSource { ObjectTypeName = headerType.FullName },
                    new CollectionDataSource { ObjectTypeName = lineType.FullName },
                    shape.BackReferencePath, shape.Slots, vat, reportTitle, currency, orderByPath);
                forSave.Name = reportTitle;

                using var saveScope = CreateObjectSpaceForType(typeof(DevExpress.Persistent.BaseImpl.ReportDataV2));
                var reportData = saveScope.Os.CreateObject<DevExpress.Persistent.BaseImpl.ReportDataV2>();
                reportData.DisplayName = reportTitle;
                var storage = DevExpress.ExpressApp.ReportsV2.ReportDataProvider.GetReportStorage(saveScope.ServiceProvider);
                if (storage == null) return "Error: report storage is not available.";
                storage.SaveReport(reportData, forSave);
                // Patrz build_report: bez IsInplaceReport raport nie pojawi sie w „Pokaz na raporcie".
                reportData.IsInplaceReport = true;
                saveScope.Os.CommitChanges();
                savedKey = saveScope.Os.GetKeyValue(reportData)?.ToString();
                _logger.LogInformation(
                    "[Tool:build_invoice_report] Saved ReportDataV2 key={Key}, DataTypeName='{DataType}'",
                    savedKey, reportData.DataTypeName);
                forSave.Dispose();

                // Kontrola po zapisie: czy podraporty przezyly serializacje?
                try
                {
                    var reloaded = storage.LoadReport(reportData);
                    foreach (var band in reloaded.Bands.OfType<Band>())
                        foreach (var sub in band.Controls.OfType<XRSubreport>())
                            _logger.LogInformation(
                                "[Tool:build_invoice_report] Round-trip {Name}: source={Source}, ds={Ds}, filter='{Filter}', bindings={Bindings}",
                                sub.Name, sub.ReportSource == null ? "NULL" : "ok",
                                (sub.ReportSource?.DataSource as CollectionDataSource)?.ObjectTypeName ?? "none",
                                sub.ReportSource?.FilterString, sub.ParameterBindings.Count);
                    reloaded.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Tool:build_invoice_report] Round-trip check failed");
                }
            }
            output.AppendLine($"Zapisano do listy Raporty (klucz `{savedKey}`), źródło danych `{headerType.Name}`, "
                              + "oznaczony jako inplace. Odśwież stronę (F5), żeby raport pojawił się w akcji "
                              + "„Pokaż na raporcie” na widoku faktur.");

            // --- render próbek ------------------------------------------------------
            if (render)
            {
                output.AppendLine();
                var file = RenderInvoiceSamples(headerType, lineType, shape, vat, reportTitle, currency,
                    orderByPath, filterCriteria, Math.Max(1, sampleCount), output);
                _logger.LogInformation("[Tool:build_invoice_report] Rendered sample to {File}", file ?? "<none>");
            }

            if (vatNote != null)
            {
                output.AppendLine();
                output.AppendLine($"NOTE: {vatNote}");
            }
            if (!string.IsNullOrWhiteSpace(templateName) && !string.Equals(templateName, "Invoice1", StringComparison.OrdinalIgnoreCase))
                output.AppendLine($"NOTE: templateName='{templateName}' was ignored — the layout is built over the header entity now.");

            _logger.LogInformation("[Tool:build_invoice_report] Done — {Slots} slot(s), vat={Vat}", shape.Slots.Count, vat != null);
            return output.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Tool:build_invoice_report] Error");
            return $"Error building invoice: {ex.Message}";
        }
    }

    /// <summary>Podpowiedz dla modelu: jakie pary naglowek/pozycje wchodza w gre dla danej encji.</summary>
    private static string DescribeShapeCandidates(Type given)
    {
        var sb = new StringBuilder();
        var children = FindChildEntities(given);
        if (children.Count > 0)
        {
            sb.AppendLine($"'{given.Name}' looks like an invoice HEADER. Line-item candidates: "
                          + string.Join(", ", children.Select(c => $"{c.Child.Name} (via {c.Child.Name}.{c.BackRef})")));
            sb.AppendLine($"Header fields on '{given.Name}': "
                          + string.Join(", ", GetReportableProperties(given).Select(p => p.Name))
                          + "; references: " + string.Join(", ", GetReferenceProperties(given).Select(p => p.Name)));
            foreach (var (child, _) in children)
                sb.AppendLine($"Line-item fields on '{child.Name}': "
                              + string.Join(", ", GetReportableProperties(child).Select(p => p.Name))
                              + "; references: " + string.Join(", ", GetReferenceProperties(child).Select(p => p.Name)));
        }
        else
        {
            sb.AppendLine($"'{given.Name}' has no child entity, so it can only be the LINE ITEM side. "
                          + "Header candidates (its references): "
                          + string.Join(", ", GetReferenceProperties(given).Select(p => $"{p.Name} -> {p.PropertyType.Name}")));
            sb.AppendLine($"Fields on '{given.Name}': "
                          + string.Join(", ", GetReportableProperties(given).Select(p => p.Name)));
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Renderuje probki na zywych danych i dopisuje sciezki plikow do <paramref name="output"/>.</summary>
    private string RenderInvoiceSamples(
        Type headerType, Type lineType, InvoiceShape shape, VatSummarySpec vat,
        string reportTitle, string currency, string orderByPath,
        string filterCriteria, int sampleCount, StringBuilder output)
    {
        using var scope = CreateObjectSpaceForType(headerType);

        List<object> headers;
        try
        {
            CriteriaOperator criteria = string.IsNullOrWhiteSpace(filterCriteria)
                ? null : CriteriaOperator.Parse(filterCriteria);

            // Filtr moze byc napisany wzgledem encji, ktora podal wywolujacy — jesli to pozycje,
            // wyciagamy z nich zbior naglowkow, zeby stary sposob wywolania dalej dzialal.
            if (criteria is not null && FilterTargetsLineEntity(filterCriteria, headerType, lineType))
            {
                var lines = scope.Os.GetObjects(lineType, criteria).Cast<object>().ToList();
                headers = lines.Select(l => ReadPath(l, shape.BackReferencePath))
                    .Where(h => h != null).Distinct().ToList();
            }
            else
            {
                headers = scope.Os.GetObjects(headerType, criteria).Cast<object>().ToList();
            }
        }
        catch (Exception ex)
        {
            output.AppendLine($"- render pominięty: filtr `{filterCriteria}` nie zadziałał — {ex.Message}");
            return null;
        }

        if (headers.Count == 0)
        {
            output.AppendLine($"- render pominięty: żaden rekord '{headerType.Name}' nie pasuje do filtru.");
            return null;
        }

        var sample = headers.Take(sampleCount).ToList();
        var keys = sample.Select(h => scope.Os.GetKeyValue(h)).ToList();
        var lineRows = scope.Os
            .GetObjects(lineType, new InOperator($"{shape.BackReferencePath}.Oid", keys))
            .Cast<object>().ToList();

        var target = InvoiceReportBuilder.Build(sample, lineRows, shape.BackReferencePath,
            shape.Slots, vat, reportTitle, currency, orderByPath);
        try
        {
            target.CreateDocument();
            if (target.Pages.Count == 0)
            {
                output.AppendLine("- dokument pusty (0 stron).");
                return null;
            }
            var stamp = DateTime.Now.ToString("HHmmss");
            var pdf = Path.Combine(RenderOutputDirectory, $"faktura-{headerType.Name}-{stamp}.pdf");
            target.ExportToPdf(pdf);
            var png = Path.Combine(RenderOutputDirectory, $"faktura-{headerType.Name}-{stamp}.png");
            target.ExportToImage(png, DevExpress.Drawing.DXImageFormat.Png);
            output.AppendLine($"### Wyrenderowane dokumenty ({sample.Count} z {headers.Count}), "
                              + $"{lineRows.Count} pozycji, {target.Pages.Count} str.");
            output.AppendLine($"- PDF: `{pdf}`");
            output.AppendLine($"- PNG: `{png}`");
            return pdf;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Tool:build_invoice_report] Render failed");
            output.AppendLine($"- render nie powiódł się: {ex.Message}");
            return null;
        }
        finally { target.Dispose(); }
    }

    /// <summary>Czy filtr odwoluje sie do pola, ktore istnieje tylko na encji pozycji?</summary>
    private static bool FilterTargetsLineEntity(string filterCriteria, Type headerType, Type lineType)
    {
        var first = System.Text.RegularExpressions.Regex.Match(filterCriteria ?? string.Empty, @"\[?([A-Za-z_][\w\.]*)\]?");
        if (!first.Success) return false;
        var path = first.Groups[1].Value;
        return ResolvePath(headerType, path, out _) is null && ResolvePath(lineType, path, out _) is not null;
    }

    // ==========================================================================
    // JSON DTOs for tool parameters
    // ==========================================================================

    private sealed class FieldDefinition
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public bool Required { get; set; }
        public string ReferencedClass { get; set; }
        public string Description { get; set; }
    }

    private sealed class ModificationsPayload
    {
        public List<FieldDefinition> AddFields { get; set; }
        public List<string> RemoveFields { get; set; }
        public List<FieldDefinition> UpdateFields { get; set; }
        public string NavigationGroup { get; set; }
        public string Description { get; set; }
        public bool? IsApiExposed { get; set; }
    }
}
