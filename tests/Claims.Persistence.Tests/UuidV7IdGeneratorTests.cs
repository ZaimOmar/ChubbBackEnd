using Chubb.Claims.Infrastructure.Identifiers;

namespace Chubb.Claims.Persistence.Tests;

public sealed class UuidV7IdGeneratorTests
{
    private const int Calls = 10_000;

    [Fact]
    public void NewId_TenThousandCalls_StrictlyIncreasingInPostgresOrder()
    {
        var generator = new UuidV7IdGenerator(TimeProvider.System);

        var bytes = Enumerable.Range(0, Calls).Select(_ => generator.NewId().ToByteArray(bigEndian: true)).ToList();

        // PostgreSQL compares uuid values byte by byte in RFC 4122 (big-endian) order.
        bytes.Zip(bytes.Skip(1)).All(pair => ((ReadOnlySpan<byte>)pair.First).SequenceCompareTo(pair.Second) < 0).Should().BeTrue();
    }

    [Fact]
    public void NewId_Generated_HasVersion7AndRfcVariant()
    {
        var bytes = new UuidV7IdGenerator(TimeProvider.System).NewId().ToByteArray(bigEndian: true);

        (bytes[6] >> 4).Should().Be(7);
        (bytes[8] >> 6).Should().Be(0b10);
    }

    [Fact]
    public void NewId_ClockStepsBack_StaysIncreasing()
    {
        var clock = new SteppingClock();
        var generator = new UuidV7IdGenerator(clock);
        var first = generator.NewId().ToByteArray(bigEndian: true);
        clock.Now = clock.Now.AddSeconds(-5);

        var second = generator.NewId().ToByteArray(bigEndian: true);

        first.AsSpan().SequenceCompareTo(second).Should().BeNegative();
    }

    private sealed class SteppingClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
