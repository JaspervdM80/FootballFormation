# Result

- **A cancelled call is a failure with no message, and both halves matter.** Threading a
  `CancellationToken` into services makes an ordinary navigation-away throw
  `OperationCanceledException` from inside EF. `ServiceOperation.RunAsync` catches it *ahead of*
  its general handler, or every visitor leaving a page would log an error with a stack trace and
  raise "Failed to load games" — on the page they moved to, because the snackbar lives on the
  circuit and not on the page that made the call. `Result.Cancelled()` therefore keeps `IsFailure`
  true (so every existing success check still reads "no") while carrying a null `ErrorKey`, and
  `UiFeedback` shows nothing for one. **`Result.To<T>()` must carry `IsCancelled` too** — drop it
  when handing a result up between services and the cancellation arrives at the page as a
  messageless failure, which is an empty red snackbar.
- **The catch filter is load-bearing**: `when (cancellationToken.IsCancellationRequested)`. An
  `OperationCanceledException` raised while the caller's own token is untouched is a bug, not
  someone leaving, and must keep falling through to the error log.
- **A cancelled load must not redirect.** The pages that treat "not found" as a reason to
  `Trail.Redirect(...)` have to check `result.IsCancelled` first, or abandoning the load throws the
  visitor off whatever page they actually navigated to — a navigation that fights the one they
  just made. `MatchResult`, `FormationBuilder`, `FormationOverview` and `PlayerStats` all carry
  that check.
- **Reading `Result<T>.Value` on a failure throws**: it used to return `default`, so a caller that
  skipped the success check got a null three frames away instead of an error where the mistake was.
  Check `IsSuccess` (or let `Snackbar.ReportFailure` do it — it returns a bool for exactly this).
- **Failure messages are templates, not interpolated strings**: `Result.Failure("Season {0} still
  has {1} games", name, count)`, never `$"..."`. The template is the resource key, so an
  interpolated message can't be translated.

