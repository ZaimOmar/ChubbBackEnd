using System.ComponentModel.DataAnnotations;

namespace Chubb.Identity.Infrastructure;

public sealed class JwtIssuerOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    [Range(1, 1440)]
    public int LifetimeMinutes { get; init; } = 30;

    [Required]
    public string PrivateKeyPath { get; init; } = string.Empty;
}
