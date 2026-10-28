using Chubb.Claims.Application;

namespace Chubb.Claims.Api;

public static class ClaimListSortParser
{
    public static bool TryParse(string? text, out ClaimListSort sort)
    {
        sort = ClaimListSort.Newest;
        return text switch
        {
            "newest" => true,
            "oldest" => (sort = ClaimListSort.Oldest) == ClaimListSort.Oldest,
            _ => false,
        };
    }
}
