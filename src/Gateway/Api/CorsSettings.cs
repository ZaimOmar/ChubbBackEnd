using System.ComponentModel.DataAnnotations;

namespace Chubb.Gateway.Api;

public sealed class CorsSettings
{
    public const string SectionName = "Cors";

    [Required]
    [MinLength(1)]
    public string[] AllowedOrigins { get; init; } = [];
}
