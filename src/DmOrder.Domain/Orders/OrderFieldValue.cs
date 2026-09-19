using DmOrder.Domain.Common;
using DmOrder.Domain.CustomFields;

namespace DmOrder.Domain.Orders;

/// <summary>
/// One answer to one of the seller's questions, as given at the time of ordering.
///
/// The label and type are **copied in**, not looked up. A seller who renames "Size" to "Dimensions",
/// changes it from a choice to free text, or deletes it entirely must not silently rewrite or
/// destroy what a customer actually said months ago. The FK is kept, nullable, for analytics only.
///
/// Typed columns rather than one string, so numbers and dates can be filtered and sorted later
/// without parsing.
/// </summary>
public sealed class OrderFieldValue : Entity
{
    private OrderFieldValue() { }

    public Guid OrderItemId { get; private set; }

    /// <summary>Null once the seller deletes the field. Rendering never depends on this.</summary>
    public Guid? CustomFieldId { get; private set; }

    public string LabelSnapshot { get; private set; } = null!;

    public CustomFieldType FieldTypeSnapshot { get; private set; }

    public int DisplayOrder { get; private set; }

    public string? ValueText { get; private set; }
    public decimal? ValueNumber { get; private set; }
    public DateOnly? ValueDate { get; private set; }
    public TimeOnly? ValueTime { get; private set; }
    public bool? ValueBoolean { get; private set; }

    /// <summary>Multi-select answers, stored as a JSON array of option values.</summary>
    public string? ValueJson { get; private set; }

    /// <summary>A customer's reference photo.</summary>
    public Guid? MediaId { get; private set; }

    public static OrderFieldValue Create(
        Guid orderItemId,
        CustomField field,
        int displayOrder)
    {
        return new OrderFieldValue
        {
            OrderItemId = orderItemId,
            CustomFieldId = field.Id,
            LabelSnapshot = field.Label,
            FieldTypeSnapshot = field.FieldType,
            DisplayOrder = displayOrder,
        };
    }

    public OrderFieldValue WithText(string? value)
    {
        ValueText = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return this;
    }

    public OrderFieldValue WithNumber(decimal? value)
    {
        ValueNumber = value;

        // Also stored as text so a slip or list can render every answer the same way without a
        // switch on type.
        ValueText = value?.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return this;
    }

    public OrderFieldValue WithDate(DateOnly? value)
    {
        ValueDate = value;
        ValueText = value?.ToString("yyyy-MM-dd");
        return this;
    }

    public OrderFieldValue WithTime(TimeOnly? value)
    {
        ValueTime = value;
        ValueText = value?.ToString("HH:mm");
        return this;
    }

    public OrderFieldValue WithBoolean(bool? value)
    {
        ValueBoolean = value;
        ValueText = value is null ? null : value.Value ? "Yes" : "No";
        return this;
    }

    public OrderFieldValue WithChoices(IReadOnlyList<string> values)
    {
        ValueJson = System.Text.Json.JsonSerializer.Serialize(values);
        ValueText = string.Join(", ", values);
        return this;
    }

    public OrderFieldValue WithMedia(Guid? mediaId)
    {
        MediaId = mediaId;
        return this;
    }
}
