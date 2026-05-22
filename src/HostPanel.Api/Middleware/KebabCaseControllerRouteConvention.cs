using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Routing;

namespace HostPanel.Api.Middleware;

/// <summary>
/// For every controller whose [Route] contains "[controller]", add a SECOND selector
/// where [controller] is converted from PascalCase to kebab-case. So
/// `[Route("api/v1/[controller]")]` on ServicePlansController exposes both
/// /api/v1/ServicePlans AND /api/v1/service-plans.
///
/// Fixes BUG-007 — scripted REST callers using kebab-case URLs (e.g. the React UI's
/// generated API client, or curl scripts) get 404 on PascalCase routes.
/// </summary>
public class KebabCaseControllerRouteConvention : IControllerModelConvention
{
    public void Apply(ControllerModel controller)
    {
        var kebabName = PascalToKebab(controller.ControllerName);
        if (kebabName == controller.ControllerName.ToLowerInvariant())
            return; // single-word controller — no conversion needed; routes are already case-insensitive

        var newSelectors = new List<SelectorModel>();
        foreach (var selector in controller.Selectors)
        {
            if (selector.AttributeRouteModel?.Template is { } template
                && template.Contains("[controller]", StringComparison.OrdinalIgnoreCase))
            {
                var kebabTemplate = template.Replace("[controller]", kebabName, StringComparison.OrdinalIgnoreCase);
                newSelectors.Add(new SelectorModel(selector)
                {
                    AttributeRouteModel = new AttributeRouteModel { Template = kebabTemplate }
                });
            }
        }
        foreach (var s in newSelectors) controller.Selectors.Add(s);
    }

    private static string PascalToKebab(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return Regex.Replace(input, "(?<!^)([A-Z])", "-$1").ToLowerInvariant();
    }
}
