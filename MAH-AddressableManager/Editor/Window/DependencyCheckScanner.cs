using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.U2D;
using UnityEngine.U2D;

namespace MAH.AddressableManager.Editor.Window
{
    /// <summary>
    /// AddressableAssetSettings・AssetDatabase・SpriteAtlas を読み、DependencyCheckInput を作る（Unity に依存する部分をここに閉じる）。
    /// 親1つごとに、キャンセルできる進捗バーを更新する。進捗バーは必ず消す。
    /// 読み取り専用。グループ・エントリ・アセットを書き換える API を呼ばない。
    /// </summary>
    internal static class DependencyCheckScanner
    {
        internal enum ScanResult
        {
            /// <summary> 最後まで走査した </summary>
            Completed,
            /// <summary> 進捗バーのキャンセルで打ち切った </summary>
            Canceled,
            /// <summary> Addressables の設定（AddressableAssetSettings）が無い </summary>
            NoSettings,
        }

        private const string PROGRESS_TITLE = "Addressable Dependency Check";
        private const string SPRITE_ATLAS_EXTENSION = ".spriteatlas";
        private const string PACKAGES_PREFIX = "Packages/";

        /// <summary> 走査する。Completed のときだけ input に結果が入る（それ以外は null） </summary>
        internal static ScanResult TryScan(out DependencyCheckInput input)
        {
            input = null;
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return ScanResult.NoSettings;

            var result = new DependencyCheckInput();
            try
            {
                CollectParents(settings, result);
                AddSpriteAtlasPackables(result);
                if (!CollectDependencies(result)) return ScanResult.Canceled;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            input = result;
            return ScanResult.Completed;
        }

        // 全グループの登録済みアセット（フォルダーエントリの配下を含む）を親にし、そのパスを「登録済み」にする
        private static void CollectParents(AddressableAssetSettings settings, DependencyCheckInput input)
        {
            var entries = new List<AddressableAssetEntry>();
            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                if (group.HasSchema<PlayerDataGroupSchema>()) continue; // Built In Data は Addressables のバンドルに入らない

                var bundled = group.GetSchema<BundledAssetGroupSchema>();
                if (bundled != null && bundled.BundleMode == BundledAssetGroupSchema.BundlePackingMode.PackSeparately)
                    input.AccPackSeparatelyGroups.Add(group.Name);

                entries.Clear();
                group.GatherAllAssets(entries, true, true, false);
                foreach (var entry in entries)
                {
                    var path = entry.AssetPath;
                    if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) continue; // フォルダー自身は親にしない
                    if (!input.AccRegisteredPaths.Add(path)) continue;

                    input.AccParents.Add(new DependencyCheckInput.Parent(path, group.Name));
                }
            }
        }

        // 登録済みの SpriteAtlas の packables（Texture・Sprite・フォルダー配下の Texture）を「登録済み」に加える。
        // 全グループ分を先に集めてから依存を辿るため、atlas と、Sprite を参照する親が別グループでも判定できる
        private static void AddSpriteAtlasPackables(DependencyCheckInput input)
        {
            foreach (var parent in input.AccParents)
            {
                if (!parent.AccPath.EndsWith(SPRITE_ATLAS_EXTENSION, System.StringComparison.OrdinalIgnoreCase)) continue;

                var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(parent.AccPath);
                if (atlas == null) continue;

                foreach (var packable in atlas.GetPackables())
                {
                    var path = AssetDatabase.GetAssetPath(packable);
                    if (string.IsNullOrEmpty(path)) continue;

                    if (AssetDatabase.IsValidFolder(path))
                    {
                        foreach (var guid in AssetDatabase.FindAssets("t:Texture", new[] { path }))
                            input.AccRegisteredPaths.Add(AssetDatabase.GUIDToAssetPath(guid));
                    }
                    else
                    {
                        input.AccRegisteredPaths.Add(path);
                    }
                }
            }
        }

        // 親ごとに、そこから届くすべてのアセットの「直接の依存」を1段ずつ集める（幅優先）。キャンセルされたら false。
        // 登録済みのアセットを経由した使用かどうかは、集約（DependencyCheckAggregator）がこの表から判定する。
        // 同じアセットの依存は1回しか読まない（AccDirectDependencies に入っていれば読み済み）。
        // 未登録の子の型名とサイズは、初めて出てきたときだけ取る
        private static bool CollectDependencies(DependencyCheckInput input)
        {
            var project_root = EditorProjectPath.GetProjectRoot();
            var queue = new Queue<string>();
            var count = input.AccParents.Count;
            for (var i = 0; i < count; i++)
            {
                var parent = input.AccParents[i];
                if (EditorUtility.DisplayCancelableProgressBar(PROGRESS_TITLE, parent.AccPath, (float)i / count))
                    return false;

                queue.Enqueue(parent.AccPath);
                while (queue.Count > 0)
                {
                    var path = queue.Dequeue();
                    if (input.AccDirectDependencies.ContainsKey(path)) continue;

                    var dependencies = new List<string>();
                    foreach (var dependency in AssetDatabase.GetDependencies(path, false))
                    {
                        if (dependency == path) continue; // 戻り値に自分自身が含まれる版があるため、常に除く

                        dependencies.Add(dependency);
                        RegisterUnregisteredChild(input, project_root, dependency);
                        queue.Enqueue(dependency);
                    }
                    input.AccDirectDependencies.Add(path, dependencies);
                }
            }
            return true;
        }

        // 未登録の子の型名とサイズを、初めて出てきたときだけ取る
        private static void RegisterUnregisteredChild(DependencyCheckInput input, string project_root, string path)
        {
            if (input.AccRegisteredPaths.Contains(path) || input.AccTypeNames.ContainsKey(path)) return;

            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            input.AccTypeNames[path] = type != null ? type.Name : DependencyCheckAggregator.UNKNOWN_TYPE_NAME;
            input.AccSizes[path] = GetFileSize(project_root, path);
        }

        // ディスク上のファイルサイズ。取得できなければ 0（走査全体を止めないため、IO・権限の例外は握る）
        private static long GetFileSize(string project_root, string asset_path)
        {
            try
            {
                var info = new FileInfo(ResolvePhysicalPath(project_root, asset_path));
                return info.Exists ? info.Length : 0;
            }
            catch (System.Exception)
            {
                return 0;
            }
        }

        // アセットのパスを、ディスク上のパスにする。Packages/ 配下は、レジストリから入れたものの実体が
        // Library/PackageCache にあるため、プロジェクトのルートからの相対では解決できない。パッケージ情報から実体の場所を引く
        private static string ResolvePhysicalPath(string project_root, string asset_path)
        {
            if (asset_path.StartsWith(PACKAGES_PREFIX, System.StringComparison.Ordinal))
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(asset_path);
                if (package != null) return package.resolvedPath + asset_path.Substring(package.assetPath.Length);
            }
            return $"{project_root}/{asset_path}";
        }
    }
}
