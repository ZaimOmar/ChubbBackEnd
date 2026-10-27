using System.ComponentModel.DataAnnotations;

namespace Chubb.Identity.Infrastructure;

public sealed class SeedOptions
{
    public const string SectionName = "Identity";

    [Required]
    [MinLength(8)]
    public string SeedPassword { get; init; } = string.Empty;
}
