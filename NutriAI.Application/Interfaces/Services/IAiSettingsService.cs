namespace NutriAI.Application.Interfaces.Services;

public interface IAiSettingsService
{
    Task<AiSettingsFormData> GetFormDataAsync(CancellationToken cancellationToken = default);

    Task<AiSettingsSaveResult> SaveAsync(string? apiKey, string selectedModel, CancellationToken cancellationToken = default);

    Task<OpenAiConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default);
}

public sealed record AiSettingsFormData(
    string SelectedModel,
    string? ApiKeyLastFour,
    string SettingsFilePath,
    string? EnvFilePath,
    IReadOnlyList<string> AvailableModels);

public sealed record AiSettingsSaveResult(
    bool Succeeded,
    string Message,
    string? SettingsFilePath = null,
    string? EnvFilePath = null);
