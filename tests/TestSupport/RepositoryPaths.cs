namespace Chubb.TestSupport;

public static class RepositoryPaths
{
    public static string ContractsDirectory { get; } = Path.Combine(FindRepositoryRoot(), "contracts", "openapi");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root (global.json) not found.");
    }
}
