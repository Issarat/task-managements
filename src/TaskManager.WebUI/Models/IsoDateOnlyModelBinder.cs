using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Localization;

namespace TaskManager.WebUI.Models;

// HTML date inputs submit Gregorian ISO dates even when the UI uses th-TH.
public sealed class IsoDateOnlyModelBinder(
    IStringLocalizer<SharedResource> text) : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext context)
    {
        var value = context.ValueProvider.GetValue(context.ModelName);
        if (value == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        context.ModelState.SetModelValue(context.ModelName, value);
        if (string.IsNullOrWhiteSpace(value.FirstValue))
        {
            context.Result = ModelBindingResult.Success(null);
        }
        else if (DateOnly.TryParseExact(value.FirstValue, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            context.Result = ModelBindingResult.Success(date);
        }
        else
        {
            context.ModelState.TryAddModelError(context.ModelName,
                text["TaskInvalidDate"].Value);
            context.Result = ModelBindingResult.Failed();
        }

        return Task.CompletedTask;
    }
}
