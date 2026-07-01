namespace NutriAI.Application.Configuration;

public static class OpenAiModelOptions
{
    public static readonly IReadOnlyList<string> SupportedModels =
    [
        "gpt-4o-mini",
        "gpt-4.1-mini",
        "gpt-4.1",
        "o4-mini"
    ];

    public const string DefaultModel = "gpt-4o-mini";

    public static bool IsSupported(string? model) =>
        !string.IsNullOrWhiteSpace(model) && SupportedModels.Contains(model.Trim());

    public static bool IsReasoningModel(string model) =>
        model.StartsWith("o", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Maps UI model id to API model id when the alias is rejected.
    /// </summary>
    public static string GetApiModelId(string model) =>
        model.Trim() switch
        {
            "o4-mini" => "o4-mini",
            _ => model.Trim()
        };

    public static IReadOnlyList<string> GetModelFallbacks(string model)
    {
        var primary = GetApiModelId(model);
        if (primary == "o4-mini")
            return ["o4-mini", "o4-mini-2025-04-16"];

        return [primary];
    }
}
