namespace DmOrder.Domain.Exceptions;

/// <summary>
/// Base class for violations of a business rule. These map to 4xx responses, never 500.
/// </summary>
public abstract class DomainException(string message) : Exception(message);

/// <summary>A business rule was broken (e.g. an illegal order status transition). Maps to 409/422.</summary>
public sealed class BusinessRuleException(string message) : DomainException(message);

/// <summary>
/// The requested resource does not exist, or the caller is not allowed to know that it exists.
/// Cross-tenant access deliberately raises this rather than a forbidden error, so that a seller
/// cannot probe for the existence of another seller's records. Maps to 404.
/// </summary>
public sealed class NotFoundException(string resource, object key)
    : DomainException($"{resource} '{key}' was not found.")
{
    public string Resource { get; } = resource;
}

/// <summary>A uniqueness or state conflict (e.g. slug already taken). Maps to 409.</summary>
public sealed class ConflictException(string message) : DomainException(message);

/// <summary>The caller is authenticated but not permitted to perform this action. Maps to 403.</summary>
public sealed class ForbiddenException(string message) : DomainException(message);
