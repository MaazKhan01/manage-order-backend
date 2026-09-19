using DmOrder.Domain.Common;
using DmOrder.Domain.Exceptions;

namespace DmOrder.Domain.Orders;

/// <summary>
/// Someone who has ordered from this store.
///
/// Created from an order — customers never register, which is the whole point of the V1 flow.
/// Identified by phone within a store, because that is what these sellers actually use.
///
/// Deliberately **per store**: the same person ordering from two sellers is two records. One seller
/// must never learn anything about another's customers, and a shared customer table would make that
/// a query away.
/// </summary>
public sealed class Customer : Entity, ITenantOwned
{
    private Customer() { }

    public Guid StoreId { get; private set; }

    public string Name { get; private set; } = null!;

    public string Phone { get; private set; } = null!;

    public string? Email { get; private set; }

    public string? AddressText { get; private set; }

    public static Customer Create(Guid storeId, string name, string phone, string? email, string? address)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BusinessRuleException("A name is required.");
        }

        if (string.IsNullOrWhiteSpace(phone))
        {
            throw new BusinessRuleException("A phone number is required.");
        }

        return new Customer
        {
            StoreId = storeId,
            Name = name.Trim(),
            Phone = NormalisePhone(phone),
            Email = Normalise(email),
            AddressText = Normalise(address),
        };
    }

    /// <summary>
    /// Refreshes details from a newer order.
    ///
    /// Blanks never overwrite what is already known: a customer who skipped the email field on their
    /// second order has not withdrawn the address they gave on their first.
    /// </summary>
    public void UpdateFromOrder(string name, string? email, string? address)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            Name = name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            Email = email.Trim();
        }

        if (!string.IsNullOrWhiteSpace(address))
        {
            AddressText = address.Trim();
        }
    }

    /// <summary>
    /// Strips formatting so "0300 123 4567" and "03001234567" are the same customer.
    ///
    /// Leading + is kept, since it is the difference between a local and an international number.
    /// No country-specific parsing: sellers here deal in several formats and guessing wrong would
    /// merge two different people.
    /// </summary>
    public static string NormalisePhone(string phone)
    {
        var trimmed = phone.Trim();
        var hasPlus = trimmed.StartsWith('+');
        var digits = new string([.. trimmed.Where(char.IsAsciiDigit)]);

        return hasPlus ? $"+{digits}" : digits;
    }

    private static string? Normalise(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
