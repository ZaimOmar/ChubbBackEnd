using System.ComponentModel.DataAnnotations;

namespace Chubb.Gateway.OpenApi;

public sealed class OpenApiOptions
{
    public const string SectionName = "OpenApi";

    [Required]
    public string ContractsPath { get; init; } = string.Empty;

    public string ResolveContractsPath(IHostEnvironment environment) =>
        Path.GetFullPath(ContractsPath, environment.ContentRootPath);
}
