using Microsoft.Extensions.Logging;
using NutriAI.Application.Configuration;
using NutriAI.Application.Interfaces.Services;

namespace NutriAI.Infrastructure.Services;

public class AiSettingsService : IAiSettingsService
{
    private readonly IOpenAiSettingsStore _settingsStore;
    private readonly IAiNutritionService _aiNutritionService;
    private readonly ILogger<AiSettingsService> _logger;

    public AiSettingsService(
        IOpenAiSettingsStore settingsStore,
        IAiNutritionService aiNutritionService,
        ILogger<AiSettingsService> logger)
    {
        _settingsStore = settingsStore;
        _aiNutritionService = aiNutritionService;
        _logger = logger;
    }

    public Task<AiSettingsFormData> GetFormDataAsync(CancellationToken cancellationToken = default)
    {
        var current = _settingsStore.GetCurrent();
        var lastFour = GetLastFour(current.ApiKey);

        return Task.FromResult(new AiSettingsFormData(
            current.Model,
            lastFour,
            _settingsStore.SettingsFilePath,
            _settingsStore.EnvFilePath,
            OpenAiModelOptions.SupportedModels));
    }

    public async Task<AiSettingsSaveResult> SaveAsync(string? apiKey, string selectedModel, CancellationToken cancellationToken = default)
    {
        var current = _settingsStore.GetCurrent();
        var resolvedKey = string.IsNullOrWhiteSpace(apiKey) ? current.ApiKey : apiKey.Trim();

        if (string.IsNullOrWhiteSpace(resolvedKey))
            return new AiSettingsSaveResult(false, "OpenAI API Key is required.");

        if (!OpenAiModelOptions.IsSupported(selectedModel))
            return new AiSettingsSaveResult(false, "Please select a supported model.");

        try
        {
            await _settingsStore.SaveAsync(resolvedKey, selectedModel.Trim(), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save OpenAI settings to {Path}", _settingsStore.SettingsFilePath);
            return new AiSettingsSaveResult(false, "Could not save AI settings. Please try again.");
        }

        _logger.LogInformation(
            "OpenAI settings updated. Model: {Model}, ApiKey configured: {IsConfigured}, Path: {Path}",
            selectedModel,
            !string.IsNullOrWhiteSpace(resolvedKey),
            _settingsStore.SettingsFilePath);

        var test = await _aiNutritionService.TestConnectionAsync(cancellationToken);
        if (!test.Succeeded)
        {
            return new AiSettingsSaveResult(
                false,
                $"Settings were saved, but the model test failed: {test.Message}",
                _settingsStore.SettingsFilePath,
                _settingsStore.EnvFilePath);
        }

        return new AiSettingsSaveResult(
            true,
            $"AI settings saved and verified with model '{test.Model}'.",
            _settingsStore.SettingsFilePath,
            _settingsStore.EnvFilePath);
    }

    public Task<OpenAiConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default) =>
        _aiNutritionService.TestConnectionAsync(cancellationToken);

    private static string? GetLastFour(string? apiKey) =>
        string.IsNullOrWhiteSpace(apiKey) || apiKey.Length < 4
            ? null
            : apiKey[^4..];
}
