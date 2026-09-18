namespace DmOrder.Api.Configuration;

/// <summary>
/// Loads a local <c>.env</c> file into environment variables so backend and frontend share one
/// convention for local configuration. Deliberately dependency-free and development-only: on a real
/// host the environment is supplied by the platform, and a stray .env must never override it.
///
/// Existing environment variables always win, so exporting a value in the shell overrides the file.
/// </summary>
public static class DotEnvLoader
{
    public static void Load(string startDirectory)
    {
        // Walk up so the file can sit at the backend repo root regardless of whether the app was
        // started from the project folder, the solution folder, or a test host.
        var path = FindEnvFile(startDirectory);
        if (path is null)
        {
            return;
        }

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim().Trim('"');

            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private static string? FindEnvFile(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);

        for (var depth = 0; depth < 6 && directory is not null; depth++)
        {
            var candidate = Path.Combine(directory.FullName, ".env");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
