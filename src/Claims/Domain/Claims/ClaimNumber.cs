using System.Text.RegularExpressions;
using Chubb.Contracts;

namespace Chubb.Claims.Domain;

public sealed partial record ClaimNumber
{
    private const int MinYear = 2000;
    private const int MaxYear = 9999;
    private const int MaxSequence = 999_999;

    private ClaimNumber(string market, int year, int sequence)
    {
        Market = market;
        Year = year;
        Sequence = sequence;
    }

    public string Market { get; }

    public int Year { get; }

    public int Sequence { get; }

    public static ClaimNumber Create(string market, int year, int sequence)
    {
        if (!Markets.All.Contains(market))
        {
            throw new ArgumentException($"Unknown market '{market}'.", nameof(market));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(year, MinYear);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(year, MaxYear);
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sequence, MaxSequence);
        return new ClaimNumber(market, year, sequence);
    }

    public static ClaimNumber Parse(string value)
    {
        var match = NumberPattern().Match(value);
        if (!match.Success)
        {
            throw new FormatException("Claim number must look like CLM-SG-2026-000123.");
        }

        return Create(match.Groups["market"].Value, int.Parse(match.Groups["year"].Value), int.Parse(match.Groups["sequence"].Value));
    }

    public override string ToString() => $"CLM-{Market}-{Year}-{Sequence:D6}";

    [GeneratedRegex(@"^CLM-(?<market>[A-Z]{2})-(?<year>\d{4})-(?<sequence>\d{6})$")]
    private static partial Regex NumberPattern();
}
