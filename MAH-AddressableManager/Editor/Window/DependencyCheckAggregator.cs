using System;
using System.Collections.Generic;

namespace MAH.AddressableManager.Editor.Window
{
    /// <summary>
    /// 走査の入力（DependencyCheckInput）から、一覧の行を作る。Unity 非依存（パスと型名の文字列だけで行う）。
    /// 未登録の子1つにつき1行。参照元は、その子へ届くすべての登録済みの親。
    /// 親が子を直接使っているか、登録済みの別のアセット（経由先）を通して使っているかを区別して、参照元に載せる。
    /// 経由の使用も、グループ数・親の数には数える（Group の分け方を見直すとき、どのグループがその子を使っているかを知りたいため）。
    /// </summary>
    internal static class DependencyCheckAggregator
    {
        internal const string UNKNOWN_TYPE_NAME = "Unknown";

        private const string RESOURCES_SEGMENT = "/Resources/";
        private const string PACKAGES_PREFIX = "Packages/";

        /// <summary> 行を作り直す（result は最初に空にする）。行はパス順（序数比較）に並べる </summary>
        internal static void Aggregate(DependencyCheckInput input, List<DependencyCheckRow> result)
        {
            result.Clear();

            // 子のパス → 参照元
            var referrers_by_child = new Dictionary<string, List<DependencyCheckReferrer>>();
            // 登録済みのアセット → そこから（登録済みのアセットも通り抜けて）届く未登録の子。親をまたいで使い回す
            var reach_cache = new Dictionary<string, List<string>>();

            // 親ごとの作業用。親ごとに Clear() して使い回す
            var direct_children = new HashSet<string>();
            var via_targets = new List<string>();
            var via_target_set = new HashSet<string>();
            var via_by_child = new Dictionary<string, (string first_path, int count)>();
            var visited = new HashSet<string>();
            var stack = new Stack<string>();

            foreach (var parent in input.AccParents)
            {
                direct_children.Clear();
                via_targets.Clear();
                via_target_set.Clear();
                via_by_child.Clear();

                CollectDirectChildren(input, parent.AccPath, direct_children, via_targets, via_target_set, visited, stack);

                // 経由先はパス順に並べる（「最初の1つ」を決まった順で選ぶため）
                via_targets.Sort(StringComparer.Ordinal);
                foreach (var via_target in via_targets)
                {
                    foreach (var child in GetReachableChildren(input, via_target, reach_cache, visited, stack))
                    {
                        if (direct_children.Contains(child)) continue; // 直接の使用があれば、直接として扱う

                        via_by_child.TryGetValue(child, out var via);
                        via_by_child[child] = (via.count == 0 ? via_target : via.first_path, via.count + 1);
                    }
                }

                foreach (var child in direct_children)
                    GetReferrers(referrers_by_child, child).Add(new DependencyCheckReferrer(parent.AccPath, parent.AccGroupName));

                foreach (var pair in via_by_child)
                    GetReferrers(referrers_by_child, pair.Key).Add(
                        new DependencyCheckReferrer(parent.AccPath, parent.AccGroupName, pair.Value.first_path, pair.Value.count));
            }

            foreach (var pair in referrers_by_child)
            {
                var path = pair.Key;
                var referrers = pair.Value;
                referrers.Sort((left, right) => string.CompareOrdinal(left.AccPath, right.AccPath));

                var groups = new HashSet<string>();
                var is_pack_separately = false;
                foreach (var referrer in referrers)
                {
                    groups.Add(referrer.AccGroupName);
                    if (input.AccPackSeparatelyGroups.Contains(referrer.AccGroupName)) is_pack_separately = true;
                }

                input.AccTypeNames.TryGetValue(path, out var type_name);
                input.AccSizes.TryGetValue(path, out var size);

                result.Add(new DependencyCheckRow(
                    path,
                    GetFileName(path),
                    string.IsNullOrEmpty(type_name) ? UNKNOWN_TYPE_NAME : type_name,
                    size,
                    referrers,
                    groups.Count,
                    path.Contains(RESOURCES_SEGMENT, StringComparison.Ordinal),
                    is_pack_separately,
                    IsHiddenByDefault(path)));
            }

            result.Sort((left, right) => string.CompareOrdinal(left.AccPath, right.AccPath));
        }

        // 親から、未登録のアセットだけを通って届く子（直接の使用）と、その途中で最初に出会った登録済みのアセット（経由先）を集める。
        // 未登録のアセットの先に未登録の孫があっても、親と孫の間に登録済みのアセットが無ければ「直接」。
        // 登録済みのアセットに着いたら、そこで止める（その先は経由の使用として、GetReachableChildren で別に集める）
        private static void CollectDirectChildren(
            DependencyCheckInput input, string parent_path, HashSet<string> direct_children,
            List<string> via_targets, HashSet<string> via_target_set, HashSet<string> visited, Stack<string> stack)
        {
            visited.Clear();
            stack.Clear();
            visited.Add(parent_path);
            stack.Push(parent_path);

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (!input.AccDirectDependencies.TryGetValue(node, out var dependencies)) continue;

                foreach (var dependency in dependencies)
                {
                    if (dependency == parent_path) continue;

                    if (input.AccRegisteredPaths.Contains(dependency))
                    {
                        if (via_target_set.Add(dependency)) via_targets.Add(dependency);
                        continue;
                    }

                    direct_children.Add(dependency);
                    if (visited.Add(dependency)) stack.Push(dependency);
                }
            }
        }

        // 登録済みのアセットから、登録済みのアセットも通り抜けて届く、未登録の子の一覧。初めて求めたときだけ辿り、結果を使い回す
        private static List<string> GetReachableChildren(
            DependencyCheckInput input, string start_path, Dictionary<string, List<string>> reach_cache,
            HashSet<string> visited, Stack<string> stack)
        {
            if (reach_cache.TryGetValue(start_path, out var cached)) return cached;

            var children = new List<string>();
            visited.Clear();
            stack.Clear();
            visited.Add(start_path);
            stack.Push(start_path);

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (!input.AccDirectDependencies.TryGetValue(node, out var dependencies)) continue;

                foreach (var dependency in dependencies)
                {
                    if (!visited.Add(dependency)) continue; // 循環する依存でも終わる

                    if (!input.AccRegisteredPaths.Contains(dependency)) children.Add(dependency);
                    stack.Push(dependency);
                }
            }

            reach_cache.Add(start_path, children);
            return children;
        }

        private static List<DependencyCheckReferrer> GetReferrers(
            Dictionary<string, List<DependencyCheckReferrer>> referrers_by_child, string child_path)
        {
            if (!referrers_by_child.TryGetValue(child_path, out var referrers))
            {
                referrers = new List<DependencyCheckReferrer>();
                referrers_by_child.Add(child_path, referrers);
            }
            return referrers;
        }

        // スクリプトはバンドルに入らず、Packages/ は組み込みのシェーダー等で利用者が直せないため、既定で隠す
        private static bool IsHiddenByDefault(string path)
            => path.StartsWith(PACKAGES_PREFIX, StringComparison.Ordinal)
               || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

        private static string GetFileName(string path)
        {
            var index = path.LastIndexOf('/');
            return index < 0 ? path : path.Substring(index + 1);
        }
    }
}
