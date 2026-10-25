using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using MAH.AddressableManager.Editor.Window;
using UnityEngine;
using UnityEngine.TestTools;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class EditorTextFileWriterTests
    {
        private const string DEFAULT_FOLDER = "Logs/Default";
        private static readonly DateTime NOW = new(2026, 10, 15, 14, 30, 0);

        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "EditorTextFileWriterTests_" + Guid.NewGuid().ToString("N")).Replace('\\', '/');
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        // 相対パスはプロジェクトのルートから。先頭の ./ ・末尾の区切りは吸収する
        [TestCase("/Users/me/proj", "Logs/X", "/Users/me/proj/Logs/X")]
        [TestCase("/Users/me/proj/", "./Logs/X/", "/Users/me/proj/Logs/X")]
        [TestCase("C:\\proj", "Logs\\X", "C:/proj/Logs/X")]
        public void ResolveFolderRelativeStartsFromProjectRoot(string root, string output_folder, string expected)
        {
            Assert.AreEqual(expected, EditorTextFileWriter.ResolveFolder(root, output_folder, DEFAULT_FOLDER));
        }

        // 空・空白・null のときは、渡した既定の出力先になる
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void ResolveFolderBlankFallsBackToDefaultFolder(string output_folder)
        {
            Assert.AreEqual("/Users/me/proj/Logs/Default", EditorTextFileWriter.ResolveFolder("/Users/me/proj", output_folder, DEFAULT_FOLDER));
        }

        // 絶対パスは、そのまま（区切りだけ / にそろえる）
        [TestCase("/var/log/x", "/var/log/x")]
        [TestCase("C:\\logs\\x", "C:/logs/x")]
        [TestCase("D:/logs/x/", "D:/logs/x")]
        public void ResolveFolderAbsoluteIsKept(string output_folder, string expected)
        {
            Assert.AreEqual(expected, EditorTextFileWriter.ResolveFolder("/Users/me/proj", output_folder, DEFAULT_FOLDER));
        }

        // ファイル名は、接頭辞＋日時
        [Test]
        public void CreateFileNameUsesPrefixAndTimestamp()
        {
            Assert.AreEqual("AddressableDependencies_20261015_143000.txt", EditorTextFileWriter.CreateFileName("AddressableDependencies_", NOW));
        }

        // フォルダ（入れ子）を作って書く。UTF-8 で、BOM は付かない
        [Test]
        public void WriteTimestampedCreatesFolderAndWritesUtf8WithoutBom()
        {
            var folder = $"{_root}/a/b";

            var path = EditorTextFileWriter.WriteTimestamped("依存\nline2", folder, "P_", NOW, "失敗");

            Assert.AreEqual($"{folder}/P_20261015_143000.txt", path);
            Assert.AreEqual("依存\nline2", File.ReadAllText(path));
            var bytes = File.ReadAllBytes(path);
            Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "BOM は付かないはず");
        }

        // 同じ秒は、上書きせず連番になる
        [Test]
        public void WriteTimestampedSameSecondAddsSequenceNumber()
        {
            var first = EditorTextFileWriter.WriteTimestamped("1", _root, "P_", NOW, "失敗");
            var second = EditorTextFileWriter.WriteTimestamped("2", _root, "P_", NOW, "失敗");
            var third = EditorTextFileWriter.WriteTimestamped("3", _root, "P_", NOW, "失敗");

            Assert.AreEqual($"{_root}/P_20261015_143000.txt", first);
            Assert.AreEqual($"{_root}/P_20261015_143000_2.txt", second);
            Assert.AreEqual($"{_root}/P_20261015_143000_3.txt", third);
            Assert.AreEqual("1", File.ReadAllText(first));
            Assert.AreEqual("2", File.ReadAllText(second));
            Assert.AreEqual("3", File.ReadAllText(third));
        }

        // 書き込めないときは、例外にならず、failure_label を含む警告を出して null
        [Test]
        public void WriteTimestampedFailureReturnsNullWithLabeledWarning()
        {
            Directory.CreateDirectory(_root);
            var file = $"{_root}/blocker";
            File.WriteAllText(file, "I am a file");

            LogAssert.Expect(LogType.Warning, new Regex("書き出せませんでした"));
            Assert.IsNull(EditorTextFileWriter.WriteTimestamped("text", $"{file}/sub", "P_", NOW, "書き出せませんでした"));
        }
    }
}
