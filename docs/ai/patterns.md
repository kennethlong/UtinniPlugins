# Plugin patterns

Conventions distilled from [The Jawa Toolbox](jawa-toolbox.md). Adopting
them gives you a clean, idiomatic Utinni plugin.

## 1. Composition over inheritance

JT has zero deep class hierarchies. Every feature area is a top-level
`<Feature>Impl` class plus one or more `SubPanel`s. The plugin class
(`TheJawaToolboxPlugin`) is **a composition root** — it instantiates each
`Impl`, hands them the shared settings/hotkey manager, and exposes them to
`FormMain` via `GetSubPanels()` / `GetForms()` / `GetStandalonePanels()`.

```csharp
public class MyPlugin : IEditorPlugin
{
    private readonly FeatureAImpl a;
    private readonly FeatureBImpl b;
    private readonly FeatureCImpl c;

    public MyPlugin()
    {
        // shared state
        var ini = new UtINI("settings.ini");
        var hk  = new HotkeyManager(false);

        // wire features
        a = new FeatureAImpl(ini, hk);
        b = new FeatureBImpl(ini, hk, a);   // B uses A
        c = new FeatureCImpl(ini, hk);

        hk.CreateSettings();
        ini.Load();
    }

    // ...
}
```

Why this matters: features are independently testable, easy to remove, and
their dependencies are explicit (you see exactly which features touch
which).

## 2. One `*Impl` per feature; panels are thin

A `SubPanel` should be presentation only — controls and event handlers that
forward to `Impl` methods. All callback registration, hotkey setup, undo
emission, and settings I/O happens in `Impl`.

```csharp
// SnapshotPanel.cs (thin)
public class SnapshotPanel : SubPanel
{
    private readonly WorldSnapshotImpl impl;
    public SnapshotPanel(WorldSnapshotImpl impl) : base("Snapshot", true)
    {
        this.impl = impl;
        // ... build controls
        btnSave.Click  += (s,e) => impl.Save();
        btnAdd.Click   += (s,e) => impl.AddNode(txtFile.Text);
        gizmoTranslate.Click += (s,e) => impl.SetOperationModeToTranslate();
    }
}
```

This means **swapping the UI** (e.g. adding a second SubPanel that exposes
the same features differently) doesn't touch the business logic at all.

## 3. Marshal everything

The single most common bug in new plugins is calling a game API on the UI
thread, or touching WinForms from a callback. The rule is:

| You're on the…             | You want to call a…       | Do this                                                       |
| -------------------------- | ------------------------- | ------------------------------------------------------------- |
| UI thread (button, hotkey) | game API                  | `GameCallbacks.AddMainLoopCall(() => Game.Foo())` or `GroundSceneCallbacks.AddUpdateLoopCall(...)` |
| game thread (callback)     | WinForms control          | `myControl.BeginInvoke((MethodInvoker)(() => myControl.Text = ...))` |
| Background `Task`          | game API                  | enqueue a callback, await it via `TaskCompletionSource`        |
| Background `Task`          | WinForms control          | `Invoke` to UI                                                 |

JT does this consistently — every `Impl` method that touches the game wraps
the call in a callback enqueue. Adopt the same style and a whole category of
bugs disappears.

## 4. Hotkeys with scope flags + dynamic enable

Define hotkeys once in the `Impl` constructor with `Name = "MyPlugin.X"`
prefixes. Use the scope flags:

- `OnGameFocusOnly` — most editor shortcuts should set this true (otherwise
  they fire while the user is typing in a textbox in the side panel).
- `OverrideGameInput` — set true for hotkeys that should not also feed
  through to SWG's input map (e.g. saving with `Ctrl+S` should not send `S`
  to the chat window). It's not frame-perfect — for hard guarantees, call
  `Client.SuspendInput()` / `ResumeInput()` directly in your handler.

For context-sensitive shortcuts, toggle `Enabled` from a state callback:

```csharp
private void OnGizmoEnabled()  =>
    hotkeys.Hotkeys["MyPlugin.GizmoTranslate"].Enabled = true;
private void OnGizmoDisabled() =>
    hotkeys.Hotkeys["MyPlugin.GizmoTranslate"].Enabled = false;
```

## 5. Undo via events, never via the stack

Never touch `UndoRedoManager` directly from a plugin. Always raise
`IEditorPlugin.AddUndoCommand`:

```csharp
owner.AddUndoCommand?.Invoke(owner,
    new AddUndoCommandEventArgs(new MyCommand(before, after)));
```

For continuous edits (gizmo drag, slider drag) capture `before` on the
edit-start callback (`OnGizmoEnabled`, `Slider.MouseDown`), capture `after`
on the edit-end callback (`OnGizmoDisabled`, `Slider.MouseUp`), and push
**once** with the pair.

For multi-object batched edits, write one `IUndoCommand` that holds a list
of (object, before, after) tuples — see the `Commands/WorldSnapshotCommands`
built-ins as the contract.

## 6. Settings as composition root

One `UtINI` per plugin, passed to every feature. Each feature owns its own
INI section name and seeds its defaults in `CreateSettings()`:

```csharp
public class FeatureAImpl
{
    public FeatureAImpl(UtINI ini, HotkeyManager hk)
    {
        ini.AddSetting("FeatureA", "defaultThing", "naboo",  VtString);
        ini.AddSetting("FeatureA", "autoStart",    "false",  VtBool);
        // ...
    }
}
```

`AddSetting` is idempotent — re-adding doesn't clobber an existing value.
So features can call it from every constructor and the user's edits in
`settings.ini` are preserved.

## 7. Async polling for live UI

When you want a label to update every frame (player position, time-of-day,
free-cam speed), don't poll from a WinForms timer — you'll either poll too
slow or too fast. Instead:

```csharp
public FeatureImpl()
{
    Task.Run(UpdateView);
}

private async Task UpdateView()
{
    while (!ShouldStop)
    {
        await Task.Delay(50);   // 20 Hz

        var tcs = new TaskCompletionSource<float>();
        GroundSceneCallbacks.AddUpdateLoopCall(() =>
            tcs.SetResult(Terrain.Get().GetTimeOfDay()));
        var tod = await tcs.Task;

        OnTodUpdated?.Invoke(tod);   // panel subscribes, marshals to UI
    }
}
```

Or simpler — re-enqueue an update-loop call each frame:

```csharp
void OnUpdate()
{
    var t = Terrain.Get().GetTimeOfDay();
    panel.BeginInvoke((MethodInvoker)(() => panel.UpdateTod(t)));
    GroundSceneCallbacks.AddUpdateLoopCall(OnUpdate);
}
GroundSceneCallbacks.AddUpdateLoopCall(OnUpdate);
```

Pick whichever feels cleaner; the JT mostly uses the `Task.Delay` pattern
for 10–20 Hz UI refresh.

## 8. Drag-drop as asset placement

The Object Browser's drag-drop pattern is general-purpose. Adapt it for any
"drag from a listbox / treeview, drop into the live game world":

```mermaid
sequenceDiagram
  participant U as User
  participant L as ListBox
  participant DD as GameDragDropEventHandlers
  participant W as Game world
  participant S as Snapshot impl

  U->>L: mousedown on item
  L->>L: capture filename
  U->>L: mousemove (with LMB)
  L->>L: DoDragDrop(filename)

  U->>W: drag over PanelGame
  W->>DD: OnDragEnter fires
  DD->>W: create temporary preview Object, addObjectNotifications
  U->>W: drag-move
  DD->>W: collide cursor → world point; reposition preview
  U->>W: drop
  DD->>S: snapshotImpl.AddNodeAt(previewPosition, filename)
  DD->>W: destroy preview object
```

The trick is **showing live feedback** during drag (the preview object
follows the cursor) so the user knows what they're placing and where.

## 9. C++ shim only when necessary

Don't write a C++ half unless you need one of:

- **Chat slash commands** (`/foo`) — must register at `CuiChatWindow::ctor`
  time, which is before the CLR is ready.
- **A new detour** that UtinniCore doesn't already wrap — you're adding a
  `swg/<subsystem>/` entry.
- **Per-frame work that's prohibitively expensive in managed code** — rare;
  the bridge is fast.

In every other case, write a `IPlugin` / `IEditorPlugin` in C# and use the
existing callbacks. If you do need C++, keep it to plugin scaffolding +
forwarding to managed events (e.g. via `EventHandler<T>` raised on a shared
static instance).

## 10. Disable controls until a scene is loaded

Most editor controls only make sense inside a scene. Wire enable/disable on
`GameCallbacks`:

```csharp
public MySubPanel()
{
    GameCallbacks.AddSetupSceneCall(() =>
        this.BeginInvoke((MethodInvoker)(() => SetEnabled(true))));
    GameCallbacks.AddCleanupSceneCall(() =>
        this.BeginInvoke((MethodInvoker)(() => SetEnabled(false))));
    SetEnabled(false);   // default
}

private void SetEnabled(bool enabled)
{
    foreach (Control c in Controls)
        if (c is UtinniButton or UtinniNumericUpDown or UtinniTextbox)
            c.Enabled = enabled;
}
```

Better UX, fewer null-deref bugs in your handlers.

## 11. Name things with plugin prefixes

For hotkeys, INI sections, and anything else that might collide with other
plugins, prefix:

- Hotkey `Name` → `MyPlugin.SaveScene` not `SaveScene`.
- Logger callouts → `[MyPlugin] ...` (use `Log.Info("[MyPlugin] " + msg)` or
  rely on the `writeClassName` config flag).

This is also why `[Plugins] plugin_N` in `ut.ini` is keyed by *directory
name* — that directory name is your plugin's namespace.

## 12. Build outputs into `Plugins/<Name>/`

The Directory.Build.props the VSIX wizard writes does this for you. If
you're hand-rolling a project:

```xml
<PropertyGroup>
  <OutputPath>$(SolutionDir)bin\$(Configuration)\Plugins\$(MSBuildProjectName)\</OutputPath>
</PropertyGroup>
```

And reference `UtinniCoreDotNet.dll` with `Private=False` so you don't
shadow the install's copy.

## See also

- [The Jawa Toolbox](jawa-toolbox.md) — the worked example these patterns
  come from.
- [Utinni docs](../../../Utinni/docs/index.html) — the framework these
  patterns sit on top of.
