# Release Notes

A user-facing summary of the changes in each version of AddressableManager. Newer versions are at the top. For the detailed list of changes, see [CHANGELOG.md](../CHANGELOG.md) (Japanese); for installation, see the [README](../README.md#installation).

## v1.0.0 (2026-10-24)

First release of AddressableManager.

### New Features
- Load and release Addressables with just `LoadAsync<T>` and `Release` through `IAddressableHandler`. Assets you forget to release are reclaimed automatically when their lifetime (Manual / Scene / Owner) ends
- Load everything under a Label at once with `LoadByLabelAsync` (`AssetGroupTicket`)
- Initialization control: automatic or manual start, how a Load called before initialization is handled, and a workaround for stale remote catalogs cached by the OS
- Content download: `HasUpdateAsync`, `CheckUpdateAsync` (update count and size), and `DownloadAsync` by Label with progress, retries, and a catalog update policy
- `AddressablePath` and `AddressableLabel` attributes to pick addresses and Labels from an Inspector dropdown
- Debug Window (`MAH/Addressable/Debug Window`): read-only view of loaded assets, reference counts, lifetimes, leaks (with jump-to-caller and log export), and Owner watching
- Dependency Check (`MAH/Addressable/Dependency Check`): lists assets used by registered assets that are not registered themselves, without entering Play mode or building
- Asset registration: automatic registration by rule assets (`MAH/Addressable/Auto Registration`) and bulk registration of selected folders (right-click `MAH > Register to Addressables...`)
- Editor tools can be displayed in Japanese or English (`Edit > Preferences > MAH Addressable > Language`). You can add other languages, or adjust the bundled translations, with a JSON file (see [Usage: Editor Language](usage.md#66-editor-language))
- Samples: Load and Release, Initialize, and Download

### Compatibility
- Unity: 6000.0 or later (tested with 6000.3.26f1)
- Addressables: 2.9.0 or later (tested with 2.10.3)
- UniTask is optional. When it is installed through UPM, the UniTask version is used; otherwise `Task` is used

---
