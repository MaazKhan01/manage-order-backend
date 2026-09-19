namespace DmOrder.Domain.CustomFields;

/// <summary>
/// A raw answer as it arrived from a public order form.
///
/// Everything is a string or a list of strings because that is what an HTML form produces. Turning it
/// into a typed value is <see cref="CustomFieldValidator"/>'s job, and nothing downstream trusts it
/// until that has happened.
/// </summary>
public sealed record CustomFieldAnswer(Guid FieldId, string? Value, IReadOnlyList<string>? Values, Guid? MediaId);

/// <summary>A validated, typed answer, ready to be written to an order.</summary>
public sealed record ValidatedAnswer(
    CustomField Field,
    string? Text,
    decimal? Number,
    DateOnly? Date,
    TimeOnly? Time,
    bool? Boolean,
    IReadOnlyList<string>? Choices,
    Guid? MediaId);

public sealed record FieldValidationError(Guid FieldId, string Label, string Message);

public sealed record CustomFieldValidationResult(
    IReadOnlyList<ValidatedAnswer> Answers,
    IReadOnlyList<FieldValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
