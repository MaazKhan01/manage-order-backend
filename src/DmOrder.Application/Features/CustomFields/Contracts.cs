using DmOrder.Domain.CustomFields;

namespace DmOrder.Application.Features.CustomFields;

public sealed record CustomFieldOptionRequest(string Label, string? Value);

public sealed record CreateCustomFieldRequest(
    Guid? ProductId,
    string Label,
    CustomFieldType FieldType);

public sealed record UpdateCustomFieldRequest(
    string Label,
    string? HelpText,
    CustomFieldType FieldType,
    bool IsRequired,
    decimal? MinValue,
    decimal? MaxValue,
    int? MaxLength,
    IReadOnlyList<CustomFieldOptionRequest> Options);

public sealed record CustomFieldOptionResponse(Guid Id, string Label, string Value, int DisplayOrder);

public sealed record CustomFieldResponse(
    Guid Id,
    Guid? ProductId,
    string? ProductName,
    string Label,
    string? HelpText,
    string FieldType,
    bool IsRequired,
    int DisplayOrder,
    decimal? MinValue,
    decimal? MaxValue,
    int? MaxLength,
    IReadOnlyList<CustomFieldOptionResponse> Options);

/// <summary>
/// A question as the public order form needs it. No internal ids beyond the field id the form must
/// send back, and no store-side configuration a customer has no business seeing.
/// </summary>
public sealed record PublicCustomFieldResponse(
    Guid Id,
    string Label,
    string? HelpText,
    string FieldType,
    bool IsRequired,
    decimal? MinValue,
    decimal? MaxValue,
    int? MaxLength,
    IReadOnlyList<string> Options);
