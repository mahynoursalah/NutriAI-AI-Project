using System.ComponentModel.DataAnnotations;
using NutriAI.Application.Configuration;

namespace NutriAI.ViewModels.Admin;

public class AiSettingsViewModel
{
    [Display(Name = "OpenAI API Key")]
    [DataType(DataType.Password)]
    public string? ApiKey { get; set; }

    [Required(ErrorMessage = "Please select a model.")]
    [Display(Name = "Model")]
    public string SelectedModel { get; set; } = OpenAiModelOptions.DefaultModel;

    public string? ApiKeyLastFour { get; set; }

    public IReadOnlyList<string> AvailableModels { get; set; } = OpenAiModelOptions.SupportedModels;
}
