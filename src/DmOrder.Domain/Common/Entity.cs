namespace DmOrder.Domain.Common;

/// <summary>
/// Base type for every persisted aggregate/entity.
/// Ids are UUID v7: time-ordered (good B-tree locality) but not guessable, so they are safe to put in URLs.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
