using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace FootballFormation.UI.Components;

/// <summary>
/// Bottom banner prompting mobile visitors to install the PWA. Android gets the native
/// install prompt via window.pwaInstall (js/pwa.js); iOS has no install API, so it gets
/// the Share → Add to Home Screen instruction instead.
/// </summary>
public partial class InstallBanner
{
    private const string IosInstruction = "Tap Share, then \"Add to Home Screen\".";
    private const string AndroidFallbackInstruction = "In the browser menu (⋮), tap \"Add to Home screen\".";

    [Inject] private IJSRuntime Js { get; set; } = null!;

    private bool Visible { get; set; }
    private bool ShowInstallButton { get; set; }
    private string Instruction { get; set; } = "Add the app to your home screen.";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            var status = await Js.InvokeAsync<InstallStatus>("pwaInstall.getStatus");
            if (status.Installed || status.Dismissed || !status.IsMobile)
            {
                return;
            }

            ShowInstallButton = !status.IsIos;
            Instruction = status.IsIos ? IosInstruction : "Add the app to your home screen.";
            Visible = true;
            StateHasChanged();
        }
        catch (JSException)
        {
            // pwa.js not loaded (e.g. prerender or old cached page) — no banner, no harm
        }
    }

    private async Task InstallAsync()
    {
        var outcome = await Js.InvokeAsync<string>("pwaInstall.prompt");
        if (outcome == "accepted")
        {
            Visible = false;
        }
        else if (outcome == "unavailable")
        {
            // Browser never offered the native prompt (e.g. already installed once,
            // or non-Chrome Android browser) — fall back to manual instructions
            ShowInstallButton = false;
            Instruction = AndroidFallbackInstruction;
        }
    }

    private async Task DismissAsync()
    {
        Visible = false;
        await Js.InvokeVoidAsync("pwaInstall.dismiss");
    }

    private sealed record InstallStatus(bool Installed, bool IsIos, bool IsMobile, bool Dismissed);
}
