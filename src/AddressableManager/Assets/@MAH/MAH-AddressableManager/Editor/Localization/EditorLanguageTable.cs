using System;
using System.Collections.Generic;
using UnityEngine;

namespace MAH.AddressableManager.Editor.Localization
{
    /// <summary>
    /// 言語ごとの「キー → 文言」の表。JSON 文字列の解析・同梱分と利用者分の統合・フォールバック付きの引き当てを行う。
    /// AssetDatabase などに依存しないピュア C#（EditMode テストで、ファイルなしに検証できるようにするため）。
    /// 引き当ては Dictionary を引くだけで、アロケーションしない。
    /// </summary>
    internal sealed class EditorLanguageTable
    {
        internal const string FALLBACK_LANGUAGE_CODE = "en";

        private readonly Dictionary<string, Dictionary<string, string>> _texts = new();
        private readonly Dictionary<string, string> _languageNames = new();
        private readonly List<string> _languageCodes = new();

        /// <summary> 登録済みの言語コード（登録順） </summary>
        internal IReadOnlyList<string> AccLanguageCodes => _languageCodes;

        internal bool HasLanguage(string language_code) => !string.IsNullOrEmpty(language_code) && _texts.ContainsKey(language_code);

        /// <summary> Preferences の選択肢に出す名前。languageName が空のときは言語コードを返す </summary>
        internal string GetLanguageName(string language_code)
        {
            return _languageNames.TryGetValue(language_code, out var name) && !string.IsNullOrEmpty(name) ? name : language_code;
        }

        /// <summary>
        /// JSON 文字列を解析する。失敗したとき（パースの失敗・languageCode が空）は false と理由を返し、表には何も足さない。
        /// </summary>
        internal static bool TryParse(string json, out EditorLanguageFile file, out string error)
        {
            file = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "The file is empty.";
                return false;
            }

            try
            {
                file = JsonUtility.FromJson<EditorLanguageFile>(json);
            }
            catch (Exception exception)
            {
                error = $"Could not parse JSON: {exception.Message}";
                return false;
            }

            if (file == null || string.IsNullOrWhiteSpace(file.languageCode))
            {
                file = null;
                error = "languageCode is empty.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 言語ファイルを表に足す。同じ言語コードが既にあれば、キー単位で上書きする（後から足したものが優先。
        /// 同梱分を先に、利用者分を後に足すことで、利用者の訳が同梱の訳を上書きする）。
        /// </summary>
        internal void Add(EditorLanguageFile file)
        {
            var code = file.languageCode.Trim();
            if (!_texts.TryGetValue(code, out var texts))
            {
                texts = new Dictionary<string, string>();
                _texts.Add(code, texts);
                _languageCodes.Add(code);
            }

            if (!string.IsNullOrEmpty(file.languageName)) _languageNames[code] = file.languageName;
            if (file.entries == null) return;

            foreach (var entry in file.entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.key) || entry.value == null) continue;
                texts[entry.key] = entry.value;
            }
        }

        /// <summary>
        /// 文言を引く。現在の言語 → 英語 → null の順で探す（null のときは、呼び出し側がキー文字列を出す）。
        /// </summary>
        internal bool TryGet(string language_code, string key, out string text)
        {
            if (!string.IsNullOrEmpty(language_code) && _texts.TryGetValue(language_code, out var texts) && texts.TryGetValue(key, out text))
                return true;

            if (_texts.TryGetValue(FALLBACK_LANGUAGE_CODE, out var fallback_texts) && fallback_texts.TryGetValue(key, out text))
                return true;

            text = null;
            return false;
        }

        /// <summary> その言語に、指定した言語（通常は英語）に在るのに無いキーを集める（一貫性のテスト・欠落の確認用） </summary>
        internal void CollectMissingKeys(string language_code, string base_language_code, List<string> missing_keys)
        {
            missing_keys.Clear();
            if (!_texts.TryGetValue(base_language_code, out var base_texts)) return;
            _texts.TryGetValue(language_code, out var texts);

            foreach (var key in base_texts.Keys)
            {
                if (texts == null || !texts.ContainsKey(key)) missing_keys.Add(key);
            }
        }

        /// <summary> 翻訳の雛形の JSON を作る。指定した言語（通常は英語）の全キーを、キーの順に並べて入れる </summary>
        internal string BuildTemplateJson(string base_language_code, string new_language_code, string new_language_name)
        {
            var file = new EditorLanguageFile
            {
                languageCode = new_language_code,
                languageName = new_language_name,
            };

            var entries = new List<EditorLanguageEntry>();
            if (_texts.TryGetValue(base_language_code, out var base_texts))
            {
                foreach (var pair in base_texts)
                    entries.Add(new EditorLanguageEntry { key = pair.Key, value = pair.Value });
            }
            entries.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
            file.entries = entries.ToArray();

            return JsonUtility.ToJson(file, true);
        }

        /// <summary> 文言に含まれる書式の引数（{0} {1} ...）の最大の番号 + 1。引数がなければ 0。エスケープされた {{ は引数として数えない </summary>
        internal static int CountFormatArguments(string text)
        {
            var count = 0;
            for (var i = 0; i < text.Length - 2; i++)
            {
                if (text[i] != '{') continue;
                if (text[i + 1] == '{')
                {
                    i++; // {{ は波括弧そのもの。2文字目を引数の始まりと見ないよう飛ばす
                    continue;
                }
                var j = i + 1;
                var number = 0;
                var has_digit = false;
                while (j < text.Length && char.IsDigit(text[j]))
                {
                    number = number * 10 + (text[j] - '0');
                    has_digit = true;
                    j++;
                }
                if (has_digit && j < text.Length && (text[j] == '}' || text[j] == ',' || text[j] == ':'))
                    count = Math.Max(count, number + 1);
            }
            return count;
        }

        /// <summary> 同梱の英語の表のキーと文言（雛形・一貫性のテストが読む） </summary>
        internal bool TryGetLanguageTexts(string language_code, out IReadOnlyDictionary<string, string> texts)
        {
            if (_texts.TryGetValue(language_code, out var dictionary))
            {
                texts = dictionary;
                return true;
            }
            texts = null;
            return false;
        }
    }
}
