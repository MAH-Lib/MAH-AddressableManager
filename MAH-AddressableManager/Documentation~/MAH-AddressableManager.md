# MAH-AddressableManager

A module that manages Unity Addressables from loading to releasing in one place. Work with just `LoadAsync<T>` / `Release`; anything you forget to release is reclaimed automatically when its lifetime (Manual / Scene / Owner) ends.

This page is a short offline summary. The full usage guide and the latest information are on GitHub: https://github.com/MAH-Lib/MAH-AddressableManager

## Requirements

| Item | Version |
| --- | --- |
| Unity | 6000.0 or later (tested with 6000.3.26f1) |
| Addressables | 2.9.0 or later (tested with 2.10.3) |
| UniTask (optional) | Install it via UPM to use the UniTask version. Without it, `Task` is used |

## Quick start

1. Create the settings asset with `Create > MAH > Addressable > RuntimeSettings`
2. Add the `AddressableManager` component to a GameObject in your startup scene and assign the settings asset
3. Load and release through `AddressableManager.AccInstance.AccHandler`

```csharp
var handler = AddressableManager.AccInstance.AccHandler;

// Returns the loaded asset itself and a ticket for releasing it
var ticket = await handler.LoadAsync<Sprite>(AssetKey.FromPath("Sprites/Enemy"), AssetLifetime.Manual);

// When done, return the ticket
handler.Release(ticket);
```

- `LoadAsync<GameObject>` returns the Prefab asset. Instantiate it yourself
- With `AssetLifetime.Scene()` or `AssetLifetime.Owner(gameObject)`, forgotten releases are reclaimed when the scene is unloaded or the GameObject is destroyed

## Features

- Initialization (automatic or manual), content update check and download by Label, and loading by Label
- `[AddressablePath]` / `[AddressableLabel]` attributes to pick addresses and Labels from an Inspector dropdown
- Editor tools (`MAH/Addressable/...`): Debug Window, Dependency Check, Auto Registration, and Register to Addressables
- Editor tool UI in Japanese or English (`Edit > Preferences > MAH Addressable > Language`)

## Samples

Open this package in the Package Manager and press **Import** under **Samples**: Load and Release, Initialize, and Download.

## Links

- Usage (API reference): https://github.com/MAH-Lib/MAH-AddressableManager/blob/main/Docs/usage.md
- Release notes: https://github.com/MAH-Lib/MAH-AddressableManager/blob/main/Docs/release-notes.md
- Changelog (Japanese): https://github.com/MAH-Lib/MAH-AddressableManager/blob/main/CHANGELOG.md
