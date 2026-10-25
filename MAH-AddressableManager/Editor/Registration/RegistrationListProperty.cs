using System.Collections.Generic;
using UnityEditor;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// 文字列のリスト（拡張子・Label）を持つ SerializedProperty の、追加・解除・読み取り。
    /// ルールの SO の Inspector と、手動一括登録のウィンドウで同じ処理を使うため、ここに集める。
    /// 呼び出し側が serializedObject.Update() / ApplyModifiedProperties() を行う。
    /// </summary>
    internal static class RegistrationListProperty
    {
        /// <summary> リストの文字列をすべて読む </summary>
        internal static List<string> Read(SerializedProperty list)
        {
            var result = new List<string>(list.arraySize);
            for (var i = 0; i < list.arraySize; i++) result.Add(list.GetArrayElementAtIndex(i).stringValue);
            return result;
        }

        /// <summary> 拡張子を、すでにあれば外し、なければ足す（大文字小文字・ドットの有無を問わず同じ扱い。保存は小文字のドット付き） </summary>
        internal static void ToggleExtension(SerializedProperty list, string extension)
        {
            var normalized = RegistrationRuleInput.NormalizeExtension(extension);
            if (normalized.Length == 0) return;

            var index = IndexOfExtension(list, normalized);
            if (index >= 0) list.DeleteArrayElementAtIndex(index);
            else Append(list, "." + normalized);
        }

        /// <summary> 拡張子を足す（すでにあれば何もしない） </summary>
        internal static void AddExtension(SerializedProperty list, string extension)
        {
            var normalized = RegistrationRuleInput.NormalizeExtension(extension);
            if (normalized.Length == 0 || IndexOfExtension(list, normalized) >= 0) return;

            Append(list, "." + normalized);
        }

        /// <summary> Label を、すでにあれば外し、なければ足す（大文字小文字を区別する。Addressables の Label と同じ） </summary>
        internal static void ToggleLabel(SerializedProperty list, string label)
        {
            var trimmed = (label ?? string.Empty).Trim();
            if (trimmed.Length == 0) return;

            var index = IndexOfLabel(list, trimmed);
            if (index >= 0) list.DeleteArrayElementAtIndex(index);
            else Append(list, trimmed);
        }

        /// <summary> Label を足す（すでにあれば何もしない） </summary>
        internal static void AddLabel(SerializedProperty list, string label)
        {
            var trimmed = (label ?? string.Empty).Trim();
            if (trimmed.Length == 0 || IndexOfLabel(list, trimmed) >= 0) return;

            Append(list, trimmed);
        }

        private static int IndexOfExtension(SerializedProperty list, string normalized)
        {
            for (var i = 0; i < list.arraySize; i++)
            {
                if (RegistrationRuleInput.NormalizeExtension(list.GetArrayElementAtIndex(i).stringValue) == normalized) return i;
            }
            return -1;
        }

        private static int IndexOfLabel(SerializedProperty list, string trimmed)
        {
            for (var i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).stringValue.Trim() == trimmed) return i;
            }
            return -1;
        }

        private static void Append(SerializedProperty list, string value)
        {
            var index = list.arraySize;
            list.InsertArrayElementAtIndex(index);
            list.GetArrayElementAtIndex(index).stringValue = value;
        }
    }
}
