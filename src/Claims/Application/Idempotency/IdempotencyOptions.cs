using System.ComponentModel.DataAnnotations;

namespace Chubb.Claims.Application;

public sealed class IdempotencyOptions
{
    public const string SectionName = "Idempotency";

    [Required]
    public TimeSpan Retention { get; init; } = TimeSpan.FromHours(24);
}
