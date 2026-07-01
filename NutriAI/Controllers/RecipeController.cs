using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NutriAI.Application.Interfaces.Services;
using NutriAI.Domain.Constants;
using NutriAI.Extensions;

namespace NutriAI.Controllers;

[Authorize(Roles = Roles.User)]
public class RecipeController : Controller
{
    private readonly IRecipeService _recipeService;
    private readonly ILogger<RecipeController> _logger;

    public RecipeController(IRecipeService recipeService, ILogger<RecipeController> logger)
    {
        _recipeService = recipeService;
        _logger = logger;
    }

    public IActionResult Index()
    {
        ViewData["Title"] = "Recipe Analyzer";
        ViewData["ActiveNav"] = "Recipe";
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Analyze([FromBody] RecipeAnalyzeRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { success = false, message = "Recipe text must be at least 10 characters." });

        try
        {
            return Json(await _recipeService.AnalyzeRecipeAsync(User.GetUserId(), request.RecipeText, cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recipe analysis failed for user {UserId}", User.GetUserId());
            return StatusCode(500, new
            {
                success = false,
                message = "Recipe analysis failed. Verify OpenAI settings in Admin and try again."
            });
        }
    }
}

public class RecipeAnalyzeRequest
{
    [Required, MinLength(10), MaxLength(8000)]
    public string RecipeText { get; set; } = string.Empty;
}
