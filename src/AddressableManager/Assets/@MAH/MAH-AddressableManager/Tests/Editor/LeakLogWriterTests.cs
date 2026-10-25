using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using MAH.AddressableManager.Editor.Window;
using UnityEngine;
using UnityEngine.TestTools;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class LeakLogWriterTests
    {
        private static readonly DateTime NOW = new(2026, 10, 14, 15, 20, 3);

        private string _root; // テストごとに、一意の一時フォルダを使う（終了時に削除する）

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "AddressableLeakLogWriterTests_" + Guid.NewGuid().ToString("N")).Replace('\\', '/');
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        // 相対パスは、プロジェクトのルートから。先頭の ./ ・末尾の区切り・ルート側の末尾の区切りは吸収する
        [TestCase("/Users/me/proj", "Logs/AddressableManager", "/Users/me/proj/Logs/AddressableManager")]
        [TestCase("/Users/me/proj/", "./Logs/AddressableManager/", "/Users/me/proj/Logs/AddressableManager")]
        [TestCase("C:\\Users\\me\\proj", "Logs\\AddressableManager", "C:/Users/me/proj/Logs/AddressableManager")]
        public void ResolveFolderRelativeStartsFromProjectRoot(string root, string output_folder, string expected)
        {
            Assert.AreEqual(expected, LeakLogWriter.ResolveFolder(root, output_folder));
        }

        // 出力先が null・空白のときは、既定の出力先にする（設定の補正を通らない経路への備え）
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void ResolveFolderBlankFallsBackToDefault(string output_folder)
        {
            Assert.AreEqual("/Users/me/proj/Logs/AddressableManager", LeakLogWriter.ResolveFolder("/Users/me/proj", output_folder));
        }

        // 絶対パス（/ 始まり・Windows のドライブレター）は、そのまま（区切りだけ / にそろえる）
        [TestCase("/var/log/leaks", "/var/log/leaks")]
        [TestCase("C:\\logs\\leaks", "C:/logs/leaks")]
        [TestCase("D:/logs/leaks/", "D:/logs/leaks")]
        public void ResolveFolderAbsoluteIsKept(string output_folder, string expected)
        {
            Assert.AreEqual(expected, LeakLogWriter.ResolveFolder("/Users/me/proj", output_folder));
        }

        // 手動のファイル名の書式
        [Test]
        public void CreateFileNameUsesTimestamp()
        {
            Assert.AreEqual("AddressableLeaks_20261014_152003.txt", LeakLogWriter.CreateFileName(NOW));
        }

        // 手動の出力は、フォルダ（入れ子）を作って、日時付きのファイルに書く。UTF-8 で、BOM は付かない
        [Test]
        public void WriteManualCreatesFolderAndWritesUtf8WithoutBom()
        {
            var folder = $"{_root}/Logs/AddressableManager";

            var path = LeakLogWriter.WriteManual("リークのログ\nline2", folder, NOW);

            Assert.AreEqual($"{folder}/AddressableLeaks_20261014_152003.txt", path);
            Assert.AreEqual("リークのログ\nline2", File.ReadAllText(path));
            var bytes = File.ReadAllBytes(path);
            Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "BOM は付かないはず");
        }

        // 同じ秒に続けて出力しても、上書きせず、連番を付けた別のファイルになる
        [Test]
        public void WriteManualSameSecondAddsSequenceNumber()
        {
            var first = LeakLogWriter.WriteManual("first", _root, NOW);
            var second = LeakLogWriter.WriteManual("second", _root, NOW);
            var third = LeakLogWriter.WriteManual("third", _root, NOW);

            Assert.AreEqual($"{_root}/AddressableLeaks_20261014_152003.txt", first);
            Assert.AreEqual($"{_root}/AddressableLeaks_20261014_152003_2.txt", second);
            Assert.AreEqual($"{_root}/AddressableLeaks_20261014_152003_3.txt", third);
            Assert.AreEqual("first", File.ReadAllText(first));
            Assert.AreEqual("second", File.ReadAllText(second));
            Assert.AreEqual("third", File.ReadAllText(third));
        }

        // 自動出力の1回目は、LastPlay だけができる
        [Test]
        public void WriteLastPlayFirstTimeCreatesLastPlayOnly()
        {
            var path = LeakLogWriter.WriteLastPlay("play1", _root);

            Assert.AreEqual($"{_root}/{LeakLogWriter.LAST_PLAY_FILE_NAME}", path);
            Assert.AreEqual("play1", File.ReadAllText(path));
            Assert.IsFalse(File.Exists($"{_root}/{LeakLogWriter.LAST_PLAY_PREV_FILE_NAME}"));
        }

        // 2回目で、前回分が -prev に移り、LastPlay は今回の内容になる。3回目で、最初の内容は消える（1世代だけ残る）
        [Test]
        public void WriteLastPlayKeepsOnlyOneGeneration()
        {
            var last_path = $"{_root}/{LeakLogWriter.LAST_PLAY_FILE_NAME}";
            var prev_path = $"{_root}/{LeakLogWriter.LAST_PLAY_PREV_FILE_NAME}";

            LeakLogWriter.WriteLastPlay("play1", _root);
            LeakLogWriter.WriteLastPlay("play2", _root);
            Assert.AreEqual("play2", File.ReadAllText(last_path));
            Assert.AreEqual("play1", File.ReadAllText(prev_path));

            LeakLogWriter.WriteLastPlay("play3", _root);
            Assert.AreEqual("play3", File.ReadAllText(last_path));
            Assert.AreEqual("play2", File.ReadAllText(prev_path), "最初の内容（play1）は消え、直前の内容（play2）だけが -prev に残るはず");
        }

        // 自動出力が触るのは、固定名の2ファイルだけ。手動のログと、他のファイルは消えない
        [Test]
        public void WriteLastPlayDoesNotTouchManualLogsOrOtherFiles()
        {
            var manual = LeakLogWriter.WriteManual("manual", _root, NOW);
            var other = $"{_root}/other.txt";
            File.WriteAllText(other, "other");

            for (var i = 0; i < 3; i++)
                LeakLogWriter.WriteLastPlay($"play{i}", _root);

            Assert.AreEqual("manual", File.ReadAllText(manual));
            Assert.AreEqual("other", File.ReadAllText(other));
        }

        // 書き込めないとき（フォルダの親がファイル）は、例外にならず、警告を出して null を返す
        [Test]
        public void WriteFailureReturnsNullWithWarningInsteadOfThrowing()
        {
            Directory.CreateDirectory(_root);
            var file = $"{_root}/blocker";
            File.WriteAllText(file, "I am a file");
            var invalid_folder = $"{file}/sub"; // ファイルの下にはフォルダを作れない

            LogAssert.Expect(LogType.Warning, new Regex("リークのログを書き出せませんでした"));
            Assert.IsNull(LeakLogWriter.WriteManual("text", invalid_folder, NOW));

            LogAssert.Expect(LogType.Warning, new Regex("リークのログを書き出せませんでした"));
            Assert.IsNull(LeakLogWriter.WriteLastPlay("text", invalid_folder));
        }
    }
}
