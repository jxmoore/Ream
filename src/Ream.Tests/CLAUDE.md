# CLAUDE.md (src/Ream.Tests)

Test-harness rules. Loaded only when working under this directory. The prohibition on driving the user's live
desktop is in the root `CLAUDE.md` and always applies.

- Test editor behaviour in-process (`Ream.Tests/Ui.cs` hosts real views on a UI thread,
  off-screen, with the app's resources; raise `Click` on toolbar buttons, use
  `Editor.AppendText`, `RenderToPng` to look at output).
- `Ui.StartHost` sets the `Ream.SkipStartup` AppContext switch so constructing `App` never runs the
  real `OnStartup` (real config, real documents, a real window). Keep it. The workspace list always
  has an empty edge workspace at both ends, so `Workspaces[0]` is not the first real workspace.
- Repositories in tests: `TestReam.Repo(root)` makes a `DocumentRepository` with data folder "." beside `Test.ream`, so a test can look at
  plain paths under `root`; `new DocumentRepository("...\\Foo.ream")` gives the real sibling-folder layout. `ManagerRig`
  (`ReamManagerTests.cs`) is a running ream plus a `ReamManager` with `FakeDialogs` / `FakePrompts`, for anything about New / Open / Save /
  Save As / Clear. Never touch real user folders: temp dirs only (`TempDir`).
- Known open issue: the full suite hangs intermittently (roughly 1 run in 10, early on, with many
  unrelated tests in flight); not yet diagnosed. `Ui.Run` has timeouts; run with
  `--blame-hang --blame-hang-timeout 90s`.
- Tests that change the theme must restore dark (`Themes.Use` in the tests does), since the
  `Application` is shared by every UI test.
- `Ui.Run` serializes UI tests with a lock: `Settle()` pumps the shared UI thread, so without
  it another test's queued work runs nested inside the current one and steals process-wide
  keyboard focus (this caused a real flaky failure). Keep every test that touches WPF views
  behind `Ui.Run`. Horizontal-wheel is tested by sending `WM_MOUSEHWHEEL` to the test's own
  off-screen window, never to the real desktop.
- Layered (see-through) windows render slowly, so animation tests must wait for the result (or check
  `HasAnimatedProperties`), never sample mid-animation after `Settle()`.
