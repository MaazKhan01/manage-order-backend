namespace DmOrder.Domain.CustomFields;

/// <summary>
/// The kinds of question a seller can ask.
///
/// Deliberately a small, closed set. Every trade's needs are covered by combining these — a baker's
/// "Eggless" is a Boolean, a tailor's "Bust (inches)" is a Number, a florist's "Flower type" is a
/// Select. Adding a type should be rare and deliberate, not the reflex when a new trade signs up.
/// </summary>
public enum CustomFieldType
{
    /// <summary>One line. Names, colours, sizes written freehand.</summary>
    Text = 0,

    /// <summary>Several lines. Special instructions, a message to write on a cake.</summary>
    LongText = 1,

    /// <summary>Measurements, quantities, weights. Bounded by MinValue/MaxValue.</summary>
    Number = 2,

    /// <summary>Pick one from the seller's list.</summary>
    Select = 3,

    /// <summary>Pick any number from the seller's list.</summary>
    MultiSelect = 4,

    /// <summary>Yes or no.</summary>
    Boolean = 5,

    /// <summary>Delivery or collection date.</summary>
    Date = 6,

    /// <summary>Delivery or collection time.</summary>
    Time = 7,

    /// <summary>
    /// A reference photo from the customer — the design they saw, the dress they want copied.
    /// Uploaded anonymously, so the upload path is treated as hostile input.
    /// </summary>
    Image = 8,
}
