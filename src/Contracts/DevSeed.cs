namespace Chubb.Contracts;

public static class DevSeed
{
    public static class SeedUserIds
    {
        public static readonly Guid Claimant = new("00000000-0000-0000-0000-0000000000a1");
        public static readonly Guid Officer = new("00000000-0000-0000-0000-0000000000b1");
        public static readonly Guid Manager = new("00000000-0000-0000-0000-0000000000c1");
        public static readonly Guid OfficerManager = new("00000000-0000-0000-0000-0000000000d1");
        public static readonly Guid SecondClaimant = new("00000000-0000-0000-0000-0000000000a2");
        public static readonly Guid SecondOfficer = new("00000000-0000-0000-0000-0000000000b2");
    }
}
