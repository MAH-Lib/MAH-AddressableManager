using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MAH.AddressableManager.Editor.Localization
{
    /// <summary>
    /// Inspector の描画部品（IMGUI。MonoBehaviour ではない）。SerializedProperty の見出し・ツールチップを、言語ファイルのキーから引いて描く。
    /// [Header] / [Tooltip] は定数で切り替えられず、PropertyField は必ず描画してしまうため、対象のクラスからは外し、文言はキーで持つ。
    /// キーは、見出し：header.&lt;SO の型名&gt;.&lt;propertyPath&gt;、ツールチップ：tooltip.&lt;SO の型名&gt;.&lt;propertyPath&gt;
    /// （例：tooltip.AddressableEditorSettingsSO._registration._autoRegistration）。キーが無いものは、見出し・ツールチップなしで描く。
    /// 入れ子の [Serializable] クラスは、見出しのキーがあれば、その文言を折りたたみ（Foldout）の名前にする（変数名由来の英語が残らないように）。
    /// 作った GUIContent は、言語が変わるまで使い回す（Inspector の描画のたびに確保しない）。
    /// </summary>
    internal static class LocalizedPropertyView
    {
        private const float HEADER_TOP_SPACE = 8f; // [Header] と同じ、見出しの上の余白
        private const string MODULE_NAMESPACE = "MAH.AddressableManager";

        private static readonly Dictionary<Type, Dictionary<string, GUIContent>> LabelCache = new();
        private static readonly Dictionary<Type, Dictionary<string, string>> HeaderCache = new();
        private static readonly Dictionary<Type, Dictionary<string, bool>> OwnedCache = new(); // 言語に依存しないため、ClearCache では捨てない

        static LocalizedPropertyView()
        {
            EditorText.LanguageChanged += ClearCache;
        }

        /// <summary>
        /// SO のすべての項目を、見出し・ツールチップ付きで描く（Script の欄を含む。標準の Inspector と同じ並び）。
        /// [Serializable] のクラスは、標準と同じ折りたたみ（Foldout）にして、中の項目を再帰して描く
        /// </summary>
        internal static void DrawAll(SerializedObject serialized_object)
        {
            serialized_object.Update();

            var iterator = serialized_object.GetIterator();
            var enter_children = true;
            while (iterator.NextVisible(enter_children))
            {
                enter_children = false;
                if (iterator.propertyPath == "m_Script")
                {
                    using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(iterator, true);
                    continue;
                }
                DrawProperty(serialized_object, iterator);
            }

            serialized_object.ApplyModifiedProperties();
        }

        /// <summary> 1つの項目を、ツールチップ付きの見出しで描く（見出しの [Header] 相当も、キーがあれば前に出す） </summary>
        internal static void Field(SerializedObject serialized_object, SerializedProperty property, bool include_children = true)
        {
            DrawHeader(serialized_object, property);
            EditorGUILayout.PropertyField(property, GetLabel(serialized_object, property), include_children);
        }

        // 入れ子のクラスは Foldout にして、中を再帰して描く。それ以外（値・配列・リスト）は PropertyField に任せる
        private static void DrawProperty(SerializedObject serialized_object, SerializedProperty property)
        {
            DrawHeader(serialized_object, property);

            if (!IsNestedClass(serialized_object, property))
            {
                EditorGUILayout.PropertyField(property, GetLabel(serialized_object, property), true);
                return;
            }

            property.isExpanded = EditorGUILayout.Foldout(property.isExpanded, GetLabel(serialized_object, property), true);
            if (!property.isExpanded) return;

            EditorGUI.indentLevel++;
            var end = property.GetEndProperty();
            var child = property.Copy();
            var enter_children = true;
            while (child.NextVisible(enter_children) && !SerializedProperty.EqualContents(child, end))
            {
                enter_children = false;
                DrawProperty(serialized_object, child);
            }
            EditorGUI.indentLevel--;
        }

        // 入れ子の [Serializable] クラス（Foldout で描くもの）。展開するのは、このモジュール自身の型だけ。
        // AssetReference・UnityEvent・利用者の型のように、独自の描画（PropertyDrawer）を持ち得る型は、PropertyField に任せる
        private static bool IsNestedClass(SerializedObject serialized_object, SerializedProperty property)
            => property.propertyType == SerializedPropertyType.Generic
               && !property.isArray
               && property.hasVisibleChildren
               && IsModuleOwnedProperty(serialized_object, property);

        /// <summary> この名前空間（MAH.AddressableManager 以下）の型か </summary>
        internal static bool IsModuleOwnedType(Type type)
            => type != null && type.Namespace != null
               && (type.Namespace == MODULE_NAMESPACE || type.Namespace.StartsWith(MODULE_NAMESPACE + ".", StringComparison.Ordinal));

        // propertyPath をフィールドの型でたどり、最後の型がこのモジュールの型かを調べる。結果は言語と無関係なため、キャッシュを言語の変更で捨てない
        private static bool IsModuleOwnedProperty(SerializedObject serialized_object, SerializedProperty property)
        {
            var root_type = serialized_object.targetObject.GetType();
            var path = property.propertyPath;
            if (!OwnedCache.TryGetValue(root_type, out var owned_by_path))
            {
                owned_by_path = new Dictionary<string, bool>();
                OwnedCache.Add(root_type, owned_by_path);
            }

            if (!owned_by_path.TryGetValue(path, out var is_owned))
            {
                is_owned = IsModuleOwnedType(ResolveFieldType(root_type, path));
                owned_by_path.Add(path, is_owned);
            }
            return is_owned;
        }

        // "_a._b" を、_a のフィールドの型 → その中の _b のフィールドの型、とたどる。たどれないとき（配列の要素など）は null
        private static Type ResolveFieldType(Type root_type, string property_path)
        {
            var type = root_type;
            var start = 0;
            while (start <= property_path.Length)
            {
                var end = property_path.IndexOf('.', start);
                if (end < 0) end = property_path.Length;

                var field = FindField(type, property_path.Substring(start, end - start));
                if (field == null) return null;

                type = field.FieldType;
                start = end + 1;
            }
            return type;
        }

        // private なフィールドは、基底クラスの分を含めて、継承の階層をたどって探す
        private static FieldInfo FindField(Type type, string name)
        {
            const BindingFlags FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (; type != null; type = type.BaseType)
            {
                var field = type.GetField(name, FLAGS);
                if (field != null) return field;
            }
            return null;
        }

        // 入れ子のクラスは、見出しを Foldout の名前として出す（別に見出しを描くと、同じ名前が2つ並び、Foldout は変数名由来の英語のままになるため）
        private static void DrawHeader(SerializedObject serialized_object, SerializedProperty property)
        {
            if (IsNestedClass(serialized_object, property)) return;

            var header = GetHeader(serialized_object, property);
            if (header == null) return;

            GUILayout.Space(HEADER_TOP_SPACE);
            EditorGUILayout.LabelField(header, EditorStyles.boldLabel);
        }

        /// <summary>
        /// 項目の名前とツールチップ。名前は、入れ子のクラスで見出しのキーがあればその文言、無ければ変数名由来の名前（Unity が付ける英語）
        /// </summary>
        internal static GUIContent GetLabel(SerializedObject serialized_object, SerializedProperty property)
        {
            var type = serialized_object.targetObject.GetType();
            var path = property.propertyPath;
            if (!LabelCache.TryGetValue(type, out var labels))
            {
                labels = new Dictionary<string, GUIContent>();
                LabelCache.Add(type, labels);
            }

            if (!labels.TryGetValue(path, out var label))
            {
                var tooltip = EditorText.TryGet("tooltip." + type.Name + "." + path, out var text) ? text : string.Empty;
                var header = IsNestedClass(serialized_object, property) ? GetHeader(serialized_object, property) : null;
                label = new GUIContent(header ?? property.displayName, tooltip);
                labels.Add(path, label);
            }
            return label;
        }

        /// <summary> テスト用：作った GUIContent・見出しのキャッシュを捨てる（言語を差し替えたとき） </summary>
        internal static void ClearCacheForTest()
        {
            LabelCache.Clear();
            HeaderCache.Clear();
        }

        // 見出しが無い項目は null（キャッシュには、無いことも覚える）
        internal static string GetHeader(SerializedObject serialized_object, SerializedProperty property)
        {
            var type = serialized_object.targetObject.GetType();
            var path = property.propertyPath;
            if (!HeaderCache.TryGetValue(type, out var headers))
            {
                headers = new Dictionary<string, string>();
                HeaderCache.Add(type, headers);
            }

            if (!headers.TryGetValue(path, out var header))
            {
                header = EditorText.TryGet("header." + type.Name + "." + path, out var text) ? text : null;
                headers.Add(path, header);
            }
            return header;
        }

        private static void ClearCache()
        {
            LabelCache.Clear();
            HeaderCache.Clear();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews(); // 開いている Inspector に、新しい言語をすぐ反映する
        }
    }
}
