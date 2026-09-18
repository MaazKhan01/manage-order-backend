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
        //
        // Only the leading 48-bit millisecond timestamp is compared. The rest is random, so two ids
        // created inside the same millisecond have no defined order between them — asserting on the
        // whole value would make this test fail at random.
        var first = new TestEntity();
        var second = new TestEntity();

        first.Id.ShouldNotBe(second.Id);
        TimestampOf(second.Id).ShouldBeGreaterThanOrEqualTo(TimestampOf(first.Id));
    }

    /// <summary>The 48-bit big-endian millisecond timestamp that leads a UUID v7.</summary>
    private static long TimestampOf(Guid id)
    {
        var bytes = id.ToByteArray(bigEndian: true);

        long timestamp = 0;
        for (var i = 0; i < 6; i++)
        {
            timestamp = (timestamp << 8) | bytes[i];
        }

        return timestamp;
    }
}
