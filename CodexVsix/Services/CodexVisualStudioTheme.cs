using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json.Linq;

namespace CodexVsix.Services;

internal sealed class CodexVisualStudioTheme
{
    private CodexVisualStudioTheme(string id, string variant, IReadOnlyDictionary<string, string> colors)
    {
        Id = id;
        Variant = variant;
        Colors = colors;
    }

    public string Id { get; }

    public string Variant { get; }

    public IReadOnlyDictionary<string, string> Colors { get; }

    internal static CodexVisualStudioTheme Create(
        string id,
        string variant,
        IReadOnlyDictionary<string, string> colors)
    {
        return new CodexVisualStudioTheme(id, variant, colors);
    }

    public static CodexVisualStudioTheme Capture()
    {
        var background = GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey, Color.FromRgb(32, 32, 32));
        var foreground = GetThemedColor(
            EnvironmentColors.ToolWindowTextColorKey,
            System.Windows.Media.Colors.White);
        var muted = GetThemedColor(EnvironmentColors.SystemGrayTextColorKey, Blend(background, foreground, 0.62d));
        var panelText = GetThemedColor(EnvironmentColors.PanelTextColorKey, foreground);
        var border = GetThemedColor(EnvironmentColors.ToolWindowBorderColorKey, Blend(background, foreground, 0.18d));
        var softBorder = GetThemedColor(EnvironmentColors.PanelBorderColorKey, border);
        var surface = GetThemedColor(
            EnvironmentColors.CommandBarMenuBackgroundGradientBeginColorKey,
            Blend(background, foreground, GetBrightness(background) < 0.5d ? 0.08d : 0.04d));
        var hover = GetThemedColor(
            EnvironmentColors.CommandBarHoverColorKey,
            Blend(background, foreground, GetBrightness(background) < 0.5d ? 0.12d : 0.08d));
        var accent = GetThemedColor(EnvironmentColors.PanelHyperlinkColorKey, Color.FromRgb(0, 122, 204));
        var isDark = GetBrightness(background) < 0.5d;
        var selection = Blend(background, accent, isDark ? 0.35d : 0.20d);
        var scrollbar = Blend(background, foreground, isDark ? 0.22d : 0.18d);
        var colors = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["--vscode-foreground"] = ToCss(foreground),
            ["--vscode-descriptionForeground"] = ToCss(muted),
            ["--vscode-disabledForeground"] = ToCss(muted),
            ["--vscode-errorForeground"] = isDark ? "#f48771" : "#a1260d",
            ["--vscode-focusBorder"] = ToCss(accent),
            ["--vscode-icon-foreground"] = ToCss(muted),
            ["--vscode-editor-background"] = ToCss(background),
            ["--vscode-editor-foreground"] = ToCss(foreground),
            ["--vscode-sideBar-background"] = ToCss(background),
            ["--vscode-sideBar-foreground"] = ToCss(panelText),
            ["--vscode-dropdown-background"] = ToCss(surface),
            ["--vscode-dropdown-foreground"] = ToCss(foreground),
            ["--vscode-dropdown-border"] = ToCss(softBorder),
            ["--vscode-button-background"] = ToCss(accent),
            ["--vscode-button-foreground"] = isDark ? "#ffffff" : ToCss(background),
            ["--vscode-button-hoverBackground"] = ToCss(Blend(
                accent,
                isDark ? System.Windows.Media.Colors.White : System.Windows.Media.Colors.Black,
                0.12d)),
            ["--vscode-button-secondaryBackground"] = ToCss(surface),
            ["--vscode-button-secondaryForeground"] = ToCss(foreground),
            ["--vscode-button-secondaryHoverBackground"] = ToCss(hover),
            ["--vscode-button-border"] = ToCss(softBorder),
            ["--vscode-input-background"] = ToCss(surface),
            ["--vscode-input-foreground"] = ToCss(foreground),
            ["--vscode-input-border"] = ToCss(softBorder),
            ["--vscode-input-placeholderForeground"] = ToCss(muted),
            ["--vscode-textLink-foreground"] = ToCss(accent),
            ["--vscode-textLink-activeForeground"] = ToCss(accent),
            ["--vscode-textPreformat-foreground"] = ToCss(foreground),
            ["--vscode-textPreformat-background"] = ToCss(surface),
            ["--vscode-textCodeBlock-background"] = ToCss(surface),
            ["--vscode-badge-background"] = ToCss(accent),
            ["--vscode-badge-foreground"] = "#ffffff",
            ["--vscode-list-hoverBackground"] = ToCss(hover),
            ["--vscode-list-hoverForeground"] = ToCss(foreground),
            ["--vscode-list-activeSelectionBackground"] = ToCss(selection),
            ["--vscode-list-activeSelectionForeground"] = ToCss(foreground),
            ["--vscode-list-focusBackground"] = ToCss(hover),
            ["--vscode-list-focusForeground"] = ToCss(foreground),
            ["--vscode-list-highlightForeground"] = ToCss(accent),
            ["--vscode-scrollbarSlider-background"] = ToCss(scrollbar),
            ["--vscode-scrollbarSlider-hoverBackground"] = ToCss(Blend(background, foreground, isDark ? 0.32d : 0.28d)),
            ["--vscode-scrollbarSlider-activeBackground"] = ToCss(Blend(background, accent, 0.62d)),
            ["--vscode-panel-background"] = ToCss(background),
            ["--vscode-panel-border"] = ToCss(border),
            ["--vscode-editorWidget-background"] = ToCss(surface),
            ["--vscode-editorWidget-foreground"] = ToCss(foreground),
            ["--vscode-editorWidget-border"] = ToCss(softBorder),
            ["--vscode-editorHoverWidget-background"] = ToCss(surface),
            ["--vscode-editorHoverWidget-foreground"] = ToCss(foreground),
            ["--vscode-editorHoverWidget-border"] = ToCss(softBorder),
            ["--vscode-toolbar-hoverBackground"] = ToCss(hover),
            ["--vscode-inputValidation-infoBackground"] = ToCss(Blend(background, accent, 0.16d)),
            ["--vscode-inputValidation-infoBorder"] = ToCss(Blend(background, accent, 0.45d)),
            ["--vscode-inputValidation-warningBackground"] = isDark ? "rgba(234,179,8,.16)" : "rgba(168,112,0,.12)",
            ["--vscode-inputValidation-warningBorder"] = isDark ? "rgba(234,179,8,.55)" : "rgba(168,112,0,.55)",
            ["--vscode-inputValidation-errorBackground"] = isDark ? "rgba(239,68,68,.16)" : "rgba(161,38,13,.10)",
            ["--vscode-inputValidation-errorBorder"] = isDark ? "rgba(239,68,68,.55)" : "rgba(161,38,13,.55)",
            ["--vscode-progressBar-background"] = ToCss(accent),
            ["--vscode-activityBarBadge-background"] = ToCss(accent),
            ["--vscode-activityBarBadge-foreground"] = "#ffffff"
        };

        AddCodexAliases(colors);
        var variant = isDark ? "dark" : "light";
        var id = string.Format(
            CultureInfo.InvariantCulture,
            "visual-studio-{0}-{1:X2}{2:X2}{3:X2}",
            variant,
            background.R,
            background.G,
            background.B);
        return new CodexVisualStudioTheme(id, variant, colors);
    }

    public JObject ToMessage()
    {
        return new JObject
        {
            ["type"] = "theme-updated",
            ["theme"] = new JObject
            {
                ["id"] = Id,
                ["type"] = Variant,
                ["variant"] = Variant,
                ["colors"] = JObject.FromObject(Colors)
            }
        };
    }

    public string ToCss()
    {
        var builder = new StringBuilder();
        builder.Append(":root{color-scheme:").Append(Variant).Append(';');
        foreach (var pair in Colors)
        {
            builder.Append(pair.Key).Append(':').Append(pair.Value).Append(';');
        }

        builder.Append("}")
            .Append("html,body,#root{background:var(--vscode-sideBar-background)!important;color:var(--vscode-foreground);}");
        return builder.ToString();
    }

    private static void AddCodexAliases(IDictionary<string, string> colors)
    {
        AddAlias(colors, "--token-foreground", "--vscode-foreground");
        AddAlias(colors, "--token-text-primary", "--vscode-foreground");
        AddAlias(colors, "--token-text-secondary", "--vscode-descriptionForeground");
        AddAlias(colors, "--token-text-tertiary", "--vscode-disabledForeground");
        AddAlias(colors, "--token-text-quaternary", "--vscode-disabledForeground");
        AddAlias(colors, "--token-description-foreground", "--vscode-descriptionForeground");
        AddAlias(colors, "--token-side-bar-background", "--vscode-sideBar-background");
        AddAlias(colors, "--token-dropdown-background", "--vscode-dropdown-background");
        AddAlias(colors, "--token-main-surface-primary", "--vscode-editor-background");
        AddAlias(colors, "--token-main-surface-secondary", "--vscode-panel-background");
        AddAlias(colors, "--token-main-surface-tertiary", "--vscode-editorWidget-background");
        AddAlias(colors, "--token-border", "--vscode-panel-border");
        AddAlias(colors, "--token-border-default", "--vscode-panel-border");
        AddAlias(colors, "--token-border-light", "--vscode-editorWidget-border");
        AddAlias(colors, "--token-border-medium", "--vscode-editorWidget-border");
        AddAlias(colors, "--token-button-secondary-hover-background", "--vscode-button-secondaryHoverBackground");
        AddAlias(colors, "--color-text-foreground", "--vscode-foreground");
        AddAlias(colors, "--color-text-foreground-secondary", "--vscode-descriptionForeground");
        AddAlias(colors, "--color-text-foreground-tertiary", "--vscode-disabledForeground");
        AddAlias(colors, "--color-text-error", "--vscode-errorForeground");
        AddAlias(colors, "--color-text-accent", "--vscode-textLink-foreground");
        AddAlias(colors, "--color-icon-primary", "--vscode-icon-foreground");
        AddAlias(colors, "--color-border-focus", "--vscode-focusBorder");
        AddAlias(colors, "--color-border", "--vscode-panel-border");
        AddAlias(colors, "--color-border-light", "--vscode-editorWidget-border");
        AddAlias(colors, "--color-border-heavy", "--vscode-panel-border");
        AddAlias(colors, "--color-background-surface", "--vscode-editor-background");
        AddAlias(colors, "--color-background-surface-under", "--vscode-sideBar-background");
        AddAlias(colors, "--color-background-editor-opaque", "--vscode-editor-background");
        AddAlias(colors, "--color-background-elevated-primary", "--vscode-editorWidget-background");
        AddAlias(colors, "--color-background-elevated-primary-opaque", "--vscode-editorWidget-background");
        AddAlias(colors, "--color-background-elevated-secondary", "--vscode-panel-background");
        AddAlias(colors, "--color-background-status-error", "--vscode-inputValidation-errorBackground");
        AddAlias(colors, "--color-background-status-warning", "--vscode-inputValidation-warningBackground");
        AddAlias(colors, "--color-background-status-success", "--vscode-inputValidation-infoBackground");
        AddAlias(colors, "--color-accent-red", "--vscode-errorForeground");
        AddAlias(colors, "--color-accent-blue", "--vscode-focusBorder");
        AddAlias(colors, "--color-accent-purple", "--vscode-textLink-foreground");
        colors["--color-accent-yellow"] = colors["--vscode-inputValidation-warningBorder"];
        colors["--color-accent-green"] = "#89d185";
        colors["--codex-titlebar-tint"] = "transparent";
    }

    private static void AddAlias(IDictionary<string, string> colors, string target, string source)
    {
        colors[target] = colors[source];
    }

    private static Color GetThemedColor(ThemeResourceKey key, Color fallback)
    {
        try
        {
            var color = VSColorTheme.GetThemedColor(key);
            return Color.FromArgb(color.A, color.R, color.G, color.B);
        }
        catch
        {
            return fallback;
        }
    }

    private static double GetBrightness(Color color)
    {
        return ((color.R * 0.299d) + (color.G * 0.587d) + (color.B * 0.114d)) / 255d;
    }

    private static Color Blend(Color background, Color foreground, double amount)
    {
        amount = Math.Max(0d, Math.Min(1d, amount));
        return Color.FromRgb(
            (byte)Math.Round(background.R + ((foreground.R - background.R) * amount)),
            (byte)Math.Round(background.G + ((foreground.G - background.G) * amount)),
            (byte)Math.Round(background.B + ((foreground.B - background.B) * amount)));
    }

    private static string ToCss(Color color)
    {
        if (color.A == byte.MaxValue)
        {
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "rgba({0},{1},{2},{3:0.###})",
            color.R,
            color.G,
            color.B,
            color.A / 255d);
    }
}
