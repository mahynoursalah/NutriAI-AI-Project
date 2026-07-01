using NutriAI.Application.Configuration;

namespace NutriAI.Application.Interfaces.Services;

public interface IOpenAiSettingsStore
{
    string SettingsFilePath { get; }

    string? EnvFilePath { get; }

    OpenAiSettings GetCurrent();

    Task SaveAsync(string apiKey, string model, CancellationToken cancellationToken = default);
}
