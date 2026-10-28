namespace Chubb.Claims.Domain.Tests;

internal sealed class TestIdGenerator : IIdGenerator
{
    private int _next;

    public Guid NewId() => new(++_next, 0, 0, new byte[8]);
}
