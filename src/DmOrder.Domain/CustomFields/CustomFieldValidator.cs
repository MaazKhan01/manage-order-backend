using System.Globalization;

namespace DmOrder.Domain.CustomFields;

/// <summary>
/// Validates a customer's answers against the seller's questions.
///
/// This runs on input from an anonymous, public form, so nothing here trusts the shape of what
/// arrived: an answer to a field the seller never defined is dropped rather than stored, a "number"
/// that is not a number is rejected rather than coerced, and a choice that is not on the seller's
/// list is refused even though the form only offered valid ones.
///
/// It lives in the Domain because "is this a valid answer" is the seller's rule, not the HTTP layer's.
/// </summary>
public static class CustomFieldValidator
{
    /// <summary>Cap on free-text answers when the seller set no limit of their own.</summary>
    public const int DefaultMaxTextLength = 500;
    public const int DefaultMaxLongTextLength = 4000;

    public static CustomFieldValidationResult Validate(
        IReadOnlyList<CustomField> fields,
        IReadOnlyList<CustomFieldAnswer> answers)
    {
        var validated = new List<ValidatedAnswer>();
        var errors = new List<FieldValidationError>();

        // Driven by the seller's fields, not by what was submitted. Answers to fields that do not
        // exist are ignored entirely — a hand-crafted payload cannot add data to an order.
        foreach (var field in fields.Where(f => !f.IsDeleted).OrderBy(f => f.DisplayOrder))
        {
            var answer = answers.FirstOrDefault(a => a.FieldId == field.Id);
            var result = ValidateField(field, answer);

            if (result.Error is not null)
            {
                errors.Add(new FieldValidationError(field.Id, field.Label, result.Error));
            }
            else if (result.Answer is not null)
            {
                validated.Add(result.Answer);
            }
        }

        return new CustomFieldValidationResult(validated, errors);
    }

    private static (ValidatedAnswer? Answer, string? Error) ValidateField(
        CustomField field,
        CustomFieldAnswer? answer)
    {
        var raw = answer?.Value?.Trim();
        var hasValue = field.FieldType switch
        {
            CustomFieldType.MultiSelect => answer?.Values is { Count: > 0 },
            CustomFieldType.Image => answer?.MediaId is not null,
            _ => !string.IsNullOrWhiteSpace(raw),
        };

        if (!hasValue)
        {
            return field.IsRequired
                ? (null, $"{field.Label} is required.")
                : (null, null);
        }

        return field.FieldType switch
        {
            CustomFieldType.Text => ValidateText(field, raw!, field.MaxLength ?? DefaultMaxTextLength),
            CustomFieldType.LongText => ValidateText(field, raw!, field.MaxLength ?? DefaultMaxLongTextLength),
            CustomFieldType.Number => ValidateNumber(field, raw!),
            CustomFieldType.Select => ValidateSelect(field, raw!),
            CustomFieldType.MultiSelect => ValidateMultiSelect(field, answer!.Values!),
            CustomFieldType.Boolean => ValidateBoolean(field, raw!),
            CustomFieldType.Date => ValidateDate(field, raw!),
            CustomFieldType.Time => ValidateTime(field, raw!),
            CustomFieldType.Image => (Answer(field, mediaId: answer!.MediaId), null),
            _ => (null, $"{field.Label} could not be read."),
        };
    }

    private static (ValidatedAnswer?, string?) ValidateText(CustomField field, string value, int maxLength)
    {
        if (value.Length > maxLength)
        {
            return (null, $"{field.Label} must be {maxLength} characters or fewer.");
        }

        return (Answer(field, text: value), null);
    }

    private static (ValidatedAnswer?, string?) ValidateNumber(CustomField field, string value)
    {
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
        {
            return (null, $"{field.Label} must be a number.");
        }

        if (field.MinValue is { } min && number < min)
        {
            return (null, $"{field.Label} must be {min} or more.");
        }

        if (field.MaxValue is { } max && number > max)
        {
            return (null, $"{field.Label} must be {max} or less.");
        }

        return (Answer(field, number: number), null);
    }

    private static (ValidatedAnswer?, string?) ValidateSelect(CustomField field, string value)
    {
        // Matched against the seller's list even though the form only offered valid options — the
        // form is a suggestion, the submission is untrusted.
        var option = field.Options.FirstOrDefault(o =>
            string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase));

        return option is null
            ? (null, $"Choose one of the available options for {field.Label}.")
            : (Answer(field, text: option.Value, choices: [option.Value]), null);
    }

    private static (ValidatedAnswer?, string?) ValidateMultiSelect(CustomField field, IReadOnlyList<string> values)
    {
        var matched = new List<string>();

        foreach (var value in values.Select(v => v.Trim()).Where(v => v.Length > 0))
        {
            var option = field.Options.FirstOrDefault(o =>
                string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase));

            if (option is null)
            {
                return (null, $"Choose only from the available options for {field.Label}.");
            }

            // De-duplicated on the *matched option*, not on the submitted string. "roses" and "Roses"
            // are the same choice, and storing it twice would show a customer asking for the same
            // thing two times.
            if (!matched.Contains(option.Value, StringComparer.Ordinal))
            {
                matched.Add(option.Value);
            }
        }

        if (matched.Count == 0)
        {
            return field.IsRequired
                ? (null, $"{field.Label} is required.")
                : (null, null);
        }

        return (Answer(field, text: string.Join(", ", matched), choices: matched), null);
    }

    private static (ValidatedAnswer?, string?) ValidateBoolean(CustomField field, string value)
    {
        // Accepts what a checkbox, a radio group and a JSON client each send.
        var normalised = value.ToLowerInvariant();

        return normalised switch
        {
            "true" or "yes" or "on" or "1" => (Answer(field, boolean: true), null),
            "false" or "no" or "off" or "0" => (Answer(field, boolean: false), null),
            _ => (null, $"{field.Label} must be yes or no."),
        };
    }

    private static (ValidatedAnswer?, string?) ValidateDate(CustomField field, string value)
    {
        if (!DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return (null, $"{field.Label} must be a date.");
        }

        return (Answer(field, date: date), null);
    }

    private static (ValidatedAnswer?, string?) ValidateTime(CustomField field, string value)
    {
        if (!TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return (null, $"{field.Label} must be a time.");
        }

        return (Answer(field, time: time), null);
    }

    private static ValidatedAnswer Answer(
        CustomField field,
        string? text = null,
        decimal? number = null,
        DateOnly? date = null,
        TimeOnly? time = null,
        bool? boolean = null,
        IReadOnlyList<string>? choices = null,
        Guid? mediaId = null) =>
        new(field, text, number, date, time, boolean, choices, mediaId);
}
