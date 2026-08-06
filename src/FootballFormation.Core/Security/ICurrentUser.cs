namespace FootballFormation.Core.Security;

/// <summary>
/// Who is asking, as far as a Core service is concerned.
/// <para>
/// The app enforces admin rights in the render tree — every mutating control sits inside an
/// <c>&lt;AuthorizeView Roles="Admin"&gt;</c>, and an unrendered event handler has no id anyone can
/// dispatch to. That holds, but it is the only thing holding: a service reached any other way (a
/// future minimal API, a control that outgrows its wrapper) would run for anybody. This is the
/// second line, so refusing is the default rather than a thing each caller must remember.
/// </para>
/// <para>
/// An interface, unlike every other service here, because it genuinely has two implementations:
/// the circuit's signed-in principal at runtime, and a fixed answer under test. The
/// no-interfaces-for-testability rule is about the ones that only ever have one.
/// </para>
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// True when the caller may change data. Async because in a Blazor Server circuit the
    /// principal is resolved through <c>AuthenticationStateProvider</c>, which is asynchronous.
    /// </summary>
    Task<bool> IsAdminAsync();
}
