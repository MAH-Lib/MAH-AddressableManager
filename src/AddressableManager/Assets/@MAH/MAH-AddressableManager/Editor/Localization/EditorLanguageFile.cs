using System;

namespace MAH.AddressableManager.Editor.Localization
{
    /// <summary>
    /// 言語ファイル（JSON）1つ分の形。JsonUtility は Dictionary を扱えないため、キーと値の組の配列で持つ。
    /// ファイル名は MAH.AddressableManager.Editor.&lt;言語コード&gt;.json に固定する。
    /// </summary>
    [Serializable]
    internal sealed class EditorLanguageFile
    {
        public string languageCode; // 例：ja / en / fr。ファイル名の言語コードとは独立して、この値が正
        public string languageName; // Preferences の選択肢に出す、その言語での名前（例：日本語 / English / Français）
        public EditorLanguageEntry[] entries;
    }

    [Serializable]
    internal sealed class EditorLanguageEntry
    {
        public string key;
        public string value;
    }
}
