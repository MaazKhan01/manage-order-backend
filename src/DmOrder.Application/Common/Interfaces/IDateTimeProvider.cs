namespace DmOrder.Application.Common.Interfaces;

/// <summary>Clock abstraction so that time-dependent rules (delivery dates, token expiry) are testable.</summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
