using DmOrder.Domain.Common;

namespace DmOrder.Domain.Tests.Common;

public class EntityTests
{
    private sealed class TestEntity : Entity;

    [Fact]
    public void NewEntity_IsGivenAVersion7Id()
    {
        var entity = new TestEntity();

        entity.Id.ShouldNotBe(Guid.Empty);
        entity.Id.Version.ShouldBe(7);
    }

    [Fact]
    public void Ids_AreOrderedByCreationTime()
    {
        // UUID v7 is time-ordered, which is what keeps the primary key index dense. If this ever
        // fails, the key generation strategy changed and the index behaviour changed with it.
        var first = new TestEntity();
        var second = new TestEntity();

        first.Id.ShouldNotBe(second.Id);
        Convert.ToHexString(first.Id.ToByteArray(bigEndian: true))
            .CompareTo(Convert.ToHexString(second.Id.ToByteArray(bigEndian: true)))
            .ShouldBeLessThanOrEqualTo(0);
    }
}
