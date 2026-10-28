namespace Chubb.Claims.Domain.Tests;

public sealed class MoneyAndPolicyTests
{
    [Fact]
    public void Money_Create_MoreThanTwoDecimals_Rejected()
    {
        var create = () => Money.Create(10.001m, "SGD");

        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("sgd")]
    [InlineData("SG")]
    [InlineData("")]
    public void Money_Create_NonIsoCurrency_Rejected(string currency)
    {
        var create = () => Money.Create(1m, currency);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Money_Create_Negative_Rejected()
    {
        var create = () => Money.Create(-1m, "SGD");

        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Money_Arithmetic_DifferentCurrencies_Throws()
    {
        var combine = () => Money.Create(5m, "SGD").MinusFlooredAtZero(Money.Create(1m, "HKD"));

        combine.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(8_000, 500, 10_000, 7_500)]
    [InlineData(20_000, 500, 10_000, 10_000)]
    [InlineData(400, 500, 10_000, 0)]
    [InlineData(500, 500, 10_000, 0)]
    [InlineData(8_000, 500, 0, 0)]
    [InlineData(50_000_000, 1_000_000, 40_000_000, 40_000_000)]
    public void NetPayableFor_AssessedExcessLimit_ReturnsFlooredAndCappedAtPolicyLimit(
        decimal assessed, decimal excess, decimal limit, decimal expected)
    {
        var policy = Scenario.PolicyFor(Guid.NewGuid(), Chubb.Contracts.Markets.Singapore, limit, excess);

        var payable = policy.NetPayableFor(Money.Create(assessed, "SGD"));

        payable.Amount.Should().Be(expected);
    }

    [Fact]
    public void ClaimNumber_Create_FormatsAsMarketYearSixDigitSequence()
    {
        ClaimNumber.Create("SG", 2026, 123).ToString().Should().Be("CLM-SG-2026-000123");
    }

    [Fact]
    public void ClaimNumber_Parse_RoundTripsValidNumber()
    {
        ClaimNumber.Parse("CLM-HK-2026-000007").Should().Be(ClaimNumber.Create("HK", 2026, 7));
    }

    [Theory]
    [InlineData("CLM-XX-2026-000001")]
    [InlineData("CLM-SG-2026-1")]
    [InlineData("SG-2026-000001")]
    public void ClaimNumber_Parse_InvalidValues_Rejected(string value)
    {
        var parse = () => ClaimNumber.Parse(value);

        parse.Should().Throw<Exception>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_000_000)]
    public void ClaimNumber_Create_SequenceOutOfRange_Rejected(int sequence)
    {
        var create = () => ClaimNumber.Create("SG", 2026, sequence);

        create.Should().Throw<ArgumentOutOfRangeException>();
    }
}
