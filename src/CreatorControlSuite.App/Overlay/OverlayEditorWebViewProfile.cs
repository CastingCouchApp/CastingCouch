using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CreatorControlSuite.App.Overlay;

/// <summary>
/// Shared WebView2 environment for the in-app Overlay Editor.
/// </summary>
public static class OverlayEditorWebViewProfile
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static CoreWebView2Environment? _environment;

    public static string UserDataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CreatorControlSuite",
        "WebView2",
        "OverlayEditor");

    public static async Task EnsureAsync(WebView2 webView)
    {
        ArgumentNullException.ThrowIfNull(webView);
        if (webView.CoreWebView2 is not null)
        {
            return;
        }

        CoreWebView2Environment environment = await GetEnvironmentAsync().ConfigureAwait(true);
        await webView.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
    }

    public static async Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        if (_environment is not null)
        {
            return _environment;
        }

        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_environment is not null)
            {
                return _environment;
            }

            Directory.CreateDirectory(UserDataFolder);
            _environment = await CoreWebView2Environment
                .CreateAsync(userDataFolder: UserDataFolder)
                .ConfigureAwait(false);
            return _environment;
        }
        finally
        {
            Gate.Release();
        }
    }
}
