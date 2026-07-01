namespace NutriAI.Infrastructure.Configuration;

internal static class OpenAiEnvFileHelper
{
    public static string? FindSolutionDirectory(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NutriAI.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        return null;
    }

    public static string ResolveEnvFilePath(string contentRootPath)
    {
        var solutionDir = FindSolutionDirectory(contentRootPath);
        var baseDir = solutionDir ?? contentRootPath;
        return Path.Combine(baseDir, ".env");
    }

    public static void UpsertOpenAiVariables(string envFilePath, string apiKey, string model)
    {
        var lines = File.Exists(envFilePath)
            ? File.ReadAllLines(envFilePath).ToList()
            : new List<string>();

        if (lines.Count == 0)
        {
            lines.Add("# NutriAI environment variables (not committed to Git)");
            lines.Add("# Copy from .env.example for a full template.");
            lines.Add(string.Empty);
        }

        UpsertLine(lines, "OpenAI__ApiKey", apiKey);
        UpsertLine(lines, "OpenAI__Model", model);

        var tempPath = envFilePath + ".tmp";
        File.WriteAllLines(tempPath, lines);
        if (File.Exists(envFilePath))
            File.Replace(tempPath, envFilePath, null);
        else
            File.Move(tempPath, envFilePath);
    }

    private static void UpsertLine(List<string> lines, string key, string value)
    {
        var prefix = key + "=";
        var index = lines.FindIndex(line =>
            line.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        var entry = prefix + value;
        if (index >= 0)
            lines[index] = entry;
        else
            lines.Add(entry);
    }
}
