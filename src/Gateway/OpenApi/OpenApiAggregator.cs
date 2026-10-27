using Microsoft.OpenApi;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

namespace Chubb.Gateway.OpenApi;

public static class OpenApiAggregator
{
    public const string BearerSchemeName = "bearer";

    private static readonly string[] ExcludedPathPrefixes = ["/internal", "/health"];
    private static readonly string[] AnonymousPaths = ["/auth/token"];

    public static string BuildJson(string contractsPath) =>
        Build(contractsPath).SerializeAsJson(OpenApiSpecVersion.OpenApi3_0);

    public static OpenApiDocument Build(string contractsPath)
    {
        if (!Directory.Exists(contractsPath))
        {
            throw new OpenApiMergeException($"OpenApi:ContractsPath '{contractsPath}' does not exist.");
        }

        var merged = CreateEmptyDocument();
        foreach (var file in Directory.EnumerateFiles(contractsPath, "*.yaml").Order(StringComparer.Ordinal))
        {
            MergeInto(merged, ReadContract(file), Path.GetFileName(file));
        }

        return merged;
    }

    private static OpenApiDocument CreateEmptyDocument() => new OpenApiDocument
    {
        Info = new OpenApiInfo { Title = "Chubb Claims API", Version = "v1" },
        Paths = new OpenApiPaths(),
        Components = new OpenApiComponents
        {
            Schemas = new Dictionary<string, OpenApiSchema>(),
            SecuritySchemes = new Dictionary<string, OpenApiSecurityScheme> { [BearerSchemeName] = CreateBearerScheme() },
        },
    };

    private static OpenApiSecurityScheme CreateBearerScheme() => new()
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = BearerSchemeName },
    };

    private static OpenApiDocument ReadContract(string file)
    {
        var document = new OpenApiStringReader().Read(File.ReadAllText(file), out var diagnostic);
        return diagnostic.Errors.Count == 0
            ? document
            : throw new OpenApiMergeException($"{Path.GetFileName(file)} is not valid OpenAPI: {diagnostic.Errors[0].Message}");
    }

    private static void MergeInto(OpenApiDocument merged, OpenApiDocument contract, string source)
    {
        RejectUnsupportedComponents(contract, source);
        foreach (var (path, item) in contract.Paths.Where(pair => !IsExcluded(pair.Key)))
        {
            if (merged.Paths.ContainsKey(path))
            {
                throw new OpenApiMergeException($"Path '{path}' from {source} is already defined by another contract.");
            }

            // Per operation rather than a global requirement: the serializer drops an empty
            // override, so an anonymous operation could not otherwise opt out.
            if (!AnonymousPaths.Contains(path))
            {
                RequireBearer(item, merged.Components.SecuritySchemes[BearerSchemeName]);
            }

            merged.Paths.Add(path, item);
        }

        foreach (var (name, schema) in contract.Components?.Schemas ?? new Dictionary<string, OpenApiSchema>())
        {
            if (!merged.Components.Schemas.TryAdd(name, schema))
            {
                throw new OpenApiMergeException($"Schema '{name}' from {source} is already defined by another contract.");
            }
        }
    }

    // Only schemas are merged, so a contract using any other component kind would silently lose its references.
    private static void RejectUnsupportedComponents(OpenApiDocument contract, string source)
    {
        var components = contract.Components;
        if (components is null)
        {
            return;
        }

        var unsupported = new (string Kind, int Count)[]
        {
            ("responses", components.Responses.Count),
            ("parameters", components.Parameters.Count),
            ("examples", components.Examples.Count),
            ("requestBodies", components.RequestBodies.Count),
            ("headers", components.Headers.Count),
            ("securitySchemes", components.SecuritySchemes.Count),
            ("links", components.Links.Count),
            ("callbacks", components.Callbacks.Count),
        }.Where(component => component.Count > 0).Select(component => component.Kind).ToList();
        if (unsupported.Count > 0)
        {
            throw new OpenApiMergeException(
                $"{source} defines unsupported component kinds ({string.Join(", ", unsupported)}); only schemas are merged.");
        }
    }

    private static void RequireBearer(OpenApiPathItem item, OpenApiSecurityScheme bearer)
    {
        foreach (var operation in item.Operations.Values)
        {
            operation.Security = [new OpenApiSecurityRequirement { [bearer] = [] }];
        }
    }

    private static bool IsExcluded(string path) =>
        ExcludedPathPrefixes.Any(prefix =>
            path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));
}
