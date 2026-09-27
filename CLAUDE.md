# CLAUDE.md

Guidance for working in this repository.

## What this is

Ream — a Windows desktop note-taking app (C# / WPF / .NET 8) with a
niri-style infinite layout: workspaces stack vertically (Alt+scroll to switch),
notes sit in a horizontally scrolling row per workspace, new notes open to the
right of the focused one. Notes are rich text (FlowDocument) with inline pasted
images. The earlier React/TypeScript/RxDB prototype was deleted; it exists only
in git history and should not be used as a reference.

The design and staged build plan (M0–M5) were agreed with the user; the plan
file is `C:\Users\JoeMo\.claude\plans\playful-crafting-taco.md`.

## Architecture

Layout is two custom `Panel`s with manually animated offsets (a vertical
workspace strip, a horizontal note row per workspace), not `ScrollViewer`
virtualization: `Ream.App/Controls/WorkspaceStripPanel.cs` and `NoteRowPanel.cs`.
The row geometry (column widths, minimal-scroll vs centered offset) is pure math in
`Ream.Core/Layout/RowLayout.cs` and is unit-tested; keep it there, not in the panel.
Animation goes through `Ream.App/Animation/Motion.cs` (respects `AppConfig.Animations`).
Empty workspaces are pruned only after a switch animation settles
(`AppViewModel.PruneEmptyWorkspacesCommand`), using `SuppressAnimation` so the strip
snaps rather than animates when the list shifts under it.

Themes: `Ream.Core/Models/ThemeCatalog.cs` lists the ids (dark = default, light, dracula, catppuccin,
material, nord, gruvbox; unknown ids and the retired "system" fall back to dark). Every
`Themes/<Id>.xaml` defines the same brush keys and stays readable (tests enforce both, incl.
contrast ratios). `Themes/Controls.xaml` holds the themed Button/ToggleButton/ComboBox/TextBox/
ScrollBar/Slider/ContextMenu/MenuItem styles. Always reference brushes with `{DynamicResource ...}`,
never StaticResource, or a theme switch won't reach them. `ThemeService` swaps
`Application.Resources.MergedDictionaries[0]` and publishes `CanvasBrush` (alpha from `canvasOpacity`,
min 1 so a 0% window still catches clicks), `RibbonBrush` and `WindowBorderBrush` (the ribbon and the
1 px window outline, both exactly the canvas alpha, so at 0% nothing of Ream is behind the notes),
`NoteBrush` (each note's card, alpha from `noteOpacity`, may be 0), `ChromeHoverBrush` / `RibbonHoverBrush` (the canvas / toolbar
color at max(canvas alpha, up to 85% as the canvas fades to clear)). Ream's own bare text (tab labels, title, group labels, workspace
label) floats unbacked at low opacity; the title bar, tab row, ribbon panel and workspace label swap their clear `Background` for
the hover brush in an `IsMouseOver` style trigger (so set the base Background in the style, not as a local attribute, or the trigger
loses), which keeps light text readable over a bright desktop exactly when you reach for it. And
`FocusBorderBrush` (`layout.focusBorderColor`, else the theme accent).
The window frame is drawn by Ream (`MainWindow.xaml`: `WindowStyle=None`, `AllowsTransparency`,
`WindowChrome`, own title bar and min/max/close). The window itself is transparent and each region
(`TitleBar`, `TabRow`, `RibbonPanel`, `CanvasArea`) paints its own brush once, so translucent brushes
never stack; tests read the rendered pixels (`Ui.Render`/`Ui.PixelAt`) to check color and alpha.
Blur behind is separate (`canvasBlur`, and never asked for at 0% - a blurred desktop is something of Ream): `WindowBackdrop` (behind `IWindowBackdrop`) asks Windows for it via
`SetWindowCompositionAttribute`; `BlurPlan` is unit-tested, the real effect is not visible off-screen.
Help/About are still ordinary native windows.
Ribbon: the tab row is File | Home | View (`MainWindow.SelectTab`, `RibbonTab`); each tab
swaps the panel below it: `FileRibbonView` has two groups for the ream itself - **Ream** (New, a full-size tile;
Auto-save right beside it, a tile again but a visibly smaller one than New's - "maybe it should be smaller?"; then
Open/Save/Save As stacked as a small icon+text row past that, reached far less often than New) and **Add** (New Note
/ New Workspace, moved here from the View tab's Window group, plus Clear, moved in from its own group - still a
"change what exists" action, just a subtractive one - all three now their own stacked column too, the same small
icon+text row style as Open/Save/Save As, rather than three full-size tiles of their own; all bound straight to
`AppViewModel.NewNoteCommand`/`NewWorkspaceCommand`/`ClearReamCommand`, no event-relay needed since this tab's
DataContext already is the `AppViewModel`) - then **Help** (Help/About, raised as events). There is no Recent group
any more (nor a workspace list - workspaces are switched by keys and the wheel) - what it was for wasn't obvious at
a glance, so it's gone; `AppConfig.RecentReams` and `AppViewModel.OpenRecentReamCommand` underneath it are untouched,
just not reachable from this ribbon right now.
`RibbonView` (Home, the
editor controls) and `ViewRibbonView` (DataContext is the `SettingsViewModel`), in this order, left to right: **Theme**,
**Zoom**, **View**, **Window**, **Show** - not Word's own order, and not alphabetical; just the arrangement asked
for (moved once already, from an initial View/Window/Show/Zoom/Theme). Word's Page Movement group (Ream's row is always horizontal, its workspace stack always vertical) and, inside
View, Print Layout/Web Layout (no page to offer a page-layout mode for) are dropped outright, and Word's page-view
controls (One Page, Multiple Pages, Page Width) go the same way except One Page, which survives as a real command
moved into Window. Every control on this tab is real - none of the Home tab's "placed but disabled,
`IsEnabled="False"`, tooltip 'Not available yet'" convention survives here. Every group's own content is explicitly
`HorizontalAlignment="Left"` (both the group's own StackPanel and each control in it, via the `StackedRow`/
`StackedLeft` styles) - relying on a vertical StackPanel's default child stretch plus left-aligned content looked
right often enough to ship once, but not reliably. Every group's content wrapper is also a fixed `Height="60"` with
`VerticalAlignment="Bottom"`, so the `RibbonGroupLabel` caption underneath starts at the exact same Y for every
group regardless of how tall that group's own content naturally is - `MinHeight` plus `VerticalAlignment="Center"`
(the previous approach) centers each group's content independently within its own band, and groups with shorter
content (Show's three CheckBoxes, especially) would end up with their captions sitting at a visibly different height
than groups whose content fills the full 60px. **View** is a horizontal row of three ToggleButtons (`RibbonToggleTile`,
the same tile One Page below uses - clicking one "enables it similar to the One Page tile"), mutually exclusive: only
one of Read Mode / Draft / Outline can be on for a note at a time, since they're alternate ways of viewing it, not
independent flags. **Read Mode** (`ReadModeButton`, a hand-drawn open-book `Path` - no icon-font glyph reads as
"book" reliably enough to risk guessing one; Word itself called this "Full Screen Reading" before renaming it) sets
both `NoteViewModel.IsFullscreen` and `IsReadOnly` together, and leaves Draft/Outline; clearing `IsFullscreen` by any
path always clears `IsReadOnly` too (`NoteViewModel.OnIsFullscreenChanged`). **Draft** (`DraftButton`) sets
`HideImages`, which `NoteColumnView.ApplyImageVisibility` turns into `Visibility.Collapsed` on every pasted image in
the live document (nothing is removed - turning it off restores them; applied on load and right after a paste too,
so it can't be bypassed by timing), and leaves Read Mode/Outline. **Outline** (`OutlineButton`) sets `IsOutlineView`;
`NoteColumnView.ApplyOutlineView` stashes the real `FlowDocument` and swaps in a generated one - one read-only
paragraph per heading (`NoteStyles.HeadingLevelOf`), indented and styled to match, a "No headings in this note"
placeholder if there are none - and clicking a line (`OnEditorPreviewMouseDown`, mapped back via `_outlineMap`) turns
Outline off and puts the caret at the real paragraph it summarizes; entering it leaves Read Mode/Draft. Both
`IsReadOnly` and `IsOutlineView` feed `NoteViewModel.EffectiveReadOnly`, which is what `Editor.IsReadOnly` actually
binds to - neither is persisted (`NoteSnapshot`/`SnapshotMapper`), unlike `IsFullscreen` which is. The mutual
exclusion lives in `AppViewModel.ToggleReadMode`/`ToggleDraftView`/`ToggleOutlineView` themselves: entering one
clears the other two's flags directly (not just its own `IsChecked` binding - a raw property set from anywhere hits
the same note fields, so there's no path around it). None of these three ToggleButtons bind to the note directly -
`ViewRibbonView`'s DataContext is `SettingsViewModel`, which has no path to "the focused note's own flag" - so
`SettingsViewModel` carries a passthrough property for each (`FocusedNoteIsReadOnly`, `FocusedNoteHideImages`,
`FocusedNoteIsOutlineView`, plus `FocusedNoteShowRuler` for Show's Ruler and `OnePageMode` for Window's One Page): the
getter reads straight off `_app.CurrentWorkspace.FocusedNote`, the setter just asks the matching `AppViewModel`
command to toggle, and `SettingsViewModel.TrackFocus` (subscribed to the app's `CurrentIndex` and the current
workspace's `FocusedIndex`) re-subscribes to whichever note is focused and raises the right property when one of its
four flags changes - so each control's own `IsChecked` always converges to the truth regardless of which end changed
it (including the other two flipping itself off from underneath it). **Window** is Switch Workspaces stacked above
Switch Notes, to the left of One Page, a big `ToggleButton` (`RibbonToggleTile`, `Themes/Controls.xaml` - the same
tile `RibbonTile` draws, but staying highlighted while checked via an `IsChecked` trigger, not just on hover/press)
the same size as the Theme tile. Word's own New Window, Arrange All, Split, View Side by Side, Synchronous Scrolling
and Reset Window Position are all dropped outright - Ream has no multi-window concept for any of them (New Note /
New Workspace, which used to stand in for New Window / Arrange All here, moved to File's own Add group; Synchronous
Scrolling never pulled its weight enough to keep once it had a real implementation to weigh). **One Page**
(`OnePageButton`, bound to `OnePageMode`) hides every note but the focused one - unlike Read Mode, the note that
stays doesn't grow to fill the row, it keeps its own `WidthFraction`-based size; the others just disappear
(`NoteViewModel.IsHiddenByOnePage`, a plain `Visibility.Collapsed` trigger in `WorkspaceView.xaml`'s
`ItemContainerStyle` - `NoteRowPanel`'s own row-position math in `RowLayout.cs` isn't touched, so a hidden note still
reserves its slot in the row rather than the row compacting around it). `AppViewModel.ApplyOnePageVisibility` sets
every note in a workspace's `IsHiddenByOnePage` to "OnePageMode is on and this isn't the focused one", called from
`OnOnePageModeChanged`, from `OnWorkspaceChanged` (focus moving within the current workspace), and from
`OnCurrentIndexChanged` (a workspace switch, which also explicitly un-hides everything in the workspace left behind,
since it isn't on screen either way - arriving back at it later without One Page being asked for again shouldn't
find it still narrowed to one note). **Switch Notes** (renamed from Word's Switch Windows - Ream has notes, not windows)
and **Switch Workspaces** are both themed drop-downs built entirely in code-behind (`ViewRibbonView.OnSwitchNotesClick`/
`OnSwitchWorkspacesClick`, `BuildPopupShell`, `BuildRow`) - Notes lists the current workspace's own notes, Workspaces
every named-or-occupied one (`SelectWorkspaceCommand`); both read `AppViewModel` through a small
`internal SettingsViewModel.App` accessor, since only it has the workspaces/notes to list. Each is a plain `Popup`,
not a `ContextMenu`: a `Border`/`StackPanel`/`Border`-per-row shell built by hand and styled to match the
`ContextMenu`/`MenuItem` template in `Controls.xaml` (rounded card, `CardBrush`/`ControlBorderBrush`, a checkmark
glyph before whichever row is current, `ControlHoverBrush` on `MouseEnter`). Getting "click either button again while
its own menu is open closes it, instead of tearing it down and reopening an identical one" right took four attempts
before this one, and the first three shared a design mistake worth remembering: each tried to ask a `ContextMenu`
itself whether *this* click was the one that had just closed it (checking `IsOpen` from inside the button's own
`Click` handler, then watching `IsOpen` change via a `DependencyPropertyDescriptor`, then setting
`ContextMenu.StaysOpen="True"` and watching only this file's own code close it) - all three passed a synthetic test
built around whatever signal they watched, and all three still failed in the live app. `StaysOpen` documents itself as
governing only the click-outside-dismisses-it case; `ContextMenu`/`MenuBase` carries its own keyboard-navigation
focus-scope machinery underneath that (for arrow-key item navigation, access keys, submenus) which - near as this
could be pinned down without being able to drive the real desktop to confirm it directly, per this file's own
"never drive the live app" rule below - closes the menu on losing keyboard focus independently of `StaysOpen`, and
clicking the anchor button again shifts focus to it, triggering that regardless of which signal was being watched.
So the fourth attempt, the one that stuck, stopped using `ContextMenu`/`MenuItem` at all: a plain `Popup` is a
framework primitive with none of that menu-specific focus baggage - it closes only when told to - so closing is
entirely this file's own job now: `CloseIfOpen` (the same button clicked again - since nothing but this code can ever
close one of these, its tracked field's own `IsOpen` is unconditionally accurate, with no race to lose) and
`OnWindowPreviewMouseDown` (anywhere else clicked - reimplementing "click elsewhere closes it" is the cost of a plain
`Popup` not doing it automatically the way `ContextMenu` did; a click actually landing inside an open popup never
reaches this handler, since its content is hosted in its own top-level window, so the only thing the handler has to
rule out is a click on the button that opened it, which its own `Click` handler already closes), plus each row
closing its own popup before acting, since picking an item no longer closes the menu for free either. Both menus are
watched (`ViewRibbonView.MenuOpenChanged`/`IsMenuOpen`, from `BuildPopupShell`'s own `Opened`/`Closed` handlers, the
same shape `RibbonView`'s own font/size drop-downs already used for their menus) and wired into
`MainWindow.UpdateMenuOpenState` (`Ribbon.IsMenuOpen || ViewRibbon.IsMenuOpen`) so the ribbon stays up for as long as
either is open, regardless of the pin - previously only `RibbonView`'s own menus were watched, so an unpinned ribbon
could vanish out from under an open View-tab menu the moment the pointer left it.
**Show** is three CheckBoxes: **Ruler** (`RulerCheckBox`, the `FocusedNoteShowRuler` passthrough) shows two rulers
outside the focused note's own card, not inside it - `NoteColumnView`'s root is a 3-column, 2-row `Grid` with the
card itself in the middle cell; `HorizontalRulerHost` sits above it (row 0, the card's own column) and
`VerticalRulerHost` sits in column 0 or column 2 of the card's row, whichever `UpdateVerticalRulerSide` decides is
the side with no neighbouring note (the first note in the row takes the left, the last takes the right, a middle
note - or the only note - defaults left; recomputed on `Subscribe`/`OnDataContextChanged` and whenever the
workspace's own `Notes.CollectionChanged` fires, since another note being added, removed or reordered anywhere in
the row can flip it). `ResizeHandle` and `DraftOutline` still key off the outer grid's own bounds, not just the
card's, so resizing and the draft outline keep working regardless of a ruler. Only the horizontal ruler has a
draggable indent `Thumb` (`IndentMarker`, sets `Paragraph.Margin.Left` for whatever `SelectionParagraphs.Of(Editor)`
returns, synced via `SyncIndentMarker` whenever the selection moves or Ruler turns on) - the vertical one is ticks
only, the same as Word's own side ruler. **Gridlines** (`GridlinesCheckBox`, `SettingsViewModel.GridlinesOn`,
session-only) draws on each note itself, not the canvas behind them: a `DrawingBrush`-tiled `Rectangle` inside
`NoteColumnView`'s own card - `Grid.RowSpan="2"` and declared before the header `DockPanel`, so it sits behind the
heading too, not just the editor beneath it - bound to `{DynamicResource GridlinesVisibility}` -
`SettingsViewModel.GridlinesVisibilityKey`, a `Visibility` (not a `bool`, so no converter is needed) published to
`Application.Resources` on every change, the same cross-DataContext trick `NoteZoomScale` uses, and once explicitly
in the constructor too (`OnGridlinesOnChanged(GridlinesOn)`) - a plain `[ObservableProperty]`'s own default value
never runs its `OnChanged`, and a `DynamicResource` nobody has ever published falls back to the property's own
default, `Visibility.Visible`, which would draw a grid on every note before anyone asked for one. **Navigation Pane**
(`NavigationPaneCheckBox`, `SettingsViewModel.NavigationPaneOpen`, session-only) is unrelated to any of that -
`MainWindow` reacts to it directly (`OnSettingsPropertyChanged`) since it affects its own layout, not a note: it
grows `CanvasArea`'s second column (`NavigationPaneColumn`, 0 <-> 280px) to show `NavigationPaneView`. That view's
DataContext is a `NavigationPaneViewModel` (`MainWindow.NavigationPane`, one instance for the window, not per-note) -
headings (from the focused note's `NoteViewModel.LiveDocument`, the same live `FlowDocument` the editor is showing,
scanned with `NoteStyles.HeadingLevelOf` and re-scanned on `NoteViewModel.ContentChangedProperty`), the current
workspace's notes, every named-or-occupied workspace, and a search (`RunSearch`, plain case-insensitive substring
over title and `NoteContent.ToPlainText(note.Body)`, each note flushed first so live edits are found) scoped to
`NavigationSearchScope.CurrentNote` / `CurrentWorkspace` / `Everywhere`. Clicking a heading raises
`NoteViewModel.CaretMoveRequested` (a paragraph reference `NoteColumnView.OnCaretMoveRequested` lands the caret on,
turning off Outline view first if it was showing); clicking a note or workspace entry reuses
`WorkspaceViewModel.SetFocus`/`AppViewModel.SelectWorkspaceCommand`, the same paths the row and the File ribbon's
workspace list already use; the search-scope `RadioButton`s' own text was unreadable until the app-wide themed
`RadioButton` style existed (below). **Zoom** is three stacked buttons, each its own magnifying-glass glyph from
Segoe Fluent Icons (`ZoomInButton`/`ZoomOutButton`, +-10 a click, clamped 50-200), plus `ZoomResetButton` with a
hand-drawn reset-arrow `Path` (not Zoom's own glyph, so Reset doesn't look like a fourth zoom control) and no
percentage printed on the tile any more - the live number stays in `ZoomResetTooltip`'s tooltip only now. Word's
page-view trio (One Page, Multiple Pages, Page Width) is dropped - One Page moved to Window, Ream has no pages for
the other two. Deliberately no slider on the ribbon itself, matching Word (its live zoom control is in a status bar
Ream doesn't have); `Ctrl+Scroll` also zooms (`MainWindow.OnPreviewMouseWheel`, its own `WheelAccumulator`).
`SettingsViewModel.ZoomPercent` (`AppConfig.Zoom`, 50-200) scales a note's whole editor, not just its font: `ThemeService`
publishes it as `NoteZoomScale` (a boxed double, config.zoom / 100) the same way it publishes theme brushes, and each
`NoteColumnView`'s `RichTextBox` binds a `ScaleTransform` `LayoutTransform` to it with `{DynamicResource NoteZoomScale}` -
a LayoutTransform, not a RenderTransform, so text actually re-wraps at the zoomed size instead of just stretching, and
it is live and app-wide with no other plumbing, exactly like a theme change. **Theme** (`ThemeButton`/`ThemeRequested`,
raised as an event and presented by `MainWindow.OpenThemeModal` the same way Help/About are - opening a window is
MainWindow's job, ownership and the test-interceptable `ShowModal` hook live there) is a single button that opens
`ThemeModal` (`Views/ThemeModal.xaml`, styled like Help/About with `ModalWindowStyle`), which holds what used to be
three separate ribbon groups (Layout, Theme, Opacity) on the exact same `SettingsViewModel` those groups already
edited - moving them didn't change what they bind to, only where they live, and down to two sections now, not three:
**Layout & Opacity** is combined into one, two columns (a `Grid` with two `*` columns and a narrow fixed gutter),
each now two rows deep to match the other: opacity (Canvas then Notes) on the left; Gap then "Center the focused
note" on the right, the checkbox in a `Height="26"` `Grid` (`VerticalAlignment="Center"` inside it) so its own
shorter content still lands centered on Notes's row despite a `CheckBox` naturally being shorter than a `ComboBox`.
Both columns' own label column is the same fixed width (52), so Gap's box lines up under Canvas's, not merely
"somewhere in the right column". **Theme** is a grid, not a long list - `ThemeList`'s `ItemsPanel` is a
`WrapPanel`, each entry a fixed-`Width` `RadioButton` with a wide right `Margin` (the gap between columns) so two fit
per row instead of one down a mostly-empty modal - `Checked` calls `SelectThemeCommand` straight away, no separate
"apply" step. Gap and both opacities are genuinely editable - pick a preset or type any in-range number - which
needed `Themes/Controls.xaml`'s `ComboBox` style to grow a `PART_EditableTextBox` part (`IsEditable="True"` did
nothing before this; WPF's own `ComboBox` wires that named part up automatically once the template has one, no extra
code needed) - every other `ComboBox` in the app keeps `IsEditable` unset and is unaffected. Each box binds `Text`
(not `SelectedItem`, and deliberately a bare number, no "%"/"px" suffix baked in - appending one would break parsing
it back to a number) `TwoWay` with `UpdateSourceTrigger=LostFocus` (so typing doesn't fight the box mid-keystroke;
Enter commits early via `GetBindingExpression(...).UpdateSource()`) straight to `GapPx`/`OpacityPercent`/
`NoteOpacityPercent`, which already clamp out-of-range values themselves; each box has its own plain "%" or "px"
`TextBlock` beside it instead, so the unit is still visible without being part of the editable text. `Themes/Controls.xaml`
also grew an app-wide themed `RadioButton` style (the same shape as its `CheckBox` - a circle with an accent dot
instead of a square with an accent check) once two different radio-button usages (this modal's theme picker, the
Navigation Pane's search scope) both turned out unreadable without one - WPF's own default RadioButton assumes a
light backdrop, so on Ream's own dark theme the text just disappeared. The View tab's own CheckBoxes (Show only now -
View went back to ToggleButtons, Window's none, Layout's `CenterFocusedCheckBox` lives in the modal) all pick up the
app-wide themed `CheckBox` style too, via `BasedOn="{StaticResource {x:Type CheckBox}}"` on the local `StackedLeft`
style that only adds the left-alignment - setting `Style` to something that ISN'T based on the app-wide one would
replace it outright and fall back to WPF's own unstyled checkbox, the exact bug the RadioButtons had. Home is laid out like Word's Home tab (flat buttons, icon rows, a label centered under each group; there is no
dialog-launcher corner and no Add-ins group - Ream doesn't have either): Clipboard, Font, Paragraph, Styles (a framed, horizontally
scrollable gallery: Normal, Heading 1-4, Title, Subtitle, Quote, with working Previous/More arrows and an "All styles" menu -
`RibbonView.Styles`, `ApplyStyle`), Editing (Find/Replace open a modeless `FindReplaceWindow` bound to `AppViewModel.FindCommand`/
`ReplaceCommand`, which `MainWindow.OnFindRequested` opens or re-shows against `RibbonView.CurrentEditor`; Select is a small menu -
Select All / Select Paragraph), then Ream's own Size group (the three reset commands, stacked). A few Word controls Ream still has
no behaviour for (Format Painter, a true multilevel list, formatting marks / ¶, Text Effects) are drawn but disabled
(`RibbonWordLayoutTests.Placed` lists them; enabling one means adding its handler and removing `IsEnabled="False"`); most of Font and
Paragraph are otherwise live, including subscript/superscript, clear formatting, change case, line spacing, paragraph shading and
borders, and sort - these act on `Ream.App/Editing/SelectionParagraphs` (the paragraphs touched by the selection) and, for
Find/Replace, `Ream.App/Editing/DocumentSearch` (a flattened-text scan so a query can straddle two differently-formatted runs).
Line height, paragraph spacing, borders and subscript/superscript are new `.reamnote` attributes (`src/Ream.Persistence/CLAUDE.md`);
shading rides the existing run/paragraph `bg` attribute for free. A border's color is a fixed gray owned by the persistence layer, not
a theme resource, so it looks the same before and after a reload regardless of the live theme (`RibbonView` uses the same fixed color
when applying one live, for the same reason). Home's own Size group sits outside `RibbonView.Bar` so it works with no editor focused. When the window is too narrow
the panel scrolls sideways with no scrollbar: chevron buttons (`RibbonScrollLeft/Right`) appear at the edge with more to see, and the wheel scrolls. There is no File menu or
settings popup any more. `ribbon.autoHide` (default true): the tab row stays, the panel (always grid row 2) grows from height 0 when
summoned and back to 0 when put away, so it pushes the notes down rather than covering them (`Core/Layout/RibbonVisibility` is the
pure state: pointer, open menu, pin; the window adds a 400 ms hide delay). Clicking a tab holds it open (`Engaged`) until a click
elsewhere or Escape (`MainWindow.DismissRibbon`); the pin button (`PinButton`, bottom right of the panel) keeps it open until
clicked again; like Word's it is a pin while unpinned and a caret up once pinned. autoHide off leaves the panel up always (and hides the pin).
`ribbon.pinned` (default false) remembers whether it was on, restored at launch and after a live reload
(`MainWindow.ApplyRibbonMode` calls `RibbonVisibility.RestorePinned`, gated by `AutoHide` the same way `TogglePin`
is - a restored pin means as little as a clicked one with auto-hide off). Unlike `autoHide` (still a hand-edit-
config.json-only setting - nothing in the app writes it), clicking the pin button does write `pinned`: `MainWindow.
OnPinClick` calls `SettingsViewModel.SetRibbonPinned`, the same "update `AppViewModel.Config` at once, save shortly
after through `AppConfigStore`" shape every other live setting here already uses, even though the pin's own on/off
*state* (`RibbonVisibility`) lives in `MainWindow`, not `SettingsViewModel` - only the persisted *value* goes through it.
Watch `ComboBox.IsDropDownOpen` itself, not DropDownOpened/Closed (Closed can arrive before the property flips).
`SettingsViewModel` applies theme and both opacities live and saves them (debounced) via
`AppConfigStore.Update`, which patches only the given keys and refuses to rewrite a file with
comments; `ConfigReloader` ignores the file-change echo of that save (`IsOurOwnLastWrite`).

Live reload: `ConfigReloader` re-reads config.json (via `ConfigWatcher`, debounced) and replaces
`AppViewModel.Config`; panels/bindings and the window's key bindings follow. It uses the
non-destructive `AppConfigStore.TryLoad`: an unusable file is reported in the toolbar
(`ConfigError`) and the running settings stay - never move or rewrite a file the user is
mid-edit.

Lazy loading: `NoteRowPanel` marks each column `IsNear` (within a viewport of the screen, in a
workspace within ~1.5 screens); `NoteColumnView` (an `INearAware`) only parses its note in
`EnsureLoaded()` once near, focused, or pasted into, and never unloads. A view that is
constructed but never shown must be loaded explicitly (`EnsureLoaded()`) in tests.

Crash recovery: on load, leftover `*.tmp` files are promoted if complete and their real file is
missing, otherwise moved to the ream's `.recovered/<stamp>/` folder (never deleted).

Packaging: `build/publish.ps1` makes a portable single-file build + zip under `artifacts/`
(gitignored); `-FrameworkDependent` for the small one; `-Version x.y.z` stamps a version. It is not an installer.
Releases: a push to `main` runs only the `release` job in `.github/workflows/tests.yml` (the tests run on every branch push, and a ruleset requires them to pass on a PR's source branch, so main does not re-run them): GitVersion (`GitVersion.yml`)
works out a plain major.minor.patch (the `MajorMinorPatch` variable only - never `FullSemVer`, which grows a `-N` suffix), the
framework-dependent zip is published with that version, and a GitHub release `v<version>` is created (which also tags it). Each merge to
main is a patch bump; `+semver: minor` / `+semver: major` in a commit message bumps more. A commit that already has a release is skipped.

Widths: a column's width is a plain fraction of the row (`NoteViewModel.WidthFraction`,
clamped to 0.15-1.0). Presets (1/3, 1/2, 2/3, full) are only labels/cycle stops
(`WidthPresets`); Alt+R goes to the next preset wider than the current width. Dragging a
column's right edge (`NoteColumnView.ResizeHandle`) sets the fraction live via
`RowLayout.FractionForWidth`; `NoteRowPanel.IsResizing` makes the panel follow the pointer
instead of animating. `layout.json` stores `widthFraction` (older files' `"width": "half"` still
load) and an optional `customTitle`. Workspaces have no chips any more: the title bar says just "Ream"
(room for a ream name later) and the workspace's name is a label in the bottom-right corner
(`AppViewModel.WorkspaceLabel`; unnamed = "Workspace N", the empty edges = "New workspace"); double-click it or press
Shift+F2 to rename in place, and the File ribbon lists them for the mouse. The first and last workspace are
unnamed empty edges. Named workspaces are never pruned; unnamed empty ones between the edges are.
Switching workspace (keys, Alt+wheel, menu) lands on the first note when `layout.focusFirstNoteOnSwitch`
(default true); moving a note keeps it focused. `NoteRowPanel.IsCurrentWorkspace` makes a row snap rather
than scroll while its workspace is off-screen. Note width is not shown on the note: `NoteViewModel.ShowSizeToast`
flashes "NN%" in the header (accent color) after Alt+=/-/R, resets (Alt+Shift+R note, Ctrl+Alt+R workspace, Ctrl+Alt+Shift+R all; Alt+0-9 are reserved for jumping to workspaces) and edge drags.
Default `layout.gapPx` is 28. Draft notes (Alt+Left/Right past the end of a row, Alt+N): `NoteViewModel.IsDraft`, never stacked,
not saved while blank, dropped when focus moves on (`WorkspaceViewModel.DiscardBlankDrafts`);
the scroll wheels never create them.

Editor: each `NoteColumnView` hosts a `RichTextBox`; `NoteViewModel` owns the note's live
`FlowDocument` and only writes it into `Body` (the saved XML) in `FlushDocument()`, which
`SnapshotMapper.ToSnapshot` calls before every save. `RibbonView` (the Home tab of the Word-style
ribbon; the File button, tab row and menus live in `MainWindow.xaml`) acts on whichever
editor last had keyboard focus. Focus follows the app's note focus via
`AppViewModel.RequestEditorFocus` -> `NoteViewModel.EditorFocusRequested`.

Reams (files): a *ream* is a `Foo.ream` file plus a data folder (formats: `src/Ream.Persistence/CLAUDE.md`). One is open at a
time, in one window; opening another swaps the content of the existing `AppViewModel` in place (`AppViewModel.LoadReam`,
`SnapshotMapper.LoadInto`), so the window, settings and key bindings stay. The pieces (all `Ream.App/Services`):
- `ReamLauncher` picks the ream at launch: `config.lastReam`; else an old `ReemDocuments` folder converted in place
  (`LegacyConverter`, backup copy first) or the `.ream` already in it; else a new ream (`Documents\Ream\My Ream.ream`, with the
  tutorial, or empty if `tutorialOnNew` is false). A last ream that is missing or unreadable is explained, never fatal.
- `ReamSession` = one open ream: its `DocumentRepository` plus a `PersistenceCoordinator`, and it tells the view model the ream's
  name, path and unsaved state (`WindowTitle` is `Ream - Foo`, with ` *` while unsaved and auto-save is off).
- `PersistenceCoordinator` watches the view models; after a 300 ms quiet period it snapshots on the UI thread (`SnapshotMapper`) and
  compares a content fingerprint (`SnapshotFingerprint`) with the last saved one: that is `HasUnsavedChanges`. Moving focus, switching
  workspace, the empty edge workspaces and blank drafts are not edits. With auto-save on the snapshot is written on a background
  queue (`IDocumentRepository.Save` reconciles disk: creates/moves/trashes note files, rewrites only what changed); with it off nothing
  is written until `Save()`. `Flush()` (app exit) writes only when auto-save is on.
- `ReamManager` is New / Open / Save / Save As / Clear / auto-save switch / "may I leave this ream?" (`ConfirmLeave`, used by New, Open
  and closing the window: with auto-save off and unsaved changes it asks Save / Don't save / Cancel). Every question goes through
  `IFileDialogs` and `IUserPrompts` (real ones are Ream's own themed windows; tests use `FakeDialogs` / `FakePrompts`), and
  `AppViewModel` reaches it through `IReamFiles` (commands `NewReam`, `OpenReam`, `SaveReam`, `SaveReamAs`, `ClearReam`,
  `ToggleAutoSave`; key ids `newReam`, `openReam`, `save`, `saveAs`, `clearReam`). New and Save As never overwrite (`ReamPaths.IsOccupied`);
  Save As (`ReamCopier`) copies the ream, brings the copy up to date with memory, and switches to it; Clear (Alt+Shift+Q) asks first and
  swaps in an empty ream - saving that trashes what disappeared, so nothing is deleted.
- Help window (`HelpWindow`, `HelpNavigator`): the switch-workspace keys (from config, Alt+Down/Up by default) move an accent border through the
  sections, centered, and past the last one onto the Close button. Never `Keyboard.ClearFocus()` there: with nothing focused the
  navigation keys stop reaching the window (keyboard focus goes to the Close button or the content root). Dialogs (Help, About) use
  `Style="{StaticResource ModalWindowStyle}"` (Themes/Controls.xaml): the same drawn title bar as the main window
  (`CaptionButtonStyle` is shared too) instead of a native caption; new dialogs should use it. Questions and errors are `PromptWindow` (behind `IUserPrompts`). Open / New / Save As are the real native Windows common dialog (`NativeFileDialogs`, behind `IFileDialogs`, wrapping `Microsoft.Win32.OpenFileDialog`/`SaveFileDialog`) rather than a window of Ream's own: an earlier themed rebuild (breadcrumbs, a places sidebar, its own icons) got a good way there but there is no supported way to paint Ream's palette onto the OS's own dialog chrome (it's rendered by shell32/comdlg32 via Windows' visual-styles engine, not something an app can inject brushes into - the only lever is an undocumented, version-fragile DWM/uxtheme light/dark toggle, and even that only gives Windows' own gray, not Ream's actual theme), so it isn't worth maintaining an approximation of something the OS already does exactly right. The save dialogs set `OverwritePrompt = false`: Ream never overwrites regardless (`ReamManager.FreePath` rejects an occupied name with its own message), so Windows' own "replace it?" would just be a confusing extra step. Tests replace the dialog's show delegate so nothing real ever opens. Only the startup-failure MessageBox in `App.xaml.cs` was already native and stays that way.
- `TutorialReam` (Ream.Core) builds the tutorial (3 workspaces, 8 notes) from the live keybindings and settings, or an empty ream.
The two always-empty edge workspaces (above the first, below the last) are never stored: a workspace is persisted only if it is
named or has a saveable note (blank drafts are not); a ream with no workspaces is a valid, cleared ream (not a first run).

## Storage

- Reams live wherever the user puts them (New / Save As pick the place). With no last ream, a fresh one is made at
  `%USERPROFILE%\Documents\Ream\My Ream.ream`. `config.json` remembers `lastReam`; `documentsRoot` is legacy, read once to find an
  old folder to convert.
- `%AppData%\Ream\config.json` — gaps, centered focus, animations, keybindings, autoSave, tutorialOnNew, lastReam,
  the ribbon pin. Kept separate from the reams. Unreadable JSON is set aside as `*.corrupt-<timestamp>`
  and defaults are used; bad/duplicate keybindings fall back to defaults.
- `--home <dir>` on the command line puts config and reams (`<dir>\Reams`) under one folder. Use it
  (with a temp dir) when running the app for testing so real data is never touched.
- File formats (`.ream`, `.reamlayout`, `.reamnote`, data folder, trash, atomic writes, recovery, conversion) are described in
  `src/Ream.Persistence/CLAUDE.md`, which loads when you work there.

## Conventions and gotchas

- Plain mouse wheel is never intercepted at the shell level — it scrolls the
  note under the cursor. Alt+wheel switches workspaces, Shift+wheel pans the row.
- Keybindings are read from config: one gesture per action, defaults in
  `AppConfig.DefaultKeybindings`. Every action also needs a description in `ActionCatalog` (a test
  enforces it) so the Help window lists it. F11 = app fullscreen (`FullscreenController` behind
  `IWindowFrame`), Alt+F11 = the focused note's fullscreen.
- `RichTextBox.Document` is not a DependencyProperty — the editor owns its
  document in code-behind; don't try to bind it.
- `XamlWriter`/`XamlReader` don't round-trip images; `NoteDocumentSerializer`
  handles images as sibling asset files with an `asset://` placeholder.
- Soft scope: no import/export, sync, or auth in v1.
- The WPF SDK removes `System.IO` from implicit usings; `Ream.Persistence` and
  `Ream.Tests` add it explicitly via `<Using Include="System.IO" />`.
- `JsonDefaults` uses relaxed escaping so hand-edited files read `Alt+Right`, not
  `Alt+Right`. Keep it that way for anything a user might edit.
- `dotnet test` may leave `Ream.App/bin` stale; run `dotnet build Ream.sln` before
  launching the app to test changes.
- A `FlowDocument` does NOT inherit font/color from its `RichTextBox`; `NoteColumnView` copies
  the editor's defaults onto the document. `TextRange.ApplyPropertyValue` rejects
  `DependencyProperty.UnsetValue` (throws) - apply explicit defaults or null instead.
- A `TextPointer` scan reports an `InlineUIContainer` at both its start and end; dedupe.
- `TextRange` can't clear a property (`UnsetValue` throws). To return text to "automatic" color,
  apply a marker brush (which splits runs at the selection edge) then `ClearValue` on the
  elements holding the marker (`RibbonView.ClearAutomaticColor`). Never copy today's theme
  brush into the document as a local value.
- Don't name a field `_contentLoaded` in a XAML code-behind: the XAML compiler generates one.
- A UserControl's own implicit `Button`/`ComboBox` style replaces the app-wide themed one unless it
  has `BasedOn="{StaticResource {x:Type Button}}"` (see `RibbonView.xaml`).
- Test in-process, never on the user's live desktop. Do NOT drive it with SendKeys/mouse events:
  other input lands in the same window and keys can reach the wrong app. If you must run the
  real app, use `--home <temp dir>`, capture with `PrintWindow` only, and never delete a path
  built from a variable without validating it (PowerShell's `$Home` is read-only and silently
  resolves to the user profile). Test-harness details (`Ui.Run`, `Ream.SkipStartup`, timing) are in
  `src/Ream.Tests/CLAUDE.md`, which loads when you work there.
