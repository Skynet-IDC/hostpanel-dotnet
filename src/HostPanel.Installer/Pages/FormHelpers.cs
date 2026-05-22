using System.Text;

namespace HostPanel.Installer.Pages;

/// <summary>
/// Tiny string-builder helpers so each wizard page can spit out
/// `<label><input type=checkbox …></label>` etc. without copy-pasting boilerplate.
/// Every value is HTML-escaped — page authors don't need to think about it.
/// </summary>
public static class FormHelpers
{
    public static string E(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");

    /// <summary>Checkbox row with label + optional sub-text. `nameAttr` is the form field.</summary>
    public static string Check(string nameAttr, string label, bool isChecked, string? sub = null)
    {
        var subHtml = sub is null ? "" : $"<div class='check-detail' style='margin-left:1.5rem'>{E(sub)}</div>";
        var checkedAttr = isChecked ? "checked" : "";
        return $"""
<div style="margin-bottom:.75rem">
  <label style="display:flex; align-items:flex-start; gap:.5rem; cursor:pointer">
    <input type="checkbox" name="{E(nameAttr)}" value="true" {checkedAttr} style="margin-top:3px">
    <span>{E(label)}</span>
  </label>
  {subHtml}
</div>
""";
    }

    /// <summary>Text input row with label.</summary>
    public static string Text(string nameAttr, string label, string? value, string? placeholder = null, string inputType = "text", bool required = false)
    {
        var req = required ? "required" : "";
        var ph = placeholder is null ? "" : $"placeholder=\"{E(placeholder)}\"";
        return $"""
<div style="margin-bottom:1rem">
  <label style="display:block; font-weight:500; margin-bottom:.25rem">{E(label)}{(required ? " <span style='color:var(--block)'>*</span>" : "")}</label>
  <input type="{E(inputType)}" name="{E(nameAttr)}" value="{E(value)}" {ph} {req}
         style="width:100%; padding:.5rem .65rem; border:1px solid var(--border); border-radius:6px; background:var(--card); color:var(--fg); font:inherit">
</div>
""";
    }

    /// <summary>Textarea row.</summary>
    public static string Area(string nameAttr, string label, string? value, string? placeholder = null, int rows = 4)
    {
        var ph = placeholder is null ? "" : $"placeholder=\"{E(placeholder)}\"";
        return $"""
<div style="margin-bottom:1rem">
  <label style="display:block; font-weight:500; margin-bottom:.25rem">{E(label)}</label>
  <textarea name="{E(nameAttr)}" rows="{rows}" {ph}
         style="width:100%; padding:.5rem .65rem; border:1px solid var(--border); border-radius:6px; background:var(--card); color:var(--fg); font:inherit; font-family:ui-monospace,monospace; font-size:13px">{E(value)}</textarea>
</div>
""";
    }

    /// <summary>Radio group. options is (value, label, sub) tuples.</summary>
    public static string Radio(string nameAttr, string heading, string? currentValue, params (string val, string label, string? sub)[] options)
    {
        var sb = new StringBuilder();
        sb.Append($"<div style='margin-bottom:1rem'><label style='display:block; font-weight:500; margin-bottom:.5rem'>{E(heading)}</label>");
        foreach (var (val, label, sub) in options)
        {
            var checkedAttr = val == currentValue ? "checked" : "";
            var subHtml = sub is null ? "" : $"<div class='check-detail' style='margin-left:1.5rem; margin-top:2px'>{E(sub)}</div>";
            sb.Append($"""
<div style="margin-bottom:.5rem">
  <label style="display:flex; align-items:flex-start; gap:.5rem; cursor:pointer">
    <input type="radio" name="{E(nameAttr)}" value="{E(val)}" {checkedAttr} style="margin-top:3px">
    <span>{E(label)}</span>
  </label>
  {subHtml}
</div>
""");
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>Select dropdown.</summary>
    public static string Select(string nameAttr, string label, string? currentValue, params (string val, string text)[] options)
    {
        var sb = new StringBuilder();
        sb.Append($"<div style='margin-bottom:1rem'><label style='display:block; font-weight:500; margin-bottom:.25rem'>{E(label)}</label>");
        sb.Append($"<select name='{E(nameAttr)}' style='width:100%; padding:.5rem .65rem; border:1px solid var(--border); border-radius:6px; background:var(--card); color:var(--fg); font:inherit'>");
        foreach (var (val, text) in options)
        {
            var sel = val == currentValue ? "selected" : "";
            sb.Append($"<option value='{E(val)}' {sel}>{E(text)}</option>");
        }
        sb.Append("</select></div>");
        return sb.ToString();
    }

    /// <summary>Form wrapper with back/continue buttons.</summary>
    public static string Form(string action, string innerHtml, string? backHref = null, string continueText = "Continue →") =>
        $"""
<form method="post" action="{E(action)}">
  {innerHtml}
  <div class="actions">
    {(backHref is null ? "" : $"<a class='btn' href='{E(backHref)}'>← Back</a>")}
    <button type="submit" class="btn primary">{E(continueText)}</button>
  </div>
</form>
""";

    /// <summary>Read a form bool (checkbox absent = false, value="true" = true).</summary>
    public static bool Bool(IFormCollection form, string key) =>
        form.TryGetValue(key, out var v) && v.ToString().Equals("true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Read a form string (null-or-whitespace returns null).</summary>
    public static string? Str(IFormCollection form, string key) =>
        form.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.ToString() : null;

    /// <summary>Read a form int.</summary>
    public static int? Int(IFormCollection form, string key) =>
        Str(form, key) is { } s && int.TryParse(s, out var i) ? i : null;
}
