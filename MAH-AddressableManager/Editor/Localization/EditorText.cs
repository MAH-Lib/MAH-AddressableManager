using System;
using System.Collections.Generic;
using UnityEngine;

namespace MAH.AddressableManager.Editor.Localization
{
    /// <summary>
    /// Editor ツールの文言の窓口。キーから、現在の表示言語の文言を返す。
    /// キーが無い言語は英語、英語にも無ければキー文字列をそのまま返す（置き換えの漏れを見つけやすくするため）。
    /// 初回の呼び出しで言語ファイルを読み込む。Get は Dictionary を引くだけでアロケーションしない。
    /// 言語が変わったとき・言語ファイルが追加／削除されたときは LanguageChanged を発行する（購読は OnEnable / OnDisable で対にする）。
    /// 基盤自身の警告は、翻訳に頼れないため英語で出す。
    /// </summary>
    internal static class EditorText
    {
        private const string LOG_PREFIX = "[AddressableManager] ";

        private static EditorLanguageTable _table;
        private static string _currentLanguageCode;
        private static bool _isFixedForTest; // テストが表と言語を差し替えている間は、アセットの変更による読み直しを見送る
        private static readonly HashSet<string> WarnedKeys = new();
        private static readonly List<EditorLanguageSource> SourceBuffer = new();

        /// <summary> 表示言語が変わった、または言語ファイルが読み直された </summary>
        internal static event Action LanguageChanged;

        internal static string AccCurrentLanguageCode
        {
            get
            {
                EnsureLoaded();
                return _currentLanguageCode;
            }
        }

        /// <summary> 選べる言語コード（読み込めた言語ファイルの分） </summary>
        internal static IReadOnlyList<string> AccLanguageCodes
        {
            get
            {
                EnsureLoaded();
                return _table.AccLanguageCodes;
            }
        }

        internal static string GetLanguageName(string language_code)
        {
            EnsureLoaded();
            return _table.GetLanguageName(language_code);
        }

        /// <summary>
        /// キーの文言を探す。無ければ false（警告は出さない）。Inspector のように、キーが無いことが正常な場合に使う
        /// （見出し・ツールチップは、書いたものだけが出る）
        /// </summary>
        internal static bool TryGet(string key, out string text)
        {
            EnsureLoaded();
            return _table.TryGet(_currentLanguageCode, key, out text);
        }

        /// <summary> キーの文言を返す。見つからなければキー文字列 </summary>
        internal static string Get(string key)
        {
            EnsureLoaded();
            if (_table.TryGet(_currentLanguageCode, key, out var text)) return text;

            WarnMissingKey(key);
            return key;
        }

        /// <summary>
        /// キーの文言に引数を埋めて返す。利用者の言語ファイルで引数の数がずれていて string.Format が失敗したときは、
        /// 英語の文言で埋め直し（それも失敗したらキー文字列を返し）、警告を1回だけ出す
        /// </summary>
        internal static string Format(string key, params object[] args)
        {
            var text = Get(key);
            try
            {
                return string.Format(text, args);
            }
            catch (FormatException)
            {
                return FormatFallback(key, args);
            }
        }

        // 引数が3個までの Format。引数の配列（params）を確保しないよう、行ごとに呼ばれる箇所向けに用意する
        // （失敗したときのフォールバックでだけ、配列を作る）
        internal static string Format(string key, object arg0)
        {
            var text = Get(key);
            try
            {
                return string.Format(text, arg0);
            }
            catch (FormatException)
            {
                return FormatFallback(key, new[] { arg0 });
            }
        }

        internal static string Format(string key, object arg0, object arg1)
        {
            var text = Get(key);
            try
            {
                return string.Format(text, arg0, arg1);
            }
            catch (FormatException)
            {
                return FormatFallback(key, new[] { arg0, arg1 });
            }
        }

        internal static string Format(string key, object arg0, object arg1, object arg2)
        {
            var text = Get(key);
            try
            {
                return string.Format(text, arg0, arg1, arg2);
            }
            catch (FormatException)
            {
                return FormatFallback(key, new[] { arg0, arg1, arg2 });
            }
        }

        // 引数の数がずれた言語ファイルのとき：警告を1回出し、英語の文言で埋め直す（それも失敗したらキー文字列）
        private static string FormatFallback(string key, object[] args)
        {
            WarnOnce("format:" + key, $"The text for \"{key}\" in language \"{_currentLanguageCode}\" does not match its arguments. Falling back to English.");
            if (!_table.TryGet(EditorLanguageTable.FALLBACK_LANGUAGE_CODE, key, out var fallback)) return key;

            try
            {
                return string.Format(fallback, args);
            }
            catch (FormatException)
            {
                return key; // 英語の文言も引数と合わないときは、置き換えの漏れとして見つけやすいキー文字列を返す
            }
        }

        /// <summary> 表示言語を切り替えて、EditorPrefs に保存する。選べない言語コードのときは英語になる </summary>
        internal static void SetLanguage(string language_code)
        {
            EnsureLoaded();
            EditorLanguagePreferences.SaveLanguageCode(language_code);
            var resolved = ResolveLanguageCode(language_code);
            if (resolved == _currentLanguageCode) return;

            _currentLanguageCode = resolved;
            LanguageChanged?.Invoke();
        }

        /// <summary> 言語ファイルを読み直す。ファイルの追加・削除・変更のとき（EditorLanguagePostprocessor）に呼ぶ </summary>
        internal static void Reload()
        {
            _table = BuildTable();
            _currentLanguageCode = ResolveLanguageCode(EditorLanguagePreferences.LoadLanguageCode());
            WarnedKeys.Clear();
            LanguageChanged?.Invoke();
        }

        /// <summary>
        /// 言語ファイルのアセットの変更を受けた読み直し（EditorLanguagePostprocessor が、Refresh の完了後に呼ぶ）。
        /// 遅れて呼ばれるため、テストが言語を固定している間は見送る（テストの途中で、固定した言語が上書きされないようにする）
        /// </summary>
        internal static void ReloadForAssetChange()
        {
            if (_isFixedForTest) return;
            Reload();
        }

        /// <summary> 翻訳の雛形の JSON を作る（英語の全キーを入れる） </summary>
        internal static string BuildTemplateJson(string new_language_code, string new_language_name)
        {
            EnsureLoaded();
            return _table.BuildTemplateJson(EditorLanguageTable.FALLBACK_LANGUAGE_CODE, new_language_code, new_language_name);
        }

        /// <summary> テスト用：表と言語を差し替える（EditorPrefs・ファイルには触れない） </summary>
        internal static void SetTableForTest(EditorLanguageTable table, string language_code)
        {
            _table = table;
            _currentLanguageCode = language_code;
            _isFixedForTest = true;
            WarnedKeys.Clear();
        }

        /// <summary>
        /// テスト用：同梱の言語ファイルだけを読み、指定した言語に固定する（EditorPrefs・利用者のファイルに左右されない）。
        /// 既存のテストの、文言の期待値を固定するのに使う。TearDown で ResetForTest を呼ぶ
        /// </summary>
        internal static void UseBundledLanguageForTest(string language_code)
        {
            var table = new EditorLanguageTable();
            EditorLanguageFileLocator.Collect(SourceBuffer);
            foreach (var source in SourceBuffer)
            {
                if (source.AccIsBundled && EditorLanguageTable.TryParse(source.AccJson, out var file, out _))
                    table.Add(file);
            }
            SourceBuffer.Clear();
            SetTableForTest(table, language_code);
        }

        /// <summary> テスト用：差し替えを戻す。次の呼び出しで、ファイルから読み込み直す </summary>
        internal static void ResetForTest()
        {
            _table = null;
            _currentLanguageCode = null;
            _isFixedForTest = false;
            WarnedKeys.Clear();
        }

        private static void EnsureLoaded()
        {
            if (_table != null) return;

            _table = BuildTable();
            _currentLanguageCode = ResolveLanguageCode(EditorLanguagePreferences.LoadLanguageCode());
        }

        private static EditorLanguageTable BuildTable()
        {
            var table = new EditorLanguageTable();
            EditorLanguageFileLocator.Collect(SourceBuffer);

            foreach (var source in SourceBuffer)
            {
                if (EditorLanguageTable.TryParse(source.AccJson, out var file, out var error))
                    table.Add(file);
                else
                    Debug.LogWarning($"{LOG_PREFIX}Ignored the language file {source.AccPath}: {error}");
            }
            SourceBuffer.Clear();
            return table;
        }

        // 保存された言語コード（未設定なら初期値）が選べるならそれ、選べなければ英語
        private static string ResolveLanguageCode(string saved_language_code)
        {
            var code = string.IsNullOrEmpty(saved_language_code) ? EditorLanguagePreferences.GetDefaultLanguageCode() : saved_language_code;
            return _table.HasLanguage(code) ? code : EditorLanguageTable.FALLBACK_LANGUAGE_CODE;
        }

        private static void WarnMissingKey(string key) => WarnOnce("missing:" + key, $"Missing text key \"{key}\".");

        private static void WarnOnce(string id, string message)
        {
            if (WarnedKeys.Add(id)) Debug.LogWarning(LOG_PREFIX + message);
        }
    }
}
