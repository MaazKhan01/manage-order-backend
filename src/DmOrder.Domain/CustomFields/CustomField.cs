using DmOrder.Domain.Common;
using DmOrder.Domain.Exceptions;

namespace DmOrder.Domain.CustomFields;

/// <summary>
/// What a seller asks a customer when they order.
///
/// This is the mechanism that makes one platform serve a baker, a tailor, a florist and a jeweller
/// without a schema change per trade. "Flavour", "Eggless", "Bust", "Fabric", "Flower type" and
/// "Cake message" are all the same structure: a label, a type, and whether it is required.
///
/// Nothing in this file — or anywhere in the domain — knows what a cake is.
/// </summary>
public sealed class CustomField : Entity, ITenantOwned
{
    private readonly List<CustomFieldOption> _options = [];

    private CustomField() { }

    public Guid StoreId { get; private set; }

    /// <summary>
    /// Null means the field is asked on every order, whatever the product — a delivery date, say.
    /// Set means it is asked only for that product.
    /// </summary>
    public Guid? ProductId { get; private set; }

    public string Label { get; private set; } = null!;

    /// <summary>Shown under the input. Where a seller explains what they need and why.</summary>
    public string? HelpText { get; private set; }

    public CustomFieldType FieldType { get; private set; }

    public bool IsRequired { get; private set; }

    public int DisplayOrder { get; private set; }

    // Constraints. Which of these apply depends on FieldType; the rest are ignored.
    public decimal? MinValue { get; private set; }
    public decimal? MaxValue { get; private set; }
    public int? MaxLength { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public IReadOnlyList<CustomFieldOption> Options => _options;

    public bool RequiresOptions => FieldType is CustomFieldType.Select or CustomFieldType.MultiSelect;

    public static CustomField Create(
        Guid storeId,
        Guid? productId,
        string label,
        CustomFieldType fieldType,
        int displayOrder)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new BusinessRuleException("A question is required.");
        }

        if (!Enum.IsDefined(fieldType))
        {
            throw new BusinessRuleException($"'{fieldType}' is not a supported field type.");
        }

        return new CustomField
        {
            StoreId = storeId,
            ProductId = productId,
            Label = label.Trim(),
            FieldType = fieldType,
            DisplayOrder = displayOrder,
        };
    }

    public void Update(
        string label,
        string? helpText,
        CustomFieldType fieldType,
        bool isRequired,
        decimal? minValue,
        decimal? maxValue,
        int? maxLength)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new BusinessRuleException("A question is required.");
        }

        if (!Enum.IsDefined(fieldType))
        {
            throw new BusinessRuleException($"'{fieldType}' is not a supported field type.");
        }

        if (minValue is not null && maxValue is not null && minValue > maxValue)
        {
            throw new BusinessRuleException("The smallest value cannot be larger than the largest.");
        }

        if (maxLength is < 1)
        {
            throw new BusinessRuleException("The maximum length must be at least 1.");
        }

        Label = label.Trim();
        HelpText = string.IsNullOrWhiteSpace(helpText) ? null : helpText.Trim();
        FieldType = fieldType;
        IsRequired = isRequired;

        // Constraints only mean something for the types that use them. Clearing the rest keeps a
        // field that was switched from Number to Text from carrying a stale min/max.
        MinValue = fieldType is CustomFieldType.Number ? minValue : null;
        MaxValue = fieldType is CustomFieldType.Number ? maxValue : null;
        MaxLength = fieldType is CustomFieldType.Text or CustomFieldType.LongText ? maxLength : null;

        if (!RequiresOptions)
        {
            _options.Clear();
        }
    }

    public void SetDisplayOrder(int displayOrder) => DisplayOrder = displayOrder;

    /// <summary>
    /// Soft delete. Orders snapshot the label and type at submission, so past orders stay readable,
    /// but the row is kept so analytics can still group by it.
    /// </summary>
    public void Delete(DateTimeOffset now)
    {
        DeletedAt ??= now;
        IsRequired = false;
    }

    public CustomFieldOption AddOption(string label, string? value)
    {
        if (!RequiresOptions)
        {
            throw new BusinessRuleException("Only a choice question can have options.");
        }

        if (_options.Count >= MaxOptions)
        {
            throw new BusinessRuleException($"A question can have at most {MaxOptions} options.");
        }

        var option = CustomFieldOption.Create(Id, label, value, _options.Count);
        _options.Add(option);
        return option;
    }

    public void ReplaceOptions(IReadOnlyList<(string Label, string? Value)> options)
    {
        if (!RequiresOptions)
        {
            if (options.Count > 0)
            {
                throw new BusinessRuleException("Only a choice question can have options.");
            }

            _options.Clear();
            return;
        }

        if (options.Count == 0)
        {
            throw new BusinessRuleException("A choice question needs at least one option.");
        }

        if (options.Count > MaxOptions)
        {
            throw new BusinessRuleException($"A question can have at most {MaxOptions} options.");
        }

        var labels = options.Select(o => o.Label.Trim()).ToList();
        if (labels.Any(string.IsNullOrWhiteSpace))
        {
            throw new BusinessRuleException("Every option needs a label.");
        }

        if (labels.Distinct(StringComparer.OrdinalIgnoreCase).Count() != labels.Count)
        {
            throw new BusinessRuleException("Two options cannot have the same label.");
        }

        _options.Clear();

        var order = 0;
        foreach (var (label, value) in options)
        {
            _options.Add(CustomFieldOption.Create(Id, label, value, order++));
        }
    }

    public const int MaxOptions = 50;
}

public sealed class CustomFieldOption : Entity
{
    private CustomFieldOption() { }

    public Guid CustomFieldId { get; private set; }

    public string Label { get; private set; } = null!;

    /// <summary>
    /// What is stored when this option is chosen. Defaults to the label — sellers think in labels,
    /// and a separate machine value only matters if this ever integrates with something.
    /// </summary>
    public string Value { get; private set; } = null!;

    public int DisplayOrder { get; private set; }

    internal static CustomFieldOption Create(Guid customFieldId, string label, string? value, int displayOrder) =>
        new()
        {
            CustomFieldId = customFieldId,
            Label = label.Trim(),
            Value = string.IsNullOrWhiteSpace(value) ? label.Trim() : value.Trim(),
            DisplayOrder = displayOrder,
        };
}
