# Usage (API Reference)

> Last updated: 2026-10-31 / Unity: 6000.3.26f1

This document explains how to use the settings asset, the public API, the Editor tools, and the samples of `AddressableManager` in your project. For installation (from the Package Manager, or by writing a pinned version in `manifest.json`) and the quickest way to get started, see the [README](../README.md#installation).

- Code examples use the UniTask version. In projects without UniTask, the return type is `Awaitable` instead of `UniTask` (the way you call it is the same)
- **`await` each returned value only once.** Both `UniTask` and `Awaitable` allow a single `await`. Do not store a returned value to `await` it again later or from multiple places
- All operations go through `IAddressableHandler` (`AddressableManager.AccInstance.AccHandler`). Add `using MAH.AddressableManager;` to use it
- Terms (release, reclaim, lifetime, etc.) follow the [glossary](glossary.md) (Japanese; see its "英語" (English) column for the English terms)

---

## 1. Settings Asset

Create it with `Create > MAH > Addressable > RuntimeSettings` and assign it in the `AddressableManager` Inspector. If none is assigned, a warning is logged and the defaults below are used.

### Logging

| Field | Default | Description |
| --- | --- | --- |
| Log Level | `Warning` | Threshold of logs to output. `Verbose` / `Warning` / `Error` / `None` |

### Initialization

| Field | Default | Description |
| --- | --- | --- |
| Mode | `Auto` | `Auto`: starts initialization in the Manager's `Awake` / `Manual`: does not start until `InitializeAsync` is called |
| Load Before Initialize | `PassThrough` | How a Load called before initialization succeeds is handled ([2.3](#23-loading-before-initialization)) |
| Auto Release Handle | ON | Value passed to `autoReleaseHandle` of `Addressables.InitializeAsync`. Usually no need to change |
| Enable Catalog Cache Busting | OFF | Adds a per-request query to the remote catalog URL so that the OS URL cache does not return an old catalog (turn ON when updates are not reflected on iOS / macOS) |

### Download

| Field | Default | Description |
| --- | --- | --- |
| Catalog Policy | `CheckAndUpdate` | How `CheckUpdateAsync` handles the catalog. `None`: no network access / `CheckOnly`: only checks whether an update exists / `CheckAndUpdate`: checks and updates |
| Labels | Empty | Labels targeted by the API overloads that take no labels. Selectable from a dropdown |
| Max Retry | 3 | Number of retries on failure (0–10). Counted per phase (check, update, size calculation, download) |
| Retry Delay Ms | 1000 | Wait time before a retry (milliseconds, 0–60000) |
| Auto Clean Bundle Cache | OFF | Whether to clear the cache of old bundles that are no longer used when the catalog is updated |

### Loading and Releasing

| Field | Default | Description |
| --- | --- | --- |
| Enable Owner Polling | ON | Whether to also detect the destruction of Owner-lifetime GameObjects by periodic checks (guards against missed destruction) |
| Owner Polling Interval Sec | 3 | Interval of the check above (seconds). Advances in real time even while paused (`timeScale = 0`) |
| Max Retry | 3 | Number of retries when a load fails. A mistyped address (`InvalidKeyException`) fails immediately without retrying |
| Retry Delay Ms | 1000 | Wait time before a retry (milliseconds) |

---

## 2. Initialization

### 2.1 Waiting for Initialization

```csharp
var handler = AddressableManager.AccInstance.AccHandler;
try
{
    // Auto: just waits for the running initialization / Manual: initialization starts here
    await handler.InitializeAsync(progress, ct);
}
catch (AddressableInitializeException ex)
{
    // Initialization cannot be retried until the app restarts, and every later Load also fails. Prompt the user to restart
    ShowFatalErrorDialog(ex); // The cause is ex.InnerException
}
```

| State when called (`AccInitializeState`) | Behavior of `InitializeAsync` |
| --- | --- |
| `NotStarted` | Starts initialization and waits until it completes |
| `Running` | Waits for the running initialization (does not start a new one) |
| `Succeeded` | Completes immediately |
| `Failed` | Immediately throws `AddressableInitializeException` (does not retry) |

- `ct` only cancels the caller's wait. The initialization itself does not stop
- `progress` receives the progress (0–1) while waiting, and always receives 1 on completion

### 2.2 Receiving Events

In Auto mode, initialization may already be finished before you subscribe (if Addressables is already initialized, it completes inside `Awake`). Also check `AccIsInitialized` when subscribing.

```csharp
private void Start()
{
    var handler = AddressableManager.AccInstance.AccHandler;
    if (handler.AccIsInitialized) { HandleInitialized(); return; }
    handler.OnInitialized += HandleInitialized;
    handler.OnInitializeFailed += HandleInitializeFailed; // Called only once on failure
}

private void OnDestroy()
{
    var manager = AddressableManager.AccInstance;
    if (manager == null) return;
    manager.AccHandler.OnInitialized -= HandleInitialized; // Always unsubscribe in pairs
    manager.AccHandler.OnInitializeFailed -= HandleInitializeFailed;
}
```

If you do not want to worry about missing the event, use `await InitializeAsync()`.

### 2.3 Loading Before Initialization

Choose with "Load Before Initialize" in the settings asset. After initialization has failed, Load is blocked regardless of this setting (`Release` is not blocked).

| Initialization state | `PassThrough` (default) | `WaitForInitialize` |
| --- | --- | --- |
| `NotStarted` (Manual mode only) | Loads as is (Addressables initializes itself automatically) | `InvalidOperationException` (`InitializeAsync` was not called) |
| `Running` | Loads as is | Waits for initialization to complete, then loads |
| `Succeeded` | Loads as is | Loads as is |
| `Failed` | `AddressableInitializeException` | `AddressableInitializeException` |

---

## 3. Content Update (Download)

Checks for updates and downloads asset bundles per Label. Downloading only stores bundles in the cache; it does not load assets.

| API | Purpose |
| --- | --- |
| `HasUpdateAsync` | Only determines whether an update exists. Does not update the catalog |
| `CheckUpdateAsync` | Checks and updates the catalog according to "Catalog Policy", and returns the number and size of bundles that need downloading (for a confirmation dialog) |
| `DownloadAsync` | Downloads the bundles of the target Labels. Does not update the catalog |

- The overloads without labels target the "Labels" in the settings asset (`InvalidOperationException` when empty). The overloads with labels (`IReadOnlyList<string> labels`) use the labels you pass (passing `null` throws `ArgumentNullException`, delivered at the `await`, not at the call)
- All of them start **after waiting for initialization to succeed** (`InvalidOperationException` if initialization has not started in Manual mode)
- **Only one** check, determination, or download can run at a time. Calling one while another is running throws `InvalidOperationException`, so prevent repeated button presses on the caller side

### 3.1 Check, Then Download

```csharp
var handler = AddressableManager.AccInstance.AccHandler;

// Check → show the count and size → download if the user agrees
var result = await handler.CheckUpdateAsync(ct);
if (result.AccHasDownload && await ShowConfirmDialogAsync(result.AccBundleCount, result.AccTotalBytes))
{
    try
    {
        await handler.DownloadAsync(Progress.Create<DownloadStatus>(OnProgress), ct);
    }
    catch (AddressableDownloadException ex)
    {
        ShowRetryDialog(ex); // ex.AccPhase is the phase that failed, InnerException is the original exception
    }
}
```

Main values of `DownloadCheckResult`:

| Value | Description |
| --- | --- |
| `AccHasDownload` | Whether there are bundles that need downloading |
| `AccBundleCount` | Number of bundles that need downloading |
| `AccTotalBytes` | Total bytes that need downloading |
| `AccCatalogUpdateCount` | Number of catalogs found to have updates (with `CheckAndUpdate`, the number applied; with `CheckOnly`, the number not applied) |
| `AccIsCatalogUpdatePending` | Whether, with `CheckOnly`, catalogs with updates were left unapplied. In this case, the count and size are for the current catalog |

- `DownloadAsync(null)` is ambiguous between overloads and fails to compile. When passing only progress, use `progress:`; when passing only labels, use `labels:`
- Without UniTask, pass a class that implements `IProgress<DownloadStatus>` instead of `Progress.Create`

### 3.2 Receiving Progress

The basic way to receive progress is through the `IProgress<DownloadStatus>` passed to `DownloadAsync`. At the end of the process, a value whose `AccPhase` is `Completed`, `Failed`, or `Canceled` is delivered exactly once.

| `DownloadStatus` value | Description |
| --- | --- |
| `AccPhase` | Phase (`None` / `CheckingCatalog` / `UpdatingCatalog` / `CalculatingSize` / `Downloading` / `Completed` / `Failed` / `Canceled`) |
| `AccIsDownloading` / `AccIsBusy` | Whether downloading / whether processing is in progress |
| `AccDownloadedBytes` / `AccTotalBytes` / `AccPercent` | Downloaded and total bytes, and the ratio (0–1) |
| `AccRetryCount` | Number of retries in the current phase |

To view the state from another UI that was not passed the `IProgress`, **get `handler.AccDownloadStatus` again every time you read it** (it is a copy of the values at the time of retrieval and does not change afterward).

```csharp
private void Update()
{
    var status = AddressableManager.AccInstance.AccHandler.AccDownloadStatus; // Get it every time
    _bar.gameObject.SetActive(status.AccIsDownloading);
    _bar.value = status.AccPercent;
}
```

### 3.3 Cancellation and Failure

- Canceling `ct` ends the caller's wait with `OperationCanceledException`. Network requests that have already started may not stop
- While the catalog is being updated (`UpdatingCatalog`), the update cannot be stopped midway, so cancellation takes effect after the update completes (even if canceled, the catalog may already be updated)
- When all retries are used up, `AddressableDownloadException` is thrown

---

## 4. Loading and Releasing

### 4.1 Basic Flow

```csharp
var handler = AddressableManager.AccInstance.AccHandler;

var ticket = await handler.LoadAsync<Sprite>(AssetKey.FromPath(_address), AssetLifetime.Owner(this), ct);
_image.sprite = ticket.AccAsset; // The loaded asset itself

// When no longer needed, return the ticket to release it
handler.Release(ticket);
```

- `LoadAsync<T>` returns the loaded asset itself together with a ticket for releasing it (`AssetTicket<T>`). **Explicit release is the norm**
- No matter how many times you Load the same asset, the actual load happens once and is managed by a reference count. Release once per Load, using the ticket you received
- Releasing the same ticket twice only logs a warning; it does not throw
- Do not use the asset (`AccAsset`) after it has been released

### 4.2 Keys (`AssetKey`)

| How to create | Description |
| --- | --- |
| `AssetKey.FromPath(string address)` | Creates a key from an Addressables address. Letting users pick the address with the `[AddressablePath]` attribute prevents typos ([Chapter 5](#5-attributes-dropdown-selection)) |
| `AssetKey.FromReference(AssetReference reference)` | Creates a key from an `AssetReference` |

If you Load the same asset both by Path and by AssetReference, it is treated as two different keys and loaded twice. This is by design. Use only one of the two for the same asset.

### 4.3 Lifetime (`AssetLifetime`)

Specify a lifetime when loading. Whatever has not been released when the lifetime ends is reclaimed automatically. Automatic reclaiming is a **safety net** against forgotten releases; when it happens, a warning with the stack trace of the allocation is logged (in the Editor). Normally, call `Release`.

| Lifetime | When it is automatically reclaimed |
| --- | --- |
| `AssetLifetime.Manual` | Never reclaimed automatically. Remains until `Release` is called or the Manager is destroyed. Use for resident assets or when you manage the release timing yourself |
| `AssetLifetime.Scene()` | When the scene that was active at the time of the call is unloaded |
| `AssetLifetime.Scene(scene)` | When the specified scene is unloaded. A scene loaded with `Addressables.LoadSceneAsync` (`SceneInstance.Scene`) can also be specified |
| `AssetLifetime.Owner(gameObject)` / `Owner(component)` | When the specified GameObject is destroyed |

- Only a GameObject or Component can be an Owner. For pure C# classes, use the `Scene` lifetime or specify the GameObject of a related View
- Specifying an already-destroyed Owner or a scene that is not loaded throws `ArgumentException`
- If you specify the `DontDestroyOnLoad` scene, a warning is logged and it is treated the same as `Manual`

### 4.4 Handling Prefabs

`LoadAsync<GameObject>` returns the **Prefab asset**, not an instance. Instantiate it with `Object.Instantiate` and destroy it with `Object.Destroy`.

- `Release` does not destroy instances you have already created
- Calling `Release` while instances still exist may break their appearance as materials and textures are lost
- It is safe to specify the GameObject that holds the instance as the Owner, or to specify the same lifetime as the instance (Scene, etc.)

### 4.5 Loading by Label

`LoadByLabelAsync` loads all assets with a Label at once and returns a group ticket (`AssetGroupTicket`). You do not specify a type when loading; you specify it when retrieving.

```csharp
var group = await handler.LoadByLabelAsync(_label, AssetLifetime.Owner(this), ct);

if (group.TryGetAsset<Sprite>("icon_sword", out var sprite))
    _image.sprite = sprite;

var clips = group.GetAssets<AudioClip>();
for (int i = 0; i < clips.Count; i++) // foreach causes GC due to boxing of the enumerator, so loop with for
    Register(clips[i]);

// Release them all at once
handler.Release(group);
```

- Only assets whose loaded type **exactly matches** `T` can be retrieved (base types are not searched; specifying `Object` returns nothing)
- The address passed to `TryGetAsset` must match the Addressables address
- For a Label with no matching assets, a warning is logged and an empty group ticket is returned
- A group ticket that has been released or reclaimed has `AccIsReleased` set to `true`, and `GetAssets<T>()` returns an empty result. Do not use assets retrieved before that either
- For images with Sprite Mode set to Multiple and for Sprite Atlases, only the first sprite is loaded per address
- The order of results is not guaranteed

### 4.6 Exceptions and Cancellation

| Situation | Exception |
| --- | --- |
| The load failed (all retries used up, or the address does not exist) | `AssetLoadException` (`AccKey` is the failed key, `InnerException` is the original exception) |
| `ct` was canceled / the lifetime ended during the load | `OperationCanceledException` |
| Load was called after initialization failed | `AddressableInitializeException` |
| Load was called after the Manager was destroyed | `ObjectDisposedException` |
| The address is null or empty / the lifetime is invalid | `ArgumentException` |

When canceled or failed, the module rolls back the reference count and no ticket is issued. You do not need to call `Release`.

```csharp
try
{
    _ticket = await handler.LoadAsync<Sprite>(key, AssetLifetime.Manual, destroyCancellationToken);
    _hasTicket = true;
}
catch (OperationCanceledException)
{
    // Destroyed while loading. No release needed
}
catch (AssetLoadException ex)
{
    Debug.LogException(ex);
}
```

---

## 5. Attributes (Dropdown Selection)

Add them to a string field to pick an address or Label from a dropdown in the Inspector. This prevents typos from manual input. Add `using MAH.AddressableManager.Attribute;` to use them.

```csharp
[SerializeField, AddressablePath] private string _address; // Pick an address from a per-group hierarchy
[SerializeField, AddressableLabel] private string _label;  // Pick a Label from a list
```

- If the field contains an address or Label that does not exist, a warning is shown in the Inspector
- Adding them to a non-string field shows an error
- In projects without Addressables settings, the field is shown as a normal text input
- The `[AddressableLabel]` list shows every Label registered in the Addressables Labels settings (including Labels not attached to any asset)

---

## 6. Editor Tools

Tools for the Editor. The Debug Window and Dependency Check are **read-only**: they have no operations that change the state or registration of Addressables (manual release, forced reclaim, adding to groups). The two asset registration tools ([6.3](#63-auto-registration), [6.4](#64-register-to-addressables-manual)) are the only exception: they write to the Addressables settings (groups and entries), and only as you instruct them. None of the tools are included in builds.

> **Note:** The UI of the Editor tools can be displayed in Japanese or English, and you can add other languages yourself ([6.6](#66-editor-language)). In this chapter, on-screen labels are written in Japanese, followed by the English label in parentheses (e.g. **ログ出力** (Export Log)). The English text on screen may differ slightly in capitalization or wording from the parentheses.

| Tool | How to open | Purpose |
| --- | --- | --- |
| Debug Window | Menu `MAH/Addressable/Debug Window` | Check the loading state, leaks, and Owner reclaiming during Play ([6.1](#61-debug-window)) |
| Dependency Check | Menu `MAH/Addressable/Dependency Check` | Check unregistered assets used by registered assets, without entering Play mode or building ([6.2](#62-dependency-check)) |
| Auto Registration | Menu `MAH/Addressable/Auto Registration`, and rule assets created with `Create > MAH > Addressable > AutoRegistrationRule` | Register assets in Addressables automatically on import, following rules you write. The window lists the rules, their conflicts, and reapplies the rules to existing assets ([6.3](#63-auto-registration)) |
| Register to Addressables | Right-click a folder or assets in the Project window, then `MAH > Register to Addressables...` | List everything under the selected folders, narrow it down by extension and by checkboxes, and register it into a group in one go ([6.4](#64-register-to-addressables-manual)) |
| Editor Settings | `Create > MAH > Addressable > EditorSettings` | Settings for the tools above, such as the refresh interval, output folder, and registration defaults ([6.5](#65-editor-settings)) |
| Language | `Edit > Preferences > MAH Addressable`, and menu `MAH/Addressable/Localization/Export Translation Template` | Switch the display language of the tools between Japanese and English, and add or customize a language with a JSON file ([6.6](#66-editor-language)) |

For the attributes that let you pick an address or Label in the Inspector, see [Chapter 5](#5-attributes-dropdown-selection).

### 6.1 Debug Window

Check the state of `AddressableManager` during Play by switching tabs. When not in Play mode, it shows "未起動" (not running). The display is refreshed at a fixed interval (0.5 seconds by default), so changes right after a release or scene transition appear at the next refresh.

#### "実行時状態" (Runtime State) Tab

Lists the loaded assets and the tickets that have not been released. You can see "which asset is still held, from which lifetime, and how many times".

- Each row shows the asset name, key (`AssetKey`), type, reference count, and load status (● loading = yellow / loaded = green / failed = red)
- Expanding a row shows the allocation count per lifetime (■ Manual = blue / Scene = green / Owner = orange) and the stack trace of where each ticket was allocated. Clicking a line of the stack trace jumps to that code
- The buttons at the top (All / Manual / Scene / Owner) filter by lifetime kind
- If the same asset is loaded both by Path and by AssetReference, it appears as separate rows ([4.2](#42-keys-assetkey))

#### "リーク" (Leak) Tab

Lists, grouped by allocation source, what was automatically reclaimed at the end of a lifetime without being `Release`d (leaks). Logs scroll by and are easy to miss, so check here what needs fixing.

- Each row shows the key, type, count (×N), last occurrence time, and a button that jumps to the calling code. Expanding a row shows the full stack trace
- **表示をクリア** (Clear View): shows only leaks that occur after this point (the records themselves are not deleted)
- **ログ出力** (Export Log): writes the displayed rows to `AddressableLeaks_yyyyMMdd_HHmmss.txt` (a new file is added each time)
- Turning ON "Export On Exit Play Mode" in the Editor Settings writes that session's leaks to `AddressableLeaks_LastPlay.txt` when Play ends (only the previous one is kept as `AddressableLeaks_LastPlay-prev.txt`)
- Only Scene / Owner lifetime allocations whose ticket was received but never `Release`d are recorded. Lifetimes that ended during the load, the `Manual` lifetime, and releases caused by the Manager being destroyed are not recorded
- The list is cleared when Play ends. Use **ログ出力** (Export Log) to keep it. Nothing is recorded in built apps or on devices

#### "Owner" Tab

Check that automatic reclaiming of the Owner lifetime works as expected.

- **監視中の Owner** (Watched Owners): name, number of tickets held, and state (● Watcher ✓ = green: normal / ● 破棄済み・未回収 (destroyed, not reclaimed) = yellow: waiting to be reclaimed by the periodic check / ● Watcher なし・未登録 (no Watcher, unregistered) = red: destruction cannot be detected. Normally does not appear)
- **回収の記録** (Reclaim Records): shows, with counts, whether reclaiming happened through the GameObject's destruction notification (`[OnDestroy]`) or the periodic check (`[ポーリング]` (Polling)). If polling is frequent, check whether you are using as an Owner a GameObject that was destroyed without ever becoming active
- The list is cleared when Play ends

### 6.2 Dependency Check

Lists **unregistered assets (children)** used by assets registered in Addressables (parents). Unregistered children are implicitly included in the bundle of the parent that uses them, so seeing which parent in which group uses them helps you review how Groups are split and configured.

1. Open the window and press **Scan** (it follows the dependencies of all groups. You can cancel from the progress bar; if canceled, the previous results remain)
2. One row is shown per unregistered child. The columns are name, type, path, size, number of groups, number of parents, and marks
   - Rows with **2 or more groups** are highlighted as "複数グループ" (multiple groups). They are used from multiple groups and may be duplicated in each group in the build
   - Expanding a row shows the parents that use it and their groups. If it is used through another registered asset, "（経由：path）" (via: path) is shown
   - Marks: `Resources/` (an asset under `Resources`. A hint of missing registration or migration) / `Pack Separately` (includes a parent from a group that can duplicate even within the same group)
3. You can filter by type, name, and group, and sort by file size in descending order. Scripts, DLLs, and assets under `Packages/` are hidden by default and can be shown with a toggle
4. Clicking a row selects that asset in the Project window
5. **テキスト出力** (Export Text) writes the displayed rows to `AddressableDependencies_yyyyMMdd_HHmmss.txt`

Notes:

- "複数グループ" (multiple groups) is based on the number of the parents' groups, not a determination of whether the asset is actually duplicated in the build. For an accurate check, use the standard Addressables Analyze (Check Duplicate Bundle Dependencies)
- The size is the file size on disk. It differs from the size in a bundle or the memory on a device. Use it for comparing assets with each other
- Dependencies of assets that are only loaded by address from code (with no serialized references) cannot be followed
- Scan results survive recompilation and are cleared when the Editor restarts

### 6.3 Auto Registration

Registers assets in Addressables for you whenever they are imported, following **rules** that you write as assets. It removes the manual work of adding assets to groups, setting addresses, and attaching Labels, and it keeps the result the same on every team member's machine.

- It is **ON by default**, but **nothing happens until you create a rule** and choose its extensions, so installing the package never changes your groups
- To turn it off completely (for example, if you use another registration tool), turn OFF "Auto Registration" in the [Editor Settings](#65-editor-settings). All rules are then ignored

#### Creating a rule

Each rule covers **one folder**. Create as many as you like; the same folder can have several rules if their extensions differ.

1. Right-click in the Project window and choose `Create > MAH > Addressable > AutoRegistrationRule` (the folder you created it in becomes the Target Folder), or press **作成** (Create) in the Auto Registration window (the rule is created in `Assets/MAH.Data/AddressableManager/Editor/AutoRegistration/`)
2. Choose the **Extensions** to register. A new rule has none, so it registers nothing yet
3. Press **当て直す** (Reapply) in the Auto Registration window to apply the rule to the assets that already exist. From then on, imported assets are registered automatically

| Field | Description |
| --- | --- |
| Enabled | Turn OFF to temporarily stop this rule (it is treated as if it did not exist) |
| Target Folder | A folder under `Assets/`. It is stored by GUID, so renaming or moving the folder does not break it |
| Target Mode | `Extensions`: only the extensions you choose / `All`: everything Addressables can register |
| Extensions | Extensions to register (case-insensitive; `png` and `.PNG` are the same). Pick them from the **拡張子を選ぶ ▼** (Select extensions) dropdown, which lists the extensions found in the folder first, or type one and press **追加** (Add). Empty means nothing is registered |
| Excluded Extensions | Extensions to skip when Target Mode is `All` |
| Exclude Patterns | Glob patterns for assets to skip (for example `**/_Test/**`) |
| Address Template | The address to give each asset. Macros are available ([below](#macros)). Empty means the asset path |
| Target Group | The name of the group to add the assets to. Macros are available. Empty means the Addressables default group. Pick an existing group from **グループを選ぶ ▼** (Select group), or type a name; a group that does not exist yet is created |
| Group Template Asset | The Addressables Group Template used when the group has to be created. Empty means the default Group Template in the Editor Settings |
| Labels | Labels to attach. Macros are available. Pick existing ones from **Label を選ぶ ▼** (Select Label), or type them |
| Address Mode | `Follow`: recalculate the address when the asset is moved or renamed (a warning shows the old and new address) / `Fixed`: once registered, never change the address |

The Inspector shows a **read-only preview** under the fields: the number of target assets, the result for the first three, and warnings (empty extensions, unknown macros, overlap with other rules, and so on).

Text fields (Address Template, Target Group, Extensions, Labels, Exclude Patterns) are applied when you press Enter or leave the field, so the preview does not change while you are typing.

> **Note:** `All` registers everything under the folder, including the textures and materials that a Prefab uses. Each of them becomes its own entry instead of being included in the Prefab's bundle, so the bundle layout changes.

#### How it behaves

- It runs when an asset is added, imported, moved, or renamed (including after a `git pull`). It does **not** run when you create or edit a rule; use **当て直す** (Reapply) for that
- The result is always the same for the same asset and rule, and nothing is written when there is no difference. This keeps the group assets from conflicting in git
- If you change the group, address, or Labels of an entry that matches a rule by hand, **it is reset to the rule's values at the next import**. To make an exception, add an Exclude Pattern
- Assets that no longer match any rule (moved out, or the rule changed) **stay registered**, and a warning is logged. Remove them yourself if you want
- Entries that match no rule (registered by hand or by another tool) are never touched
- If an asset matches **two or more rules** (their folders and extensions both overlap), it is a **conflict**: the asset is not registered or changed, and one error per pair of rules is logged. Fix the rules, or add an Exclude Pattern to the outer rule
- If different assets end up with the same address, they are still registered, and an error is logged
- These are never registered: folders, scripts and assembly definitions (`.cs`, `.asmdef`, `.asmref`, and so on), anything under an `Editor` folder or `Packages/`, the Addressables settings folder, and the rule and Editor Settings assets. Assets under `Resources/` are not registered either, and a warning is logged
- If you use a mistyped or unknown macro, it is kept as written and a warning is logged. If the expanded group name contains characters that cannot be used in a file name (for example `/`), the asset is not registered and an error is logged
  - **Known limitation:** which characters are rejected depends on the OS. On macOS and Linux, names containing characters that Windows does not allow (such as `*`, `?`, `"`, `<`, `>`, `|`, `:`) are accepted, and Windows team members then cannot check out that group. In a team that mixes operating systems, use only characters that every OS allows in group names
- When many warnings are produced at once (for example, addresses changed by reapplying a rule), they are combined into one warning with the count and the first five while Log Detail is `Summary`. Set it to `PerAsset` to see all of them ([6.5](#65-editor-settings))

#### Macros

Macros are written in `{ }` and can be used in the Address Template, Target Group, and Labels. Names are case-sensitive. Add `:lower` or `:upper` to change the case, for example `{Asset.Name:lower}`. The examples assume the asset `Assets/App/Audio/BGM/Title.wav` and the target folder `Assets/App`.

| Macro | Meaning | Example |
| --- | --- | --- |
| `{Asset.Path}` | Asset path | `Assets/App/Audio/BGM/Title.wav` |
| `{Asset.Name}` | File name without the extension | `Title` |
| `{Asset.Extension}` | Extension without the dot | `wav` |
| `{Asset.Type}` | Type name of the main asset | `AudioClip` |
| `{Asset.Guid}` | GUID (does not change when the asset is moved or renamed) | `3f2a…` |
| `{Folder.Name}` | Name of the parent folder | `BGM` |
| `{Folder.Top}` | Name of the folder directly under the target folder | `Audio` |
| `{Folder.Relative}` | Path from the target folder to the parent folder | `Audio/BGM` |
| `{Root}` | The target folder | `Assets/App` |
| `{Path.Relative}` | Path from the target folder, without the extension | `Audio/BGM/Title` |
| `{Rule.Name}` | Name of the rule asset (empty in manual registration) | `BGM_Rule` |

Some Unity values can also be used. Any other static property or Addressables Profile variable is **not** supported (it is treated as an unknown macro).

| Macro | Value | Differs by environment? |
| --- | --- | --- |
| `{UnityEngine.Application.version}` | Version in Player Settings | No |
| `{UnityEngine.Application.productName}` | Product Name | No |
| `{UnityEngine.Application.companyName}` | Company Name | No |
| `{UnityEngine.Application.identifier}` | Bundle Identifier | Can differ by platform |
| `{UnityEngine.Application.unityVersion}` | Editor version | Can differ if team members use different Editor versions |
| `{UnityEngine.Application.platform}` | Platform the Editor runs on | **Yes** (depends on the OS) |
| `{UnityEditor.PlayerSettings.bundleVersion}` | Version in Player Settings | No |
| `{UnityEditor.EditorUserBuildSettings.activeBuildTarget}` | Selected build target | **Yes** |

Avoid values that differ between team members: each person's import would rewrite the addresses the others produced. Also note that an address that uses `{UnityEngine.Application.version}` changes when you raise the version and reapply the rules.

If an expanded address is empty, the asset path is used; an empty group name means the default group; an empty Label is skipped.

#### The Auto Registration window

Open it from `MAH/Addressable/Auto Registration`. It is not required for auto registration to work. It never edits a rule (edit them in the Inspector).

- **Rule list**: one row per rule asset, with 有効 (Enabled), ルール (Rule), 対象フォルダ (Target Folder), 拡張子 (Extensions), グループ (Group), アドレス (Address), and 対象の数 (number of target assets). Click a row to select the rule asset. A `!` mark shows rules that register nothing (empty extensions) or use `All`
- **Conflicts, duplicate addresses, errors, and warnings** are collected below the list, because Console messages scroll by and are easy to miss. Click a rule name or an asset path to select it. Long lists show one line per item; hover over a line to see all of it
- **当て直す** (Reapply) applies the rules to the assets that already exist. It first lists what would change (additions, group moves, address changes, Label changes) without writing anything. Press **適用** (Apply) to write it, or **キャンセル** (Cancel). You can cancel from the progress bar; what was already written stays. If nothing would change, it says so and writes nothing
- If an entry was changed by something else after the list was made (for example, you registered or moved it in the Groups window), Apply **skips** that entry instead of overwriting it, and tells you how many were skipped. Check the new list and apply again
- **再読み込み** (Reload) rebuilds the list. It also rebuilds when a rule asset is added or deleted. If you only edited a rule, the window shows that its list may be out of date, because rebuilding scans the whole project
- When "Auto Registration" is OFF in the Editor Settings, the window says so, and Reapply is disabled

### 6.4 Register to Addressables (Manual)

For when you want to decide the timing yourself, or keep test assets out. You do not need a rule, and it works even when Auto Registration is OFF.

1. Right-click one or more folders (or assets) in the Project window and choose `MAH > Register to Addressables...`
2. **The list appears immediately.** Everything under the selected folders is listed, with a thumbnail, the path, and the address it would get. Assets that are already registered are not shown (the number hidden is displayed)
3. **Narrow it down by extension.** The extensions found in the targets are listed with their counts, all ON at first. Turn one OFF to remove those assets from the list, or use **すべてオン** (All on) / **すべてオフ** (All off). You can also uncheck individual rows. If you selected assets directly instead of folders, the extension filter does not apply to them
4. **Choose the group and Labels (optional).** 追加先のグループ (Target group) and Label start from the values in the [Editor Settings](#65-editor-settings), so leave them as they are to use those. Pick them from the dropdowns or type them (typed values are applied when you press Enter or leave the field). A group that does not exist yet is created when you register
5. Press **登録する（N 件）** (Register (N items)). A confirmation dialog shows how many assets will be registered, any new groups and Labels, and any duplicate addresses; choose whether to continue. Assets that were registered by something else while the window was open are skipped (not moved), and the number skipped is shown

Notes:

- The address and the exclude patterns are not on this screen; they use the Editor Settings values (Default Address Template and Default Exclude Patterns). `{Root}` is the folder you selected, and `{Rule.Name}` is empty
- Assets that match an enabled auto-registration rule are shown but cannot be checked, with the state 自動登録の対象（未適用）(subject to auto registration, not applied yet), because the rule would reset anything you set by hand. Reapply the rule instead ([6.3](#63-auto-registration)). A conflicting rule shows as 競合中 (conflicting)
- Labels are only added; existing Labels on an asset are never removed
- Include subfolders is ON by default. Turning it OFF reloads the list with only the files directly in the folder
- The "MAH" entry of the right-click menu is placed apart from the other commands, separated by a line. Where Unity draws the line is decided by menu order numbers, so another package that uses a close number may share the section

### 6.5 Editor Settings

The settings asset for the Editor tools. **If none exists when the Editor starts, it is created automatically at `Assets/MAH.Data/AddressableManager/Editor/AddressableEditorSettings.asset`** (you may move it anywhere). To create one manually, use `Create > MAH > Addressable > EditorSettings`. Keep only one in the project (if there are several, a warning is logged and the first one is used).

| Section | Field | Default | Description |
| --- | --- | --- | --- |
| Debug Window | Refresh Interval Sec | 0.5 | Interval (seconds) for re-reading the runtime state. Minimum is 0.1 seconds |
| Leak | Output Folder | `Logs/AddressableManager` | Output folder for logs. Relative paths are resolved from the project root (the parent of `Assets`); absolute paths are used as is. Leaving it empty restores the default |
| Leak | Export On Exit Play Mode | OFF | Automatically writes leaks to `AddressableLeaks_LastPlay.txt` when Play ends |
| Dependency Check | Output Folder | `Logs/AddressableManager` | Output folder for the text export (relative and absolute paths are handled the same as for leaks) |
| Registration | Auto Registration | ON | Turn OFF to disable auto registration completely (all rules are ignored). Manual registration is not affected |
| Registration | Log Detail | `Summary` | `Summary`: one line per import (for example "3 registered, 1 group moved"); more than five warnings are combined into one (the count and the first five) / `PerAsset`: one line per asset, and every warning. Conflicts, duplicate addresses, and errors are always logged in full |
| Registration | Default Address Template | `{Asset.Path}` | Copied into a rule when you create it, and used by manual registration. The asset path is always unique and equals the address Addressables gives by default |
| Registration | Default Labels | Empty | Copied into a rule when you create it, and the starting Labels of manual registration |
| Registration | Default Exclude Patterns | `**/_Test/**`, `**/Editor/**` | Copied into a rule when you create it, and used by manual registration |
| Registration | Default Target Group | Empty | **Manual registration only.** The group the assets go to when you do not choose one. Empty means the Addressables default group. Rules do not use this |
| Registration | Default Group Template | Empty | The Group Template used when a group has to be created and no Group Template is specified. Empty means the first Group Template in the Addressables settings |

The "Default ..." values for rules are only copied when a rule is created; changing them later does not change existing rules.

The default output folder `Logs/` is not imported by Unity and is usually not tracked by git.

### 6.6 Editor Language

The Editor tools can be displayed in **Japanese** or **English**. Switch it in `Edit > Preferences > MAH Addressable > Language` (the list also shows any language you added, see below).

- The setting is **personal**: it is stored per user in the Unity Editor, not in the project, so each team member can use a different language.
- Until you choose one, the language settings of your PC are used, with **Japanese taking priority**: Japanese is shown if the OS language, the display language, or the regional format of the PC is Japanese, or if none of them can be read; otherwise English is shown.
- It applies to the windows, the Inspector labels and tooltips, dialogs, the Console messages written by the Editor tools, and the text files they export (leak logs and Dependency Check). Windows that are already open switch immediately.
- It does not apply to the runtime: exceptions and logs written by `AddressableManager` itself while your game runs (for example the messages about a leak or a released ticket) stay in Japanese.

#### Adding or customizing a language

The texts of the Editor tools are stored in JSON files, so you can add a language, or fix a bundled translation, **without changing the package** (a package installed from a git URL is read-only, so the file lives in your own project).

1. Choose the menu `MAH/Addressable/Localization/Export Translation Template`, and save the file under `Assets` with the name **`MAH.AddressableManager.Editor.<language code>.json`**, for example `MAH.AddressableManager.Editor.fr.json`. The file already contains every English text.
2. Open it and edit:
   - `languageCode`: the code of the language, such as `fr`. Use the same code as in the file name.
   - `languageName`: the name shown in the Preferences list, written in that language (for example `Français`).
   - `entries`: translate each `value`. **Do not change a `key`.**
3. Save the file. Unity imports it, and the language appears in `Edit > Preferences > MAH Addressable > Language`. Select it, and the open windows switch immediately.

```json
{
    "languageCode": "fr",
    "languageName": "Français",
    "entries": [
        { "key": "debug.notRunning", "value": "Non démarré. Aucun AddressableManager en mode Play." },
        { "key": "leak.summary", "value": "{0} fuites ({1} sites d'allocation)" }
    ]
}
```

Things to know:

- **You can translate only part of it.** A text you leave out is shown in English, so you can start with the screens you use most. Delete the entries you do not translate if you like; a smaller file is easier to maintain.
- **Customizing a bundled language.** If you use the code of a bundled language (`ja` or `en`), your file overrides the bundled text **key by key**, and everything else stays as bundled. This is how to change just a few wordings.
- **Placeholders such as `{0}` and `{1}`** are replaced with numbers, names, or paths. Keep every placeholder in the text. You may change their order to suit your language. If the placeholders do not match, the tool shows the English text for that entry and logs a warning once. Escape a line break as `\n` and a double quote as `\"`.
- **Where to put the file.** Anywhere under `Assets`. Putting it in a folder named `Editor` guarantees that it is never included in a build.
- **If a file is broken** (invalid JSON, or an empty `languageCode`), it is ignored and a warning that names the file is written to the Console. The other languages keep working.
- **After you update the package**, new texts may have been added. Texts without a translation are shown in English. To find them, export a new template and compare its `key` list with your file.
- Translations are welcome. If you would like to share one so that it can be bundled, please send it through an Issue or a Pull Request on the repository.

---

## 7. Notes

- **Inside a namespace under `MAH`, such as `MAH.MyGame`, `AddressableManager` is interpreted as a namespace (`MAH.AddressableManager`) rather than the class, causing a compile error**. In that case, write the fully qualified name `MAH.AddressableManager.AddressableManager.AccInstance` or use a `using` alias
- **Install UniTask via UPM**. If you put it under `Assets/` with a `.unitypackage`, it is not recognized and the `Awaitable` version is used
- Once Addressables initialization fails, it cannot be retried until the app restarts. Treat an initialization failure as an error that prompts a restart

---

## 8. Samples

### 8.1 Importing

1. Select **AddressableManager** in `Window > Package Manager`
2. In the **Samples** tab (or the **Samples** item in the details pane, depending on the Unity version), press **Import** for the sample you want
3. It is copied to `Assets/Samples/MAH-AddressableManager/<version>/<sample name>/`

| Sample (name shown in the Package Manager) | Folder | Description |
| --- | --- | --- |
| Load and Release | `AddressableLoad` | Check loading and releasing of Prefab, Sprite, Audio, and Label batches, together with the differences between lifetimes (Manual / Scene / Owner) |
| Initialize | `AddressableInitialize` | Check the initialization state transitions and how to receive completion and failure (`await` and events) |
| Download | `AddressableDownload` | Check update checks, getting the download size, and downloading with progress |

The comment at the top of each sample script describes "what you can verify" and "the parts to imitate as an implementation example". Code marked `// [検証用]` (for verification) exists only for testing behavior and is not needed in your implementation.

### 8.2 Setup Before Running

The samples do not include Addressables settings (groups, addresses, Labels). After importing, register the following.

1. **Register addresses and Labels**: In `Window > Asset Management > Addressables > Groups`, put the following assets of the Load and Release sample into any group, **set the address to the file name (without extension)**, and add the Label

   | Folder | Asset (= address) | Label |
   | --- | --- | --- |
   | `Sprites/` | `manual_sprite_load_test` | `Manual` |
   | `Sprites/` | `scene_sprite_load_test` | `Scene` |
   | `Prefabs/` | `manual_object_load_test` | `Manual` |
   | `Prefabs/` | `scene_object_load_test` | `Scene` |
   | `Prefabs/` | `owner_object_load_test` | `Owner` |
   | `Audios/` | `manual_audio_load_test_bgm`, `manual_audio_load_test_se` | `Manual` |
   | `Audios/` | `scene_audio_load_test_bgm`, `scene_audio_load_test_se` | `Scene` |

   The Initialize and Download samples also use the assets registered here (Initialize loads `manual_sprite_load_test`)

2. **Add the scenes to Build Settings**: Add `SpriteLoadScene`, `AudioLoadScene`, `PrefabLoadScene`, and `LabelLoadScene` of the Load and Release sample to the scene list in `File > Build Profiles` (Build Settings). This is needed because the buttons in each scene switch scenes, and switching is also used to verify automatic reclaiming of the Scene lifetime

3. **Choose the Play Mode Script**: Normally, everything works with the Addressables Groups `Play Mode Script` left at **Use Asset Database**. Only if you want to verify actual downloads in the Download sample do you need the setup in [8.5](#85-download)

### 8.3 Load and Release

| Scene | What you can verify |
| --- | --- |
| `SpriteLoadScene` | Loading and displaying Sprites. Different images are loaded for Manual and Scene, so you can tell which remained and which was reclaimed |
| `AudioLoadScene` | Loading and playing AudioClips. `Release` stops only the Manual sound; the Scene sound keeps playing until you switch scenes |
| `PrefabLoadScene` | Loading Prefab assets and `Instantiate`. With the Owner lifetime button, you can also verify that the asset is reclaimed when the owner is `Destroy`ed (body colors: owner = white, Owner lifetime = green, Scene lifetime = light blue, Manual lifetime = yellow) |
| `LabelLoadScene` | Loading by Label (`LoadByLabelAsync`), retrieving by type, and releasing all at once. Behavior when a nonexistent Label is specified (a warning and an empty group ticket) |
| `PrefabSameAddressLoadScene` | Loads the same address with three lifetimes. Verify in the Debug Window's "実行時状態" (Runtime State) tab that the allocations for each lifetime are listed under one entry |

Common operations:

- **Load (Manual)**: Even if pressed twice, the actual load happens once (shared by the reference count)
- **Release**: Returns tickets one at a time. **Releasing the same ticket again** only logs a warning; it does not throw
- **Load (Scene lifetime)**: Does not keep the ticket. Switching to another scene reclaims it automatically and logs a leak warning. A row also appears in the Debug Window's "リーク" (Leak) tab

Useful scripts: `AssetLoadSampleViewBase` (the flow from Load to Release and cleanup in `OnDestroy`), `PrefabLoadSampleView` (handling Prefabs and the Owner lifetime), `LabelLoadSampleView` (loading and retrieving by Label)

### 8.4 Initialize

Open `InitializeScene` and enter Play mode. The Manager is not placed in the scene; instead, buttons create Manager Prefabs with different settings (because the Manager reads its settings only in `Awake`).

| Setting (Prefab) | What you can verify |
| --- | --- |
| Auto | Initialization starts as soon as it is created |
| Manual | Does not initialize until `InitializeAsync` is called. Safe to call any number of times (the second and later calls complete immediately) |
| Manual + PassThrough | A Load before initialization goes through as is (Addressables initializes itself) |
| Manual + WaitForInitialize | A Load while initialization has not started throws `InvalidOperationException` |

- Addressables initialization happens only once per launch. To verify "the first initialization" with another setting, stop Play and start again
- Useful script: `InitializeSampleView` (how to call `InitializeAsync` and subscribe to the success and failure events)

### 8.5 Download

Open `DownloadScene` and enter Play mode. The buttons are disabled until initialization succeeds.

| Button | What you can verify |
| --- | --- |
| Has Update | Whether update data exists (does not update the catalog) |
| Check Update | Checking and updating the catalog, and the number of bundles and total size that need downloading (the values shown in a confirmation dialog) |
| Download | Downloading and its progress (phase, bytes, ratio, retry count). The result delivered last (Completed / Failed / Canceled) |
| Cancel | Canceling a running download |
| Clear Cache | Deleting the downloaded cache (an operation for verification, not a module API) |

- The target Labels are the "Labels" of the sample settings asset `DownloadSettings` (the sample value is `Preset`). Add that Label to the assets you want to download, or change the setting
- **With Use Asset Database there are no bundles, so the number of items to download is always 0**. To verify actual downloads, make the target groups Remote, build them, serve them over an HTTP server, and set the Play Mode Script to **Use Existing Build** (set the Remote Build/Load Path, the `Remote.LoadPath` URL, build with `Build > New Build > Default Build Script`, and serve the `ServerData` folder)
- Bundles downloaded once remain in the cache, so the second and later checks show 0 items. To repeat, press **Clear Cache**
- Useful script: `DownloadSampleView` (how `OnDownloadClicked` calls the API and handles exceptions, and receiving progress in `Report`)
