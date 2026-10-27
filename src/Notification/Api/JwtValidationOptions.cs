using System.ComponentModel.DataAnnotations;

namespace Chubb.Notification.Api;

public sealed class JwtValidationOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    [Required]
    public string PublicKeyPath { get; init; } = string.Empty;
}
