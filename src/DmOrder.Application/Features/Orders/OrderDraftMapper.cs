using DmOrder.Application.Common.Interfaces;
using DmOrder.Domain.CustomFields;

namespace DmOrder.Application.Features.Orders;

/// <summary>
/// Turns what the reader found into what the seller sees.
///
/// Pure and separate from the handler on purpose: this is where a model's output stops being
/// suggestions and becomes references to this store's real rows, and that is the step worth being
/// able to test exhaustively without a database or a network call.
///
/// The rule it enforces: nothing the reader said is trusted as an identity. Names and labels are
/// matched against the store's own data, and anything that does not match is dropped or downgraded
/// to free text - never passed through.
/// </summary>
public static class OrderDraftMapper
{
    /// <summary>The same ceiling <see cref="ManualOrderValidator"/> enforces on submit.</summary>
    public const int MaxQuantity = 999;

    public static OrderDraftResponse Map(
        OrderDraft draft,
        IReadOnlyList<DraftProduct> products,
        IReadOnlyList<CustomField> questions,
        string? normalisedPhone,
        DateOnly today)
    {
        // The reader returns a product name. Resolving it to an id happens here, against this
        // store's catalogue, so an invented name becomes a one-off rather than a foreign product.
        var matched = string.IsNullOrWhiteSpace(draft.ProductName)
            ? null
            : products.FirstOrDefault(p =>
                string.Equals(p.Name, draft.ProductName.Trim(), StringComparison.OrdinalIgnoreCase));

        var productName = matched?.Name ?? Trimmed(draft.ProductName);

        return new OrderDraftResponse(
            ProductId: matched?.Id,
            ProductName: productName,
            Quantity: draft.Quantity is > 0 and <= MaxQuantity ? draft.Quantity : null,
            CustomerName: Trimmed(draft.CustomerName),
            CustomerPhone: normalisedPhone,
            DeliveryAddress: Trimmed(draft.DeliveryAddress),
            DeliveryDate: FutureOnly(draft.DeliveryDate, today),
            CustomerNote: Trimmed(draft.CustomerNote),
            Answers: MatchAnswers(draft.Answers, questions),
            Missing: MissingFields(matched?.Id, productName, Trimmed(draft.CustomerName), normalisedPhone),
            SuggestedReply: Trimmed(draft.SuggestedReply));
    }

    /// <summary>
    /// What the seller still has to supply, decided here rather than by the model.
    ///
    /// These are the rules <see cref="ManualOrderValidator"/> enforces on submit. Asking the model
    /// to judge completeness as well would put a second, drifting copy of them inside a prompt.
    /// </summary>
    private static List<string> MissingFields(
        Guid? productId,
        string? productName,
        string? customerName,
        string? phone)
    {
        var missing = new List<string>();

        if (productId is null && string.IsNullOrWhiteSpace(productName)) missing.Add("product");
        if (string.IsNullOrWhiteSpace(customerName)) missing.Add("customerName");
        if (string.IsNullOrWhiteSpace(phone)) missing.Add("customerPhone");

        return missing;
    }

    /// <summary>
    /// Matches answers to the seller's own questions by label, dropping anything else.
    /// </summary>
    private static List<DraftAnswerResponse> MatchAnswers(
        IReadOnlyList<DraftAnswer> answers,
        IReadOnlyList<CustomField> questions)
    {
        var matched = new List<DraftAnswerResponse>();

        foreach (var answer in answers)
        {
            if (string.IsNullOrWhiteSpace(answer.Value)) continue;

            var question = questions.FirstOrDefault(q =>
                string.Equals(q.Label, answer.QuestionLabel?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (question is null) continue;

            // One answer per question. A model that says "Small" and then "Large" has not given the
            // seller a choice to review, it has given them a bug.
            if (matched.Any(m => m.FieldId == question.Id)) continue;

            // An answer to a fixed-list question that is not on the list is dropped rather than
            // offered: the form could not accept it, so showing it only invites a save that fails.
            if (question.Options.Count > 0
                && !question.Options.Any(o =>
                    string.Equals(o.Value, answer.Value.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            matched.Add(new DraftAnswerResponse(question.Id, question.Label, answer.Value.Trim()));
        }

        return matched;
    }

    /// <summary>
    /// Drops a delivery date that has already passed.
    ///
    /// Relative dates are the one thing the model is measurably bad at here. Asked to resolve
    /// "Thursday" it has returned both a Wednesday and, once the weekday was spelled out for it, the
    /// Thursday that had already gone. Prompt wording moved the error around rather than removing
    /// it, so the direction is decided here instead.
    ///
    /// Dropping it is the right failure. A customer asking for something is asking for a day still
    /// to come, and an empty date box costs the seller one tap - while a date that is quietly four
    /// days early reaches the customer as a missed deadline. The manual form still accepts any date
    /// the seller types, including past ones, because a recorded-after-the-fact order is real.
    /// </summary>
    private static DateOnly? FutureOnly(DateOnly? date, DateOnly today) =>
        date is { } value && value >= today ? value : null;

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>A catalogue row as the mapper needs it: an id the reader never saw, and a name it did.</summary>
public sealed record DraftProduct(Guid Id, string Name, decimal? Price);
