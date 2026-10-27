using System.Collections.Frozen;

namespace Chubb.Contracts;

public static class Markets
{
    public const string Singapore = "SG";
    public const string HongKong = "HK";
    public const string Malaysia = "MY";
    public const string Thailand = "TH";
    public const string Indonesia = "ID";
    public const string Australia = "AU";

    public static readonly FrozenSet<string> All = new[]
    {
        Singapore, HongKong, Malaysia, Thailand, Indonesia, Australia,
    }.ToFrozenSet();
}
