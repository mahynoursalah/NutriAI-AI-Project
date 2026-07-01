using System.Text.Json;
using Microsoft.Extensions.Logging;
using NutriAI.Application.Common;
using NutriAI.Application.DTOs;
using NutriAI.Application.Interfaces.Repositories;
using NutriAI.Application.Interfaces.Services;
using NutriAI.Domain.Entities;
using NutriAI.Infrastructure.Data;

namespace NutriAI.Infrastructure.Services;

public class RecipeService : IRecipeService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ApplicationDbContext _context;
    private readonly IAiNutritionService _aiService;
    private readonly IFallbackNutritionRepository _fallbackRepository;
    private readonly ILogger<RecipeService> _logger;

    public RecipeService(
        ApplicationDbContext context,
        IAiNutritionService aiService,
        IFallbackNutritionRepository fallbackRepository,
        ILogger<RecipeService> logger)
    {
        _context = context;
        _aiService = aiService;
        _fallbackRepository = fallbackRepository;
        _logger = logger;
    }

    public async Task<object> AnalyzeRecipeAsync(string userId, string recipeText, CancellationToken cancellationToken = default)
    {
        if (!_aiService.IsConfigured)
        {
            return new { success = false, message = AiMessages.ApiKeyNotConfigured, dataSource = AiDataSource.Unavailable };
        }

        var aiResult = await _aiService.AnalyzeRecipeAsync(recipeText, cancellationToken);

        if (aiResult == null)
        {
            _logger.LogWarning("OpenAI recipe analysis returned no result for user {UserId}", userId);

            var previous = await _fallbackRepository.GetLatestUserRecipeAnalysisAsync(userId, cancellationToken);
            if (previous != null)
            {
                return await MapStoredAnalysisAsync(
                    previous, recipeText, userId, AiDataSource.Database, AiMessages.AiUnavailableUseDatabase,
                    saveNew: true, cancellationToken);
            }

            var template = await _fallbackRepository.GetRecipeTemplateAsync(cancellationToken: cancellationToken);
            if (template != null)
            {
                return await MapTemplateAnalysisAsync(template, recipeText, userId, cancellationToken);
            }

            return new { success = false, message = AiMessages.InformationUnavailable, dataSource = AiDataSource.Unavailable };
        }

        return await SaveAiAnalysisAsync(userId, recipeText, aiResult, AiDataSource.Ai, string.Empty, cancellationToken);
    }

    private async Task<object> SaveAiAnalysisAsync(
        string userId,
        string recipeText,
        RecipeAnalysisResult aiResult,
        string dataSource,
        string userMessage,
        CancellationToken cancellationToken)
    {
        var recipe = new Recipe
        {
            UserId = userId,
            RawText = recipeText,
            Title = string.IsNullOrWhiteSpace(aiResult.RecipeName) ? "Analyzed Recipe" : aiResult.RecipeName,
            CreatedAt = DateTime.UtcNow
        };
        _context.Recipes.Add(recipe);
        await _context.SaveChangesAsync(cancellationToken);

        var servings = Math.Max(1, aiResult.Servings <= 0 ? 1 : aiResult.Servings);
        var ingredientList = aiResult.Ingredients ?? Array.Empty<RecipeIngredientResult>();
        var alternativeList = aiResult.Alternatives ?? Array.Empty<string>();
        var ingredients = ingredientList
            .Select(i => new { name = i.Name ?? string.Empty, amount = i.Amount ?? string.Empty, calories = i.Calories })
            .ToArray();

        var analysis = new RecipeAnalysis
        {
            RecipeId = recipe.Id,
            UserId = userId,
            TotalCalories = Math.Max(0, aiResult.TotalCalories),
            Servings = servings,
            Protein = aiResult.Protein,
            Carbs = aiResult.Carbs,
            Fat = aiResult.Fat,
            PerServingCalories = Math.Max(0, aiResult.TotalCalories) / servings,
            PerServingProtein = aiResult.Protein / servings,
            PerServingCarbs = aiResult.Carbs / servings,
            PerServingFat = aiResult.Fat / servings,
            IngredientsJson = JsonSerializer.Serialize(ingredients),
            AlternativesJson = JsonSerializer.Serialize(alternativeList),
            CreatedAt = DateTime.UtcNow
        };
        _context.RecipeAnalyses.Add(analysis);
        await _context.SaveChangesAsync(cancellationToken);

        return BuildPayload(
            recipe.Title ?? "Analyzed Recipe",
            analysis,
            ingredients,
            alternativeList.ToArray(),
            dataSource,
            string.IsNullOrEmpty(userMessage) ? "Recipe analyzed successfully." : userMessage);
    }

    private async Task<object> MapTemplateAnalysisAsync(
        SeedRecipeTemplate template,
        string recipeText,
        string userId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(template.AnalysisJson);
            var root = doc.RootElement;

            var recipe = new Recipe
            {
                UserId = userId,
                RawText = recipeText,
                Title = template.RecipeName,
                CreatedAt = DateTime.UtcNow
            };
            _context.Recipes.Add(recipe);
            await _context.SaveChangesAsync(cancellationToken);

            var totalCalories = root.GetProperty("totalCalories").GetInt32();
            var servings = Math.Max(1, root.GetProperty("servings").GetInt32());
            var protein = root.GetProperty("protein").GetDouble();
            var carbs = root.GetProperty("carbs").GetDouble();
            var fat = root.GetProperty("fat").GetDouble();
            var ingredientsJson = root.GetProperty("ingredients").GetRawText();
            var alternativesJson = root.GetProperty("alternatives").GetRawText();

            var analysis = new RecipeAnalysis
            {
                RecipeId = recipe.Id,
                UserId = userId,
                TotalCalories = totalCalories,
                Servings = servings,
                Protein = protein,
                Carbs = carbs,
                Fat = fat,
                PerServingCalories = totalCalories / servings,
                PerServingProtein = protein / servings,
                PerServingCarbs = carbs / servings,
                PerServingFat = fat / servings,
                IngredientsJson = ingredientsJson,
                AlternativesJson = alternativesJson,
                CreatedAt = DateTime.UtcNow
            };
            _context.RecipeAnalyses.Add(analysis);
            await _context.SaveChangesAsync(cancellationToken);

            return BuildPayload(
                recipe.Title ?? template.RecipeName,
                analysis,
                DeserializeIngredients(ingredientsJson),
                DeserializeAlternatives(alternativesJson),
                AiDataSource.Database,
                AiMessages.AiUnavailableUseDatabase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to map recipe template analysis for user {UserId}", userId);
            throw;
        }
    }

    private async Task<object> MapStoredAnalysisAsync(
        RecipeAnalysis previous,
        string recipeText,
        string userId,
        string dataSource,
        string message,
        bool saveNew,
        CancellationToken cancellationToken)
    {
        if (!saveNew)
        {
            return BuildPayload(
                "Saved recipe analysis",
                previous,
                DeserializeIngredients(previous.IngredientsJson),
                DeserializeAlternatives(previous.AlternativesJson),
                dataSource,
                message);
        }

        var recipe = new Recipe
        {
            UserId = userId,
            RawText = recipeText,
            Title = "Saved recipe analysis",
            CreatedAt = DateTime.UtcNow
        };
        _context.Recipes.Add(recipe);
        await _context.SaveChangesAsync(cancellationToken);

        var copy = new RecipeAnalysis
        {
            RecipeId = recipe.Id,
            UserId = userId,
            TotalCalories = previous.TotalCalories,
            Servings = previous.Servings,
            Protein = previous.Protein,
            Carbs = previous.Carbs,
            Fat = previous.Fat,
            PerServingCalories = previous.PerServingCalories,
            PerServingProtein = previous.PerServingProtein,
            PerServingCarbs = previous.PerServingCarbs,
            PerServingFat = previous.PerServingFat,
            IngredientsJson = previous.IngredientsJson,
            AlternativesJson = previous.AlternativesJson,
            CreatedAt = DateTime.UtcNow
        };
        _context.RecipeAnalyses.Add(copy);
        await _context.SaveChangesAsync(cancellationToken);

        return BuildPayload(
            recipe.Title ?? "Saved recipe analysis",
            copy,
            DeserializeIngredients(copy.IngredientsJson),
            DeserializeAlternatives(copy.AlternativesJson),
            dataSource,
            message);
    }

    private static object[] DeserializeIngredients(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<object[]>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string[] DeserializeAlternatives(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static object BuildPayload(
        string recipeName,
        RecipeAnalysis analysis,
        object? ingredients,
        string[] alternatives,
        string dataSource,
        string message) =>
        new
        {
            success = true,
            message,
            dataSource,
            recipeName,
            totalCalories = analysis.TotalCalories,
            servings = analysis.Servings,
            perServing = new
            {
                calories = analysis.PerServingCalories,
                protein = analysis.PerServingProtein,
                carbs = analysis.PerServingCarbs,
                fat = analysis.PerServingFat
            },
            macros = new { protein = analysis.Protein, carbs = analysis.Carbs, fat = analysis.Fat },
            ingredients = ingredients ?? Array.Empty<object>(),
            alternatives
        };
}
