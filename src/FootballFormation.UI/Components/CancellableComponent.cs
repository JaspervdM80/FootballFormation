using Microsoft.AspNetCore.Components;

namespace FootballFormation.UI.Components;

/// <summary>
/// A component that stops its own reads when it goes away.
/// <para>
/// Blazor Server hands a component no request lifetime of its own: a page that starts a query in
/// <c>OnInitializedAsync</c> and is then navigated away from leaves that query running against the
/// database with nobody left to render it. On a phone on a bad connection — which is what this app
/// is for — that happens constantly. This owns a <see cref="CancellationTokenSource"/> tripped on
/// disposal, so <see cref="Cancellation"/> is the token every service read should be given.
/// </para>
/// <para>
/// Writes deliberately do <em>not</em> get it. An admin who taps "finish match" and then loses the
/// circuit must still have finished the match — abandoning a half-applied write to save a few
/// milliseconds of SQLite is the wrong trade — so a mutating call is left to run to completion on
/// <c>default</c>. The rule is: the token goes on the calls whose only purpose is to show
/// something.
/// </para>
/// <para>
/// A cancelled read comes back as <see cref="Core.Result.IsCancelled"/> rather than an exception,
/// so the usual <c>Snackbar.ReportFailure(...)</c> line stays exactly as it was — see
/// <c>UiFeedback</c>, which keeps quiet about one. The one thing a caller must add is a check
/// before doing something the visitor would notice, a redirect above all: leaving a page is not a
/// reason to throw them off the one they went to.
/// </para>
/// </summary>
public abstract class CancellableComponent : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();

    /// <summary>
    /// Cancelled when this component is disposed — navigated away from, closed, or its circuit
    /// dropped. Pass it to every service read the component makes.
    /// </summary>
    protected CancellationToken Cancellation => _cancellation.Token;

    /// <summary>
    /// Overriders must call <c>base.Dispose()</c>, or the component's reads outlive it again.
    /// </summary>
    public virtual void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
        GC.SuppressFinalize(this);
    }
}
