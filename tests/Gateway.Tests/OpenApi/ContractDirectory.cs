namespace Chubb.Gateway.Tests.OpenApi;

public sealed class ContractDirectory : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("chubb-contracts-").FullName;

    public void Dispose() => Directory.Delete(Path, recursive: true);

    public void Copy(string sourceFile) =>
        File.Copy(sourceFile, System.IO.Path.Combine(Path, System.IO.Path.GetFileName(sourceFile)));

    public void Write(string fileName, string path, string schemaName, string[]? extraPaths = null, string? extraComponents = null)
    {
        var extras = string.Concat((extraPaths ?? []).Select(extra => $$"""
              {{extra}}:
                get:
                  responses:
                    '200':
                      description: OK
            """ + "\n"));
        var yaml = $$"""
            openapi: 3.0.1
            info:
              title: stub
              version: v1
            paths:
              {{path}}:
                get:
                  responses:
                    '200':
                      description: OK
                      content:
                        application/json:
                          schema:
                            $ref: '#/components/schemas/{{schemaName}}'
            {{extras}}components:
              schemas:
                {{schemaName}}:
                  type: object
                  properties:
                    id:
                      type: string
            {{extraComponents}}
            """;
        File.WriteAllText(System.IO.Path.Combine(Path, fileName), yaml);
    }
}
