using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NutriAI.Application.Configuration;
using NutriAI.Application.Interfaces.Services;

namespace NutriAI.Infrastructure.Configuration;

public class JsonOpenAiSettingsStore : IOpenAiSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly OpenAiSettings _baseline;
    private readonly ILogger<JsonOpenAiSettingsStore> _logger;
    private readonly object _lock = new();

    public JsonOpenAiSettingsStore(
        IOptions<OpenAiSettings> baseline,
        IHostEnvironment environment,
        ILogger<JsonOpenAiSettingsStore> logger)
    {
        _baseline = baseline.Value;
        _logger = logger;

        var dataDir = Path.Combine(environment.ContentRootPath, "Data");
        Directory.CreateDirectory(dataDir);
        SettingsFilePath = Path.Combine(dataDir, "openai-settings.json");
        EnvFilePath = OpenAiEnvFileHelper.ResolveEnvFilePath(environment.ContentRootPath);
    }

    public string SettingsFilePath { get; }

    public string? EnvFilePath { get; }

    public OpenAiSettings GetCurrent()
    {
        lock (_lock)
        {
            var overrides = LoadFromDisk();
            return new OpenAiSettings
            {
                ApiKey = FirstNonEmpty(overrides?.ApiKey, _baseline.ApiKey) ?? string.Empty,
                Model = FirstNonEmpty(overrides?.Model, _baseline.Model) ?? OpenAiModelOptions.DefaultModel,
                BaseUrl = _baseline.BaseUrl
            };
        }
    }

    public Task SaveAsync(string apiKey, string model, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var overrides = new OpenAiRuntimeOverrides
            {
                ApiKey = apiKey.Trim(),
                Model = model.Trim()
            };
            WriteJsonToDisk(overrides);

            if (!string.IsNullOrWhiteSpace(EnvFilePath))
            {
                OpenAiEnvFileHelper.UpsertOpenAiVariables(EnvFilePath, overrides.ApiKey, overrides.Model);
            }
        }

        if (!File.Exists(SettingsFilePath))
            throw new IOException($"Settings file was not created at {SettingsFilePath}");

        _logger.LogInformation(
            "OpenAI settings saved. Json: {JsonPath}, Env: {EnvPath}",
            SettingsFilePath,
            EnvFilePath);

        return Task.CompletedTask;
    }

    private OpenAiRuntimeOverrides? LoadFromDisk()
    {
        if (!File.Exists(SettingsFilePath))
            return null;

        try
        {
            var json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<OpenAiRuntimeOverrides>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read OpenAI settings from {Path}; using configuration defaults", SettingsFilePath);
            return null;
        }
    }

    private void WriteJsonToDisk(OpenAiRuntimeOverrides overrides)
    {
        var json = JsonSerializer.Serialize(overrides, JsonOptions);
        var tempPath = SettingsFilePath + ".tmp";

        try
        {
            File.WriteAllText(tempPath, json);
            if (File.Exists(SettingsFilePath))
                File.Replace(tempPath, SettingsFilePath, null);
            else
                File.Move(tempPath, SettingsFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist OpenAI settings to {Path}", SettingsFilePath);
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }
    }

    private static string? FirstNonEmpty(string? primary, string? fallback) =>
        !string.IsNullOrWhiteSpace(primary) ? primary : fallback;

    private sealed class OpenAiRuntimeOverrides
    {
        public string? ApiKey { get; set; }
        public string? Model { get; set; }
    }
}
