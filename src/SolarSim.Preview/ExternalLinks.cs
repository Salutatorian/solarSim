using System.Diagnostics;
using System.Windows;
using SolarSim.Domain.Estimate;

namespace SolarSim.Preview;

/// <summary>Opens only allowlisted official HTTPS pages in the system browser.</summary>
internal static class ExternalLinks
{
    public const string Repo = "https://github.com/Salutatorian/solarSim";
    public const string Releases = "https://github.com/Salutatorian/solarSim/releases";
    public const string LatestRelease = "https://github.com/Salutatorian/solarSim/releases/latest";
    public const string License = "https://github.com/Salutatorian/solarSim/blob/main/LICENSE";
    public const string Ownership = "https://github.com/Salutatorian/solarSim/blob/main/OWNERSHIP.md";
    public const string BugIssue = "https://github.com/Salutatorian/solarSim/issues/new?template=bug_report.yml";
    public const string SuggestionIssue = "https://github.com/Salutatorian/solarSim/issues/new?template=suggestion.yml";
    public const string Issues = "https://github.com/Salutatorian/solarSim/issues";
    public const string WebView2 = "https://developer.microsoft.com/microsoft-edge/webview2/";

    /// <summary>Stripe Payment Links (USD) — card data never touches solarSim.</summary>
    public const string Donate1 = "https://donate.stripe.com/eVq7sM9CF6MS01H7rj5AQ03";
    public const string Donate3 = "https://donate.stripe.com/9B6bJ24ilefkcOth1T5AQ04";
    public const string Donate5 = "https://donate.stripe.com/aFa14ocORefk9Chh1T5AQ05";

    public static void Open(string url, DependencyObject? owner = null)
    {
        var window = owner as Window ?? (owner is null ? null : Window.GetWindow(owner));
        if (!IsAllowed(url))
        {
            AppConfirmDialog.Alert(
                window,
                "Blocked an unexpected link for safety.\n\nsolarSim only opens known official HTTPS pages.",
                "solarSim",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppConfirmDialog.Alert(
                window,
                $"Could not open browser:\n{ex.Message}\n\nOpen this URL manually:\n{url}",
                "solarSim",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    public static bool IsAllowed(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps) return false;
        var host = uri.Host.ToLowerInvariant();
        if (host is "github.com"
            or "www.github.com"
            or "developer.microsoft.com"
            or "console.cloud.google.com"
            or "developers.google.com"
            or "maps.googleapis.com"
            or "solar.googleapis.com"
            or "donate.stripe.com"
            or "buy.stripe.com"
            or "checkout.stripe.com"
            or "stripe.com"
            or "www.stripe.com"
            or "eia.gov"
            or "www.eia.gov"
            or "openei.org"
            or "www.openei.org"
            or "apps.openei.org")
            return true;
        return UsUtilityCatalog.IsKnownRatesHost(host);
    }
}
