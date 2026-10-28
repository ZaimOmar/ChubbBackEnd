using System.Security.Cryptography;
using Chubb.Claims.Domain;

namespace Chubb.Claims.Infrastructure.Identifiers;

/// <summary>
/// RFC 9562 UUIDv7 with a 12-bit monotonic counter (method 1), so ids sort in generation order
/// in PostgreSQL even when several are made in one millisecond.
/// </summary>
public sealed class UuidV7IdGenerator(TimeProvider clock) : IIdGenerator
{
    private const int CounterLimit = 1 << 12;
    private const int UuidLength = 16;
    private const int RandomOffset = 8;

    private readonly object _gate = new();
    private long _lastMilliseconds;
    private int _counter;

    public Guid NewId()
    {
        Span<byte> bytes = stackalloc byte[UuidLength];
        var (milliseconds, counter) = NextTick();
        WriteTimestamp(bytes, milliseconds);
        bytes[6] = (byte)(0x70 | (counter >> 8));
        bytes[7] = (byte)counter;
        RandomNumberGenerator.Fill(bytes[RandomOffset..]);
        bytes[RandomOffset] = (byte)(0x80 | (bytes[RandomOffset] & 0x3F));
        return new Guid(bytes, bigEndian: true);
    }

    private (long Milliseconds, int Counter) NextTick()
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
            if (now > _lastMilliseconds)
            {
                _lastMilliseconds = now;
                _counter = 0;
            }
            else if (++_counter == CounterLimit)
            {
                // Counter exhausted inside one millisecond (or the clock stepped back): borrow the next millisecond.
                _lastMilliseconds++;
                _counter = 0;
            }

            return (_lastMilliseconds, _counter);
        }
    }

    private static void WriteTimestamp(Span<byte> bytes, long milliseconds)
    {
        for (var index = 5; index >= 0; index--)
        {
            bytes[index] = (byte)milliseconds;
            milliseconds >>= 8;
        }
    }
}
