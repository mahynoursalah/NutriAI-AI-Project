using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NutriAI.Application.DTOs;
using NutriAI.Application.Interfaces.Services;
using NutriAI.Domain.Constants;
using NutriAI.ViewModels.Admin;

namespace NutriAI.Controllers;

[Authorize(Roles = Roles.Admin)]
public class AdminController : Controller
{
    private readonly IAdminService _adminService;
    private readonly IAiSettingsService _aiSettingsService;

    public AdminController(IAdminService adminService, IAiSettingsService aiSettingsService)
    {
        _adminService = adminService;
        _aiSettingsService = aiSettingsService;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Admin Dashboard";
        ViewData["ActiveNav"] = "Admin";
        return View(await BuildAiSettingsViewModelAsync(cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> AiSettings(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Admin Dashboard";
        ViewData["ActiveNav"] = "Admin";
        return View("Index", await BuildAiSettingsViewModelAsync(cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AiSettings(AiSettingsViewModel viewModel, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Admin Dashboard";
        ViewData["ActiveNav"] = "Admin";

        var formData = await _aiSettingsService.GetFormDataAsync(cancellationToken);
        viewModel.AvailableModels = formData.AvailableModels;
        viewModel.ApiKeyLastFour = formData.ApiKeyLastFour;
        if (string.IsNullOrWhiteSpace(viewModel.ApiKey) && string.IsNullOrWhiteSpace(formData.ApiKeyLastFour))
            ModelState.AddModelError(nameof(viewModel.ApiKey), "OpenAI API Key is required.");

        if (!ModelState.IsValid)
            return View("Index", viewModel);

        var result = await _aiSettingsService.SaveAsync(viewModel.ApiKey, viewModel.SelectedModel, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return View("Index", viewModel);
        }

        TempData["AiSettingsSuccess"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestAiSettings(CancellationToken cancellationToken)
    {
        var result = await _aiSettingsService.TestConnectionAsync(cancellationToken);
        return Json(new { success = result.Succeeded, message = result.Message, model = result.Model });
    }

    private async Task<AiSettingsViewModel> BuildAiSettingsViewModelAsync(CancellationToken cancellationToken)
    {
        var formData = await _aiSettingsService.GetFormDataAsync(cancellationToken);
        return new AiSettingsViewModel
        {
            SelectedModel = formData.SelectedModel,
            ApiKeyLastFour = formData.ApiKeyLastFour,
            AvailableModels = formData.AvailableModels
        };
    }

    [HttpGet]
    public async Task<IActionResult> GetStats(CancellationToken cancellationToken) =>
        Json(await _adminService.GetStatsAsync(cancellationToken));

    [HttpGet]
    public async Task<IActionResult> GetUsers(int page = 1, string? search = null, CancellationToken cancellationToken = default) =>
        Json(await _adminService.GetUsersAsync(search, page, cancellationToken));

    [HttpGet]
    public async Task<IActionResult> GetUser(string id, CancellationToken cancellationToken)
    {
        var user = await _adminService.GetUserAsync(id, cancellationToken);
        return user == null ? NotFound(new { success = false, message = "User not found." }) : Json(user);
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] AdminCreateUserDto dto, CancellationToken cancellationToken) =>
        Json(await _adminService.CreateUserAsync(dto, cancellationToken));

    [HttpPut]
    public async Task<IActionResult> UpdateUser(string id, [FromBody] AdminUpdateUserDto dto, CancellationToken cancellationToken) =>
        Json(await _adminService.UpdateUserAsync(id, dto, cancellationToken));

    [HttpDelete]
    public async Task<IActionResult> DeleteUser(string id, CancellationToken cancellationToken) =>
        Json(await _adminService.DeleteUserAsync(id, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> SetBan(string id, [FromBody] BanRequest request, CancellationToken cancellationToken) =>
        Json(await _adminService.SetBanAsync(id, request.Banned, cancellationToken));

    [HttpGet]
    public async Task<IActionResult> GetMealLogs(int page = 1, string? userId = null, CancellationToken cancellationToken = default) =>
        Json(await _adminService.GetMealLogsAsync(page, userId, cancellationToken));

    [HttpGet]
    public async Task<IActionResult> GetRecipeAnalyses(int page = 1, string? userId = null, CancellationToken cancellationToken = default) =>
        Json(await _adminService.GetRecipeAnalysesAsync(page, userId, cancellationToken));

    [HttpGet]
    public async Task<IActionResult> GetWeeklyReports(int page = 1, string? userId = null, CancellationToken cancellationToken = default) =>
        Json(await _adminService.GetWeeklyReportsAsync(page, userId, cancellationToken));
}

public class BanRequest
{
    public bool Banned { get; set; }
}
