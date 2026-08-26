namespace FootballFormation.UI.Components;

/// Reads take <see cref="Cancellation"/>, because a page navigated away from otherwise leaves its queries running. Writes deliberately
/// do not: an admin who taps "finish match" and loses the circuit must still have finished the match.
public abstract class CancellableComponent : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();

    /// Cancelled when this component is disposed. Pass it to every read it makes.
    protected CancellationToken Cancellation => _cancellation.Token;

    /// Overriders must call <c>base.Dispose()</c>, or the reads outlive the component again.
    public virtual void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
        GC.SuppressFinalize(this);
    }
}
