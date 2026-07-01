using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NutriAI.Application.Configuration;
using NutriAI.Application.DTOs;
using NutriAI.Application.Interfaces.Services;

namespace NutriAI.Infrastructure.AI;

public class OpenAiNutritionService : IAiNutritionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _httpClient;
    private readonly IOpenAiSettingsStore _settingsStore;
    private readonly ILogger<OpenAiNutritionService> _logger;

    public OpenAiNutritionService(
        HttpClient httpClient,
        IOpenAiSettingsStore settingsStore,
        ILogger<OpenAiNutritionService> logger)
    {
        _httpClient = httpClient;
        _settingsStore = settingsStore;
        _logger = logger;
    }

    private OpenAiSettings Settings => _settingsStore.GetCurrent();

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Settings.ApiKey);

    public async Task<OpenAiConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return new OpenAiConnectionTestResult(false, "OpenAI API key is not configured.", Settings.Model);

        var result = await SendChatAsync(
            "You are a connectivity check. Reply with exactly the word OK and nothing else.",
            "ping",
            jsonMode: false,
            cancellationToken);

        return result.Content != null
            ? new OpenAiConnectionTestResult(true, $"Model '{result.ModelUsed}' is working.", result.ModelUsed)
            : new OpenAiConnectionTestResult(false, result.Error ?? "OpenAI request failed.", Settings.Model);
    }

    public Task<MealAnalysisResult?> AnalyzeMealAsync(string description, UserNutritionContext context, CancellationToken cancellationToken = default) =>
        ExecuteWithRetryAsync(() => SendJsonAsync<MealAnalysisResult>(
            """
            You are a nutrition assistant. Estimate calories and macros for the meal described.
            Respond ONLY with valid JSON:
            {"calories":number,"protein":number,"carbs":number,"fat":number,"aiResponse":"string explaining impact on daily calorie goal and weight progress"}
            """,
            $"Meal: {description}\nDaily calorie target: {context.DailyCalorieTarget}. Current weight: {context.CurrentWeightKg}kg, goal: {context.GoalWeightKg}kg.",
            cancellationToken), cancellationToken);

    public async Task<IReadOnlyList<MealPlanDayResult>?> GenerateMealPlanAsync(
        double goalWeightKg,
        int timelineWeeks,
        string dietaryPreference,
        UserNutritionContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteWithRetryAsync(() => SendJsonAsync<MealPlanResponse>(
            """
            You are a meal planning nutritionist. Create a 7-day plan (Monday-Sunday) with breakfast, lunch, dinner, snacks.
            Each meal needs low-calorie preparation instructions.
            Respond ONLY with valid JSON:
            {"days":[{"day":"Monday","meals":[{"mealType":"Breakfast","name":"string","calories":number,"protein":number,"carbs":number,"fat":number,"instructions":"string"}]}]}
            """,
            $"Goal weight: {goalWeightKg}kg in {timelineWeeks} weeks. Preference: {dietaryPreference}. Daily calories ~{context.DailyCalorieTarget}. Activity: {context.ActivityLevel}.",
            cancellationToken), cancellationToken);

        return result?.Days;
    }

    public Task<RecipeAnalysisResult?> AnalyzeRecipeAsync(string recipeText, CancellationToken cancellationToken = default) =>
        ExecuteWithRetryAsync(() => SendJsonAsync<RecipeAnalysisResult>(
            """
            Parse the recipe and estimate nutrition per ingredient and totals.
            Respond ONLY with valid JSON:
            {"recipeName":"string","totalCalories":number,"servings":number,"protein":number,"carbs":number,"fat":number,
            "ingredients":[{"name":"string","amount":"string","calories":number}],
            "alternatives":["string"]}
            """,
            recipeText,
            cancellationToken), cancellationToken);

    public async Task<IReadOnlyList<string>?> GetWeeklyRecommendationsAsync(string reportSummary, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteWithRetryAsync(() => SendJsonAsync<TipsResponse>(
            "Provide exactly 3 actionable weekly nutrition tips. Respond ONLY with JSON: {\"tips\":[\"tip1\",\"tip2\",\"tip3\"]}",
            reportSummary,
            cancellationToken), cancellationToken);

        return result?.Tips;
    }

    public Task<string?> GetHydrationRecommendationAsync(
        UserNutritionContext context,
        int currentMl,
        int todayCalories,
        CancellationToken cancellationToken = default) =>
        ExecuteWithRetryAsync(() => SendTextAsync(
            "Give one concise hydration recommendation (max 2 sentences). No markdown.",
            $"Water today: {currentMl}ml of {context.DailyWaterTargetMl}ml goal. Calories today: {todayCalories}/{context.DailyCalorieTarget}. Activity: {context.ActivityLevel}. Weight goal: {context.GoalWeightKg}kg.",
            cancellationToken), cancellationToken);

    public Task<string?> GetDashboardInsightAsync(
        UserNutritionContext context,
        int caloriesConsumed,
        int waterMl,
        CancellationToken cancellationToken = default) =>
        ExecuteWithRetryAsync(() => SendTextAsync(
            "Give one encouraging dashboard nutrition insight (max 2 sentences). No markdown.",
            $"Calories: {caloriesConsumed}/{context.DailyCalorieTarget}. Water: {waterMl}/{context.DailyWaterTargetMl}ml. Weight: {context.CurrentWeightKg}kg -> {context.GoalWeightKg}kg.",
            cancellationToken), cancellationToken);

    public Task<string?> GetWeightInsightAsync(
        UserNutritionContext context,
        double latestWeight,
        CancellationToken cancellationToken = default) =>
        ExecuteWithRetryAsync(() => SendTextAsync(
            "Explain briefly how consistent logging supports reaching the weight goal (max 2 sentences). No markdown.",
            $"Latest weight: {latestWeight}kg. Goal: {context.GoalWeightKg}kg. Calorie target: {context.DailyCalorieTarget}.",
            cancellationToken), cancellationToken);

    private async Task<T?> ExecuteWithRetryAsync<T>(Func<Task<T?>> operation, CancellationToken cancellationToken) where T : class
    {
        if (!IsConfigured) return null;

        var first = await operation();
        if (first != null) return first;

        _logger.LogInformation("OpenAI call failed for model {Model}; retrying in 5 seconds...", Settings.Model);
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

        return await operation();
    }

    private async Task<T?> SendJsonAsync<T>(string systemPrompt, string userPrompt, CancellationToken cancellationToken) where T : class
    {
        var result = await SendChatAsync(systemPrompt, userPrompt, jsonMode: true, cancellationToken);
        if (string.IsNullOrWhiteSpace(result.Content)) return null;

        var normalized = NormalizeModelContent(result.Content);
        if (string.IsNullOrWhiteSpace(normalized)) return null;

        try
        {
            return JsonSerializer.Deserialize<T>(normalized, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse OpenAI JSON from model {Model}", result.ModelUsed);
            return null;
        }
    }

    private async Task<string?> SendTextAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var result = await SendChatAsync(systemPrompt, userPrompt, jsonMode: false, cancellationToken);
        return result.Content;
    }

    private async Task<ChatCallResult> SendChatAsync(
        string systemPrompt,
        string userPrompt,
        bool jsonMode,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            return ChatCallResult.Fail(Settings.Model, "API key is not configured.");

        var modelCandidates = OpenAiModelOptions.GetModelFallbacks(Settings.Model);
        ChatCallResult? lastFailure = null;

        foreach (var modelId in modelCandidates)
        {
            var result = await SendChatAsyncForModel(modelId, systemPrompt, userPrompt, jsonMode, cancellationToken);
            if (result.Content != null)
                return result;

            lastFailure = result;
            _logger.LogWarning("OpenAI request failed for model {Model}: {Error}", modelId, result.Error);
        }

        return lastFailure ?? ChatCallResult.Fail(Settings.Model, "OpenAI request failed.");
    }

    private async Task<ChatCallResult> SendChatAsyncForModel(
        string modelId,
        string systemPrompt,
        string userPrompt,
        bool jsonMode,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = BuildRequestBody(modelId, systemPrompt, userPrompt, jsonMode);
            var requestUri = $"{Settings.BaseUrl.TrimEnd('/')}/chat/completions";
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Settings.ApiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = ParseOpenAiError(responseJson) ?? $"HTTP {(int)response.StatusCode}";
                _logger.LogWarning("OpenAI API error for model {Model} ({Status}): {Body}", modelId, response.StatusCode, responseJson);
                return ChatCallResult.Fail(modelId, error);
            }

            using var doc = JsonDocument.Parse(responseJson);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return ChatCallResult.Ok(modelId, NormalizeModelContent(content));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "OpenAI request failed for model {Model}", modelId);
            return ChatCallResult.Fail(modelId, ex.Message);
        }
    }

    private static Dictionary<string, object> BuildRequestBody(string modelId, string systemPrompt, string userPrompt, bool jsonMode)
    {
        var body = new Dictionary<string, object>
        {
            ["model"] = modelId,
            ["messages"] = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        if (OpenAiModelOptions.IsReasoningModel(modelId))
        {
            body["temperature"] = 1;
            body["max_completion_tokens"] = 4096;
            body["reasoning_effort"] = "low";
        }
        else
        {
            body["temperature"] = 0.4;
            body["max_completion_tokens"] = 2048;
        }

        if (jsonMode)
            body["response_format"] = new { type = "json_object" };

        return body;
    }

    private static string? NormalizeModelContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        content = content.Trim();
        if (!content.StartsWith("```", StringComparison.Ordinal))
            return content;

        var firstNewLine = content.IndexOf('\n');
        if (firstNewLine < 0)
            return content;

        content = content[(firstNewLine + 1)..].TrimEnd();
        if (content.EndsWith("```", StringComparison.Ordinal))
            content = content[..^3].TrimEnd();

        return content;
    }

    private static string? ParseOpenAiError(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
                return message.GetString();
        }
        catch
        {
            // ignored
        }

        return null;
    }

    private sealed class ChatCallResult
    {
        public string? Content { get; init; }
        public string? Error { get; init; }
        public string ModelUsed { get; init; } = string.Empty;

        public static ChatCallResult Ok(string model, string? content) =>
            new() { Content = content, ModelUsed = model };

        public static ChatCallResult Fail(string model, string? error) =>
            new() { Error = error, ModelUsed = model };
    }

    private sealed class MealPlanResponse
    {
        public List<MealPlanDayResult>? Days { get; set; }
    }

    private sealed class TipsResponse
    {
        public List<string>? Tips { get; set; }
    }
}
