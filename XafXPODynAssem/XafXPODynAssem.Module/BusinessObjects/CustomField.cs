using System.ComponentModel;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.Model;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Persistent.Validation;
using DevExpress.Xpo;
using XafXPODynAssem.Module.Services;
using XafXPODynAssem.Module.Validation;

namespace XafXPODynAssem.Module.BusinessObjects
{
    /// <summary>
    /// Typy pola widziane przez uzytkownika. Persystentny pozostaje string TypeName
    /// (uzywa go GraduationService przy generowaniu kodu), a ten enum jest tylko
    /// nakladka UI konwertowana w obie strony.
    /// </summary>
    public enum CustomFieldType
    {
        Text,
        Integer,
        LongInteger,
        Decimal,
        Double,
        Single,
        Boolean,
        DateTime,
        Guid,
        ByteArray,
        Reference
    }

    [DefaultClassOptions]
    [NavigationItem("Zarządzanie schematem")]
    [DefaultProperty(nameof(FieldName))]
    [XafDisplayName("Pole użytkownika")]
    public class CustomField : BaseObject
    {
        public CustomField(Session session) : base(session) { }

        CustomClass customClass;
        [Association("CustomClass-Fields")]
        [XafDisplayName("Klasa")]
        public CustomClass CustomClass
        {
            get => customClass;
            set => SetPropertyValue(nameof(CustomClass), ref customClass, value);
        }

        string fieldName;
        [XafDisplayName("Nazwa pola")]
        public string FieldName
        {
            get => fieldName;
            set => SetPropertyValue(nameof(FieldName), ref fieldName, value);
        }

        string typeName = "System.String";
        // Edytowane przez FieldType (enum) ponizej. Zostaje widoczne i tylko do odczytu,
        // bo to ta wartosc trafia do generatora kodu i do narzedzi AI.
        [ModelDefault("AllowEdit", "False")]
        [XafDisplayName("Typ CLR")]
        public string TypeName
        {
            get => typeName;
            set
            {
                if (SetPropertyValue(nameof(TypeName), ref typeName, value))
                {
                    OnChanged(nameof(FieldType));
                }
            }
        }

        /// <summary>Wybor typu z listy. Konwertuje w obie strony na <see cref="TypeName"/>.</summary>
        [NonPersistent]
        [ImmediatePostData]
        [XafDisplayName("Typ pola")]
        public CustomFieldType FieldType
        {
            get => FromTypeName(TypeName);
            set
            {
                var clr = ToTypeName(value);
                if (!string.Equals(TypeName, clr, StringComparison.Ordinal))
                {
                    TypeName = clr;
                }
            }
        }

        static string ToTypeName(CustomFieldType t) => t switch
        {
            CustomFieldType.Text => "System.String",
            CustomFieldType.Integer => "System.Int32",
            CustomFieldType.LongInteger => "System.Int64",
            CustomFieldType.Decimal => "System.Decimal",
            CustomFieldType.Double => "System.Double",
            CustomFieldType.Single => "System.Single",
            CustomFieldType.Boolean => "System.Boolean",
            CustomFieldType.DateTime => "System.DateTime",
            CustomFieldType.Guid => "System.Guid",
            CustomFieldType.ByteArray => "System.Byte[]",
            CustomFieldType.Reference => "Reference",
            _ => "System.String"
        };

        // Wartosc spoza listy (np. wpisana wczesniej recznie) mapuje sie na Text,
        // ale TypeName zostaje nietkniety dopoki uzytkownik czegos nie wybierze.
        static CustomFieldType FromTypeName(string name) => name switch
        {
            "System.String" => CustomFieldType.Text,
            "System.Int32" => CustomFieldType.Integer,
            "System.Int64" => CustomFieldType.LongInteger,
            "System.Decimal" => CustomFieldType.Decimal,
            "System.Double" => CustomFieldType.Double,
            "System.Single" => CustomFieldType.Single,
            "System.Boolean" => CustomFieldType.Boolean,
            "System.DateTime" => CustomFieldType.DateTime,
            "System.Guid" => CustomFieldType.Guid,
            "System.Byte[]" => CustomFieldType.ByteArray,
            "Reference" => CustomFieldType.Reference,
            _ => CustomFieldType.Text
        };

        bool isRequired;
        [XafDisplayName("Wymagane")]
        public bool IsRequired
        {
            get => isRequired;
            set => SetPropertyValue(nameof(IsRequired), ref isRequired, value);
        }

        bool isDefaultField;
        [XafDisplayName("Pole domyślne")]
        public bool IsDefaultField
        {
            get => isDefaultField;
            set => SetPropertyValue(nameof(IsDefaultField), ref isDefaultField, value);
        }

        string description;
        [XafDisplayName("Opis")]
        public string Description
        {
            get => description;
            set => SetPropertyValue(nameof(Description), ref description, value);
        }

        string referencedClassName;
        [XafDisplayName("Klasa referencyjna")]
        public string ReferencedClassName
        {
            get => referencedClassName;
            set => SetPropertyValue(nameof(ReferencedClassName), ref referencedClassName, value);
        }

        int sortOrder;
        [XafDisplayName("Kolejność")]
        public int SortOrder
        {
            get => sortOrder;
            set => SetPropertyValue(nameof(SortOrder), ref sortOrder, value);
        }

        bool isImmediatePostData;
        [XafDisplayName("Natychmiastowy zapis")]
        public bool IsImmediatePostData
        {
            get => isImmediatePostData;
            set => SetPropertyValue(nameof(IsImmediatePostData), ref isImmediatePostData, value);
        }

        int? stringMaxLength;
        [XafDisplayName("Maks. długość tekstu")]
        public int? StringMaxLength
        {
            get => stringMaxLength;
            set => SetPropertyValue(nameof(StringMaxLength), ref stringMaxLength, value);
        }

        bool isVisibleInListView = true;
        [XafDisplayName("Widoczne na liście")]
        public bool IsVisibleInListView
        {
            get => isVisibleInListView;
            set => SetPropertyValue(nameof(IsVisibleInListView), ref isVisibleInListView, value);
        }

        bool isVisibleInDetailView = true;
        [XafDisplayName("Widoczne w szczegółach")]
        public bool IsVisibleInDetailView
        {
            get => isVisibleInDetailView;
            set => SetPropertyValue(nameof(IsVisibleInDetailView), ref isVisibleInDetailView, value);
        }

        bool isEditable = true;
        [XafDisplayName("Edytowalne")]
        public bool IsEditable
        {
            get => isEditable;
            set => SetPropertyValue(nameof(IsEditable), ref isEditable, value);
        }

        string toolTip;
        [XafDisplayName("Podpowiedź")]
        public string ToolTip
        {
            get => toolTip;
            set => SetPropertyValue(nameof(ToolTip), ref toolTip, value);
        }

        string displayName;
        [XafDisplayName("Etykieta")]
        public string DisplayName
        {
            get => displayName;
            set => SetPropertyValue(nameof(DisplayName), ref displayName, value);
        }

#pragma warning disable XAF0020
        [NonPersistent]
        [RuleFromBoolProperty("CustomField_ValidFieldName", DefaultContexts.Save,
            "Field Name must be a valid C# identifier (letters, digits, underscores; cannot start with a digit).")]
        [Browsable(false)]
        public bool IsFieldNameValid => !string.IsNullOrWhiteSpace(FieldName) && CustomFieldValidation.IsValidIdentifier(FieldName);

        [NonPersistent]
        [RuleFromBoolProperty("CustomField_NotReservedField", DefaultContexts.Save,
            "Field Name is reserved (Oid, ObjectType, GCRecord, OptimisticLockField).")]
        [Browsable(false)]
        public bool IsFieldNameNotReserved => string.IsNullOrWhiteSpace(FieldName) || !CustomFieldValidation.IsReservedFieldName(FieldName);

        [NonPersistent]
        [RuleFromBoolProperty("CustomField_ValidTypeName", DefaultContexts.Save,
            "Type Name must be a supported CLR type (or 'Reference' with a Referenced Class Name).")]
        [Browsable(false)]
        public bool IsTypeNameValid => string.IsNullOrWhiteSpace(TypeName) || SupportedTypes.IsSupported(TypeName);

        [NonPersistent]
        [RuleFromBoolProperty("CustomField_ReferenceRequiresClass", DefaultContexts.Save,
            "A Reference field requires a Referenced Class Name.")]
        [Browsable(false)]
        public bool IsReferenceClassValid => TypeName != "Reference" || !string.IsNullOrWhiteSpace(ReferencedClassName);
#pragma warning restore XAF0020
    }
}
