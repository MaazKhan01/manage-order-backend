namespace DmOrder.Application.Common.Exceptions;

/// <summary>
/// One or more fields on an incoming request failed validation. Carries a field -> messages map so the
/// API can return an RFC 7807 problem with per-field errors that the frontend can attach to inputs.
/// </summary>
public sealed class RequestValidationException(IDictionary<string, string[]> errors)
    : Exception("One or more validation errors occurred.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;
}
