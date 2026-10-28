namespace Chubb.Claims.Domain.Tests;

internal static class ResultAssertions
{
    public static void ShouldBeRefused(this Result result, string type, bool trailed) =>
        ShouldBeRefused(result.Refusal, type, trailed);

    public static void ShouldBeRefused<T>(this Result<T> result, string type, bool trailed) =>
        ShouldBeRefused(result.Refusal, type, trailed);

    public static void ShouldSucceed(this Result result) =>
        result.IsSuccess.Should().BeTrue(result.Refusal?.Type);

    public static void ShouldSucceed<T>(this Result<T> result) =>
        result.IsSuccess.Should().BeTrue(result.Refusal?.Type);

    private static void ShouldBeRefused(DomainRefusal? refusal, string type, bool trailed)
    {
        refusal.Should().NotBeNull();
        refusal?.Type.Should().Be(type);
        refusal?.IsTrailed.Should().Be(trailed);
    }
}
