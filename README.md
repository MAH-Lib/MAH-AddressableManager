# MAH-AddressableManager

English | [日本語](README_JP.md)

> Last updated: 2026-10-24 / Unity: 6000.3.26f1

A general-purpose module that manages Unity Addressables from loading to releasing in one place.

- If you are considering using it → read "Installation" and "Usage" below
- If you want to contribute → read [CONTRIBUTING.md](CONTRIBUTING.md) (Japanese) first

---

## Features

- Work with Addressables using only `LoadAsync<T>` and `Release`, without worrying about reference counts or handles
- `LoadAsync<T>` always returns **the loaded asset itself** (for `GameObject`, the Prefab asset)
- Explicit release is the main path. Anything you forget to release is reclaimed automatically when the lifetime specified at Load (Manual / Scene / Owner) ends
- Initialization can be set to automatic (on Manager startup) or manual. Completion and failure can be received both via `await` and via events
- Initialization, download, loading, and releasing are all done from a single `IAddressableHandler` (`AddressableManager.AccInstance.AccHandler`)
- Check for and download content updates per Label. Includes getting the "count and size" for a confirmation dialog, retries on failure, and progress notifications
- Load assets of different types together by specifying a Label, and release them together
- The `[AddressablePath]` / `[AddressableLabel]` attributes let you pick addresses and Labels from a dropdown in the Inspector (preventing typos from manual input)
- Asynchronous code is built around UniTask (installing it via UPM switches to the UniTask version automatically). Projects without UniTask run on `Awaitable`
- No dependency on external libraries such as DI containers (VContainer, etc.) or R3
- Includes a debug window for checking loading state, leaks, and Owner reclaiming, and a window that shows where and how much assets not registered in Addressables are used, without entering Play mode or building ([Editor Tools](#editor-tools))

## Requirements

| Item | Version |
| --- | --- |
| Unity | Tested with 6000.3.26f1 |
| Addressables | Tested with 2.10.3 |
| UniTask (optional, recommended) | 2.5.0 or later. **Must be installed via UPM** (if installed with a `.unitypackage`, it is not recognized and the Awaitable version is used) |

## Installation

Install with the Unity Package Manager (UPM) from the GitHub git URL. The package name is `com.github.mah-lib.addressable-manager`.

If you use UniTask, **install UniTask via UPM first**. If it is already installed, this module automatically uses the UniTask version.

### Install from the Package Manager

1. Copy the following URL

   ```
   https://github.com/MAH-Lib/MAH-AddressableManager.git?path=MAH-AddressableManager
   ```

2. Open `Window > Package Manager`, then choose **Add package from git URL...** from the **+** button at the top left
3. Paste the copied URL into the field and press **Install** (**Add**)

The latest version on the default branch (`main`) is installed.

### Install by editing manifest.json (pinning a version)

Add the following to `dependencies` in your project's `Packages/manifest.json`. It is fetched automatically when you return to Unity.

```json
{
  "dependencies": {
    "com.github.mah-lib.addressable-manager": "https://github.com/MAH-Lib/MAH-AddressableManager.git?path=MAH-AddressableManager#vX.Y.Z"
  }
}
```

### About versions

- Writing the **release tag name** (`v` + version number) after the `#` at the end of the URL in manifest.json pins that version
- Replace `vX.Y.Z` in the example with the tag name of the version you want to install (see the [release notes](Docs/release-notes.md) for published versions)
- To upgrade, rewrite the part after `#` with the new tag name
- Add the line inside your existing `dependencies` (the example above shows only the line to add)
- For published versions and their changes, see the [release notes](Docs/release-notes.md) and [CHANGELOG.md](CHANGELOG.md) (Japanese)

## Usage

Code examples use the UniTask version. In projects without UniTask, the return type is `Awaitable` instead of `UniTask` (the way you call it is the same).

### 1. Create the settings asset

In the Project window, choose `Create > MAH > Addressable > RuntimeSettings` to create the settings asset. Configure how initialization starts, the Labels to download, the number of retries, and so on here (see [Settings Asset](Docs/usage.md#1-settings-asset) for each field).

### 2. Place the Manager in a scene

Add the `AddressableManager` component to a GameObject in your startup scene, and assign the settings asset from step 1 in the Inspector.

- The Manager stays resident with `DontDestroyOnLoad` and survives scene changes. Any second or later Manager that starts is destroyed automatically
- It starts before your scripts' `Awake`, so you can reference `AddressableManager.AccInstance` from `Awake`
- If no settings asset is assigned, a warning is logged and the defaults are used

### 3. Load, and release when done

```csharp
using System;
using MAH.AddressableManager;
using MAH.AddressableManager.Attribute;
using UnityEngine;

public sealed class EnemyView : MonoBehaviour
{
    // Pick the address from the dropdown in the Inspector
    [SerializeField, AddressablePath] private string _spriteAddress;

    private AssetTicket<Sprite> _ticket;
    private bool _hasTicket;

    private async void Start()
    {
        var handler = AddressableManager.AccInstance.AccHandler;
        try
        {
            // Returns the loaded asset itself and a ticket for releasing it
            _ticket = await handler.LoadAsync<Sprite>(
                AssetKey.FromPath(_spriteAddress), AssetLifetime.Manual, destroyCancellationToken);
            _hasTicket = true;
            GetComponent<SpriteRenderer>().sprite = _ticket.AccAsset;
        }
        catch (OperationCanceledException)
        {
            // Destroyed while loading. No ticket is issued, so no release is needed
        }
    }

    private void OnDestroy()
    {
        // When done, return the ticket to release it (explicit release is the norm)
        if (_hasTicket) AddressableManager.AccInstance?.AccHandler.Release(_ticket);
    }
}
```

- `LoadAsync<GameObject>` returns the Prefab asset. Instantiate it yourself with `Instantiate`
- If you specify `Scene()` or `Owner(gameObject)` as the lifetime (`AssetLifetime`), anything you forget to release is reclaimed automatically when the scene is unloaded or the GameObject is destroyed. Automatic reclaiming is a safety net against forgotten releases; normally, call `Release`

### More details

All fields of the settings asset, and how to use the initialization, content update (download), loading/releasing, and attribute APIs, are covered in [Usage (API Reference)](Docs/usage.md).

## Samples

Select MAH-AddressableManager in the Package Manager and press **Import** under **Samples** to copy them under `Assets/Samples/MAH-AddressableManager/`. The samples do not include Addressables settings, so before running them you need to register addresses and Labels and add the scenes to Build Settings ([setup steps](Docs/usage.md#82-setup-before-running)).

| Sample | Description |
| --- | --- |
| Load and Release | Check loading and releasing of Prefab, Sprite, Audio, and Label batches, together with the differences between lifetimes (Manual / Scene / Owner). Switching scenes also lets you verify automatic reclaiming of the Scene lifetime and leak warnings |
| Initialize | Check automatic / manual initialization, how a Load before initialization is handled (PassThrough / WaitForInitialize), and how to receive completion and failure (`await` and events) |
| Download | Check determining whether update data exists, getting the count and size, downloading with progress, and canceling (actual downloads require a remote hosting environment) |

For the scenes, operations, and what each sample lets you verify, see [Usage: Samples](Docs/usage.md#8-samples).

## Editor Tools

None of them are included in builds. The Debug Window and Dependency Check are read-only; only the two asset registration tools write to the Addressables settings, and only as you instruct them. The UI of the tools can be displayed in Japanese or English (`Edit > Preferences > MAH Addressable > Language`), and you can add other languages with a JSON file (see [Usage: Editor Language](Docs/usage.md#66-editor-language)).

| Tool | How to open | Description |
| --- | --- | --- |
| Debug Window | `MAH/Addressable/Debug Window` | Check the state during Play with tabs. "実行時状態" (Runtime State): loaded assets, reference counts, allocation counts per lifetime, and the stack trace of where each was allocated / "リーク" (Leak): aggregates what was reclaimed automatically without being `Release`d by allocation source, with jumping to code and text export / "Owner": the watch state of the Owner lifetime and the route of reclaiming |
| Dependency Check | `MAH/Addressable/Dependency Check` | Lists unregistered assets used by registered assets, without entering Play mode or building. Highlights those used from multiple groups, for reviewing Groups |
| Auto Registration | `MAH/Addressable/Auto Registration`, and rule assets from `Create > MAH > Addressable > AutoRegistrationRule` | Registers assets in Addressables automatically on import, following rules you write (target folder, extensions, address, group, Labels). Nothing happens until you create a rule. See [Usage: Auto Registration](Docs/usage.md#63-auto-registration) |
| Register to Addressables | Right-click in the Project window, then `MAH > Register to Addressables...` | Lists everything under the selected folders, narrows it down by extension and checkboxes, and registers it into a group in one go. See [Usage: Register to Addressables](Docs/usage.md#64-register-to-addressables-manual) |
| `[AddressablePath]` / `[AddressableLabel]` | Attributes added to string fields | Pick addresses and Labels from a dropdown in the Inspector |
| Editor Settings | `Create > MAH > Addressable > EditorSettings` (if none exists, it is created automatically at `Assets/MAH.Data/AddressableManager/Editor/` on startup) | Refresh interval of the debug window, output folders for leak logs and Dependency Check, automatic leak export when Play ends, and the registration defaults |
| Language | `Edit > Preferences > MAH Addressable`, and `MAH/Addressable/Localization/Export Translation Template` | Switches the display language of the tools between Japanese and English (a personal setting). Add or customize a language by exporting a template and translating it. See [Usage: Editor Language](Docs/usage.md#66-editor-language) |

For details, see [Usage: Editor Tools](Docs/usage.md#6-editor-tools).

## Documentation

- For users
  - [Usage (API reference, Editor tools, samples)](Docs/usage.md)
  - [Release notes](Docs/release-notes.md)
  - [Glossary](Docs/glossary.md) (Japanese; the English term for each entry is listed)
- For developers (Japanese)
  - [Design specification](src/AddressableManager/Docs/addressable-manager-design.md)
  - [Coding conventions](src/AddressableManager/Docs/conventions.md)
  - For detailed specifications of each feature, see [`src/AddressableManager/Docs/specs/`](src/AddressableManager/Docs/specs/)

## For Contributors

Branching, pull requests, CHANGELOG, and release procedures are described in [CONTRIBUTING.md](CONTRIBUTING.md) (Japanese). Be sure to read it before you start.

- Open `src/AddressableManager/` in Unity. The repository root is not a Unity project
- Development happens in `src/AddressableManager/Assets/@MAH/MAH-AddressableManager/`. The root `MAH-AddressableManager/` is **for distribution** and is copied from the development folder at release time (do not edit it directly)
- Before proposing design changes, check section 5.3 "Designs intentionally not adopted" of the [design specification](src/AddressableManager/Docs/addressable-manager-design.md)

## Changelog

See [CHANGELOG.md](CHANGELOG.md) (Japanese) and the [release notes](Docs/release-notes.md).
