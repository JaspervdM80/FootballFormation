namespace FootballFormation.UI.Components;

/// <summary>
/// A component that stops its own reads when it goes away. Blazor Server gives a component no
/// request lifetime of its own, so a page navigated away from otherwise leaves its queries running
/// with nobody left to render them.
/// <para>
/// Reads take <see cref="Cancellation"/>; writes deliberately do not — an admin who taps "finish
/// match" and then loses the circuit must still have finished the match. A caller whose failure
/// branch does something the visitor would notice checks <c>IsCancelled</c> first.
/// See docs/ui_components/shared-components.md.
/// </para>
/// </summary>
public abstract class CancellableComponent : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();

    /// <summary>Cancelled when this component is disposed. Pass it to every read it makes.</summary>
    protected CancellationToken Cancellation => _cancellation.Token;

    /// <summary>Overriders must call <c>base.Dispose()</c>, or the reads outlive the component again.</summary>
    public virtual void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
        GC.SuppressFinalize(this);
    }
}
