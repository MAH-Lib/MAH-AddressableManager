using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace MAH.AddressableManager.Editor.Localization
{
    /// <summary>
    /// 表示言語の設定（EditorPrefs。個人ごと）の読み書きと、未設定のときの初期値の決定。
    /// プロジェクトで共有される Editor SO ではなく EditorPrefs に置くのは、言語が人ごとの好みのため。
    /// </summary>
    internal static class EditorLanguagePreferences
    {
        private const string PREFS_KEY = "MAH.AddressableManager.Editor.LanguageCode";
        private const string JAPANESE_LANGUAGE_CODE = "ja";

        /// <summary> 保存された言語コード。未設定のときは空文字 </summary>
        internal static string LoadLanguageCode() => EditorPrefs.GetString(PREFS_KEY, string.Empty);

        internal static void SaveLanguageCode(string language_code) => EditorPrefs.SetString(PREFS_KEY, language_code);

        /// <summary>
        /// 未設定のときの初期値。PC の言語設定を読み、日本語（ja）か英語（en）にする。日本語を優先する（下の DecideDefaultLanguageCode）
        /// </summary>
        internal static string GetDefaultLanguageCode()
        {
            return DecideDefaultLanguageCode(ReadSystemLanguage(), ReadCultureName(() => CultureInfo.InstalledUICulture), ReadCultureName(() => CultureInfo.CurrentCulture));
        }

        /// <summary>
        /// PC の言語設定から、初期値を決める。<b>日本語を優先する</b>：
        /// OS の言語・表示言語（InstalledUICulture）・地域の書式（CurrentCulture）のどれかが日本語なら ja（表示言語が英語でも、地域が日本の日本語ユーザーのため）。
        /// どれも読めなかったときも ja。読めて、どれも日本語でなければ en
        /// </summary>
        internal static string DecideDefaultLanguageCode(SystemLanguage system_language, string ui_culture_name, string culture_name)
        {
            var readable = false;

            if (system_language != SystemLanguage.Unknown)
            {
                if (system_language == SystemLanguage.Japanese) return JAPANESE_LANGUAGE_CODE;
                readable = true;
            }

            foreach (var name in new[] { ui_culture_name, culture_name })
            {
                // 言語を特定できない名前（InvariantCulture は空文字）は、読めなかったものとして扱う
                if (string.IsNullOrEmpty(name)) continue;

                var language = name.Split('-')[0];
                if (language.Equals(JAPANESE_LANGUAGE_CODE, StringComparison.OrdinalIgnoreCase)) return JAPANESE_LANGUAGE_CODE;
                readable = true;
            }

            return readable ? EditorLanguageTable.FALLBACK_LANGUAGE_CODE : JAPANESE_LANGUAGE_CODE;
        }

        // OS の言語。取得できないときは Unknown（初期値は、読めなかったものとして日本語になる）
        private static SystemLanguage ReadSystemLanguage()
        {
            try
            {
                return Application.systemLanguage;
            }
            catch (Exception)
            {
                return SystemLanguage.Unknown;
            }
        }

        private static string ReadCultureName(Func<CultureInfo> read)
        {
            try
            {
                return read().Name;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
