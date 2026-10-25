using System;
using System.Collections.Generic;
using NUnit.Framework;
using MAH.AddressableManager;
using MAH.AddressableManager.Core.Loading;
using MAH.AddressableManager.Editor.Window;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class LeakLogFormatterTests
    {
        private const string ROOT = "/Users/me/proj";
        private static readonly DateTime EXPORTED_AT = new(2026, 10, 14, 15, 20, 3);
        private static readonly DateTime LAST_TIME = new(2026, 10, 14, 15, 18, 41);

        // 利用者のフレームを含むトレース（モジュール内 → 利用者の順）
        private const string TRACE_WITH_CALLER =
            "  at System.Environment.get_StackTrace () [0x00000] in <4ac0ee785b7843ea944af6fc649f4ee5>:0 \n" +
            "  at MAH.AddressableManager.Core.Loading.LoadingController.LoadAsync[T] (MAH.AddressableManager.AssetKey key) [0x00115] in /Users/me/proj/Assets/@MAH/AddressableManager/Runtime/Core/Loading/Controller/LoadingController.cs:92 \n" +
            "  at MyGame.EnemySpawner.Spawn () [0x00027] in /Users/me/proj/Assets/Scripts/EnemySpawner.cs:74 \n";

        private static LeakSnapshot Ticket(string address, int count, LifetimeKind kind, int scope_value, string scope_name, string trace)
            => new(1, 1, false, AssetKey.FromPath(address), typeof(FakeAsset), string.Empty, 0, kind, scope_value, scope_name, count, LAST_TIME, trace);

        private static LeakSnapshot Group(string label, int group_count, int count, string trace)
            => new(2, 2, true, default, null, label, group_count, LifetimeKind.Owner, 1, "Owner1", count, LAST_TIME, trace);

        private static string Format(List<LeakSnapshot> leaks, List<int> rows, List<int> counts)
            => LeakLogFormatter.Format(EXPORTED_AT, leaks, rows, counts, ROOT);

        // 見出しに、出力日時と、リークの回数・確保元の件数が入る
        [Test]
        public void FormatHeaderHasExportTimeAndTotals()
        {
            var leaks = new List<LeakSnapshot>
            {
                Ticket("a", 10, LifetimeKind.Owner, 1, "O", TRACE_WITH_CALLER),
                Ticket("b", 2, LifetimeKind.Owner, 1, "O", TRACE_WITH_CALLER),
            };

            var text = Format(leaks, new List<int> { 0, 1 }, new List<int> { 10, 2 });

            StringAssert.Contains("出力日時：2026-10-14 15:20:03", text);
            StringAssert.Contains("リーク 12 回（確保元 2 件）", text);
        }

        // 行は、渡した順に並ぶ
        [Test]
        public void FormatRowsFollowGivenOrder()
        {
            var leaks = new List<LeakSnapshot>
            {
                Ticket("first-key", 1, LifetimeKind.Owner, 1, "O", TRACE_WITH_CALLER),
                Ticket("second-key", 1, LifetimeKind.Owner, 1, "O", TRACE_WITH_CALLER),
            };

            var text = Format(leaks, new List<int> { 1, 0 }, new List<int> { 1, 1 });

            Assert.Less(text.IndexOf("second-key", StringComparison.Ordinal), text.IndexOf("first-key", StringComparison.Ordinal));
            StringAssert.Contains("[1] ×1  Path:second-key", text);
            StringAssert.Contains("[2] ×1  Path:first-key", text);
        }

        // 回数は、Snapshot の回数ではなく、渡した値（クリア以降の回数）になる
        [Test]
        public void FormatUsesGivenCountsInsteadOfSnapshotCount()
        {
            var leaks = new List<LeakSnapshot> { Ticket("a", 10, LifetimeKind.Owner, 1, "O", TRACE_WITH_CALLER) };

            var text = Format(leaks, new List<int> { 0 }, new List<int> { 3 });

            StringAssert.Contains("×3", text);
            StringAssert.DoesNotContain("×10", text);
            StringAssert.Contains("リーク 3 回（確保元 1 件）", text);
        }

        // 渡していない行は出力しない（表示中の行だけを出力できる）
        [Test]
        public void FormatSkipsRowsNotGiven()
        {
            var leaks = new List<LeakSnapshot>
            {
                Ticket("hidden-key", 1, LifetimeKind.Owner, 1, "O", TRACE_WITH_CALLER),
                Ticket("shown-key", 1, LifetimeKind.Owner, 1, "O", TRACE_WITH_CALLER),
            };

            var text = Format(leaks, new List<int> { 1 }, new List<int> { 1 });

            StringAssert.Contains("shown-key", text);
            StringAssert.DoesNotContain("hidden-key", text);
        }

        // 単体はキーと型、Label 一括は Label と件数で表す
        [Test]
        public void FormatTicketShowsKeyAndTypeAndGroupShowsLabelAndCount()
        {
            var leaks = new List<LeakSnapshot>
            {
                Ticket("Prefabs/Enemy", 1, LifetimeKind.Owner, 1, "O", TRACE_WITH_CALLER),
                Group("ui", 8, 1, TRACE_WITH_CALLER),
            };

            var text = Format(leaks, new List<int> { 0, 1 }, new List<int> { 1, 1 });

            StringAssert.Contains("Path:Prefabs/Enemy  <FakeAsset>", text);
            StringAssert.Contains("Label「ui」（8件）", text);
        }

        // スコープ名。名前があればそれを、無ければ ID（Owner）・handle（Scene）で示す
        [Test]
        public void BuildScopeNameUsesNameOrFallsBackToIdAndHandle()
        {
            Assert.AreEqual("Owner：Enemy_07（ID 12345）", LeakLogFormatter.BuildScopeName(Ticket("a", 1, LifetimeKind.Owner, 12345, "Enemy_07", null)));
            Assert.AreEqual("Owner：名前なし（ID 12345）", LeakLogFormatter.BuildScopeName(Ticket("a", 1, LifetimeKind.Owner, 12345, string.Empty, null)));
            Assert.AreEqual("Scene：Stage1", LeakLogFormatter.BuildScopeName(Ticket("a", 1, LifetimeKind.Scene, -5136, "Stage1", null)));
            Assert.AreEqual("Scene：名前なし（handle -5136）", LeakLogFormatter.BuildScopeName(Ticket("a", 1, LifetimeKind.Scene, -5136, string.Empty, null)));
        }

        // 寿命・最終発生時刻が書かれる
        [Test]
        public void FormatRowHasScopeAndLastTime()
        {
            var leaks = new List<LeakSnapshot> { Ticket("a", 1, LifetimeKind.Owner, 12345, "Enemy_07", TRACE_WITH_CALLER) };

            var text = Format(leaks, new List<int> { 0 }, new List<int> { 1 });

            StringAssert.Contains("寿命：Owner：Enemy_07（ID 12345）", text);
            StringAssert.Contains("最終発生：15:18:41", text);
        }

        // 呼び出し元は、モジュール内を飛ばした、最初の利用者のフレーム
        [Test]
        public void FormatCallerIsFirstUserFrame()
        {
            var leaks = new List<LeakSnapshot> { Ticket("a", 1, LifetimeKind.Owner, 1, "O", TRACE_WITH_CALLER) };

            var text = Format(leaks, new List<int> { 0 }, new List<int> { 1 });

            StringAssert.Contains("呼び出し元：/Users/me/proj/Assets/Scripts/EnemySpawner.cs:74", text);
        }

        // 利用者のフレームが無い（ファイル情報が無い）ときは「不明」。トレースが空でも例外にならない
        [TestCase("  at A () [0x00000] in <abc>:0 \n")]
        [TestCase("")]
        [TestCase(null)]
        public void FormatCallerIsUnknownWithoutUserFrame(string trace)
        {
            var leaks = new List<LeakSnapshot> { Ticket("a", 1, LifetimeKind.Owner, 1, "O", trace) };

            var text = Format(leaks, new List<int> { 0 }, new List<int> { 1 });

            StringAssert.Contains("呼び出し元：不明", text);
        }

        // スタックトレースは、全文が入る（フレームごとに1行）
        [Test]
        public void FormatIncludesFullStackTrace()
        {
            var leaks = new List<LeakSnapshot> { Ticket("a", 1, LifetimeKind.Owner, 1, "O", TRACE_WITH_CALLER) };

            var text = Format(leaks, new List<int> { 0 }, new List<int> { 1 });

            StringAssert.Contains("スタックトレース：", text);
            StringAssert.Contains("      at System.Environment.get_StackTrace () [0x00000] in <4ac0ee785b7843ea944af6fc649f4ee5>:0", text);
            StringAssert.Contains("      at MyGame.EnemySpawner.Spawn () [0x00027] in /Users/me/proj/Assets/Scripts/EnemySpawner.cs:74", text);
        }

        // 0件のときは、見出しと「リークはありませんでした」
        [Test]
        public void FormatNoRowsSaysNoLeak()
        {
            var text = Format(new List<LeakSnapshot>(), new List<int>(), new List<int>());

            StringAssert.Contains("出力日時：2026-10-14 15:20:03", text);
            StringAssert.Contains("リーク 0 回（確保元 0 件）", text);
            StringAssert.Contains("リークはありませんでした。", text);
        }

        // rows と counts の長さが違うときは、呼び出し側の誤りとして例外にする
        [Test]
        public void FormatMismatchedRowsAndCountsThrowsArgumentException()
        {
            var leaks = new List<LeakSnapshot> { Ticket("a", 1, LifetimeKind.Owner, 1, "O", TRACE_WITH_CALLER) };

            Assert.Throws<ArgumentException>(() => Format(leaks, new List<int> { 0 }, new List<int>()));
        }
    }
}
