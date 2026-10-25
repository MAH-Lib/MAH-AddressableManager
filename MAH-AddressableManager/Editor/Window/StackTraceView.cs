using System.Collections.Generic;
using System.IO;
using MAH.AddressableManager.Core;
using MAH.AddressableManager.Editor.Localization;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MAH.AddressableManager.Editor.Window
{
    /// <summary>
    /// スタックトレースの表示とジャンプ。「実行時状態」タブと「リーク」タブが共通で使う。
    /// ファイルと行の情報があるフレームはリンクにして、押すと外部エディタで該当行を開く。全文のコピーもできる。
    /// 解析結果はトレース文字列ごとにキャッシュし、初めて見たトレースの1回だけ確保する（OnGUI は1フレームに何度も呼ばれるため、描画のたびに作らない）。
    /// </summary>
    internal sealed class StackTraceView
    {
        private const string FIRST_USER_MARK = "▶"; // 最初の利用者のフレームの印
        private const float INDENT_WIDTH = 15f; // EditorGUI.indentLevel 1段の幅。Unity 側の値は公開されていないため、見た目に合わせた値を持つ
        private static readonly Color DIM_COLOR = new(0.60f, 0.60f, 0.60f); // Dark / Light どちらのスキンでも読める中間の明るさ

        // GUILayout.Width は呼ぶたびに確保するため、使い回す
        private static readonly GUILayoutOption[] COPY_WIDTH = { GUILayout.Width(52f) };
        private static readonly GUILayoutOption[] MARK_WIDTH = { GUILayout.Width(14f) };

        /// <summary> 解析結果（フレームの一覧と、最初の利用者のフレームの添字） </summary>
        private sealed class ParsedTrace
        {
            private readonly TraceFrame[] _frames;
            private readonly int _firstUserFrameIndex;

            internal TraceFrame[] AccFrames => _frames;
            /// <summary> 最初の利用者のフレームの添字。無ければ -1 </summary>
            internal int AccFirstUserFrameIndex => _firstUserFrameIndex;

            internal ParsedTrace(TraceFrame[] frames)
            {
                _frames = frames;
                _firstUserFrameIndex = StackTraceParser.FindFirstUserFrame(frames);
            }
        }

        // Play 中は増え続けるが、件数は確保元の経路（スタックトレースの種類）の数で頭打ちになるため、上限は設けない。Clear() で破棄する
        private readonly Dictionary<string, ParsedTrace> _cache = new();
        private readonly string _projectRoot;
        private GUIStyle _dimStyle; // EditorStyles は OnGUI の中でしか使えないため、最初の描画で作る
        private GUIStyle _markStyle;

        internal StackTraceView()
        {
            // Application.dataPath は主スレッドでしか読めないため、タブの生成時（OnEnable）に、1回だけ読む
            _projectRoot = EditorProjectPath.GetProjectRoot();
        }

        /// <summary> 解析結果のキャッシュを空にする（未起動になったときにタブが呼ぶ） </summary>
        internal void Clear() => _cache.Clear();

        /// <summary> 最初の「利用者のフレーム」（モジュール内を飛ばした、ジャンプできる最初のフレーム）を取り出す。無ければ false </summary>
        internal bool TryGetFirstUserFrame(string stack_trace, out TraceFrame frame)
        {
            var parsed = GetOrParse(stack_trace);
            if (parsed.AccFirstUserFrameIndex < 0)
            {
                frame = default;
                return false;
            }

            frame = parsed.AccFrames[parsed.AccFirstUserFrameIndex];
            return true;
        }

        /// <summary> フレームのファイルを、外部エディタで該当行を開く。ジャンプできないフレーム・見つからないファイルは何もしない（見つからないときは警告） </summary>
        internal static void Jump(TraceFrame frame)
        {
            if (!frame.AccCanJump) return;
            if (!File.Exists(frame.AccPath))
            {
                AddressableLogger.LogWarning(EditorText.Format("stackTrace.fileNotFound", frame.AccPath));
                return;
            }

            InternalEditorUtility.OpenFileAtLineExternal(frame.AccPath, frame.AccLine);
        }

        /// <summary> スタックトレースを、フレームごとのリンクと、コピーのボタンで描く（OnGUI の中から呼ぶ）。現在の EditorGUI.indentLevel に合わせてインデントする </summary>
        internal void Draw(string stack_trace)
        {
            EnsureStyles();

            var parsed = GetOrParse(stack_trace);
            var indent = EditorGUI.indentLevel * INDENT_WIDTH;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(EditorText.Get("stackTrace.copy"), EditorStyles.miniButton, COPY_WIDTH))
                EditorGUIUtility.systemCopyBuffer = stack_trace ?? string.Empty;
            EditorGUILayout.EndHorizontal();

            var frames = parsed.AccFrames;
            if (frames.Length == 0)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(indent);
                GUILayout.Label(EditorText.Get("stackTrace.empty"), _dimStyle);
                EditorGUILayout.EndHorizontal();
                return;
            }

            for (var i = 0; i < frames.Length; i++)
            {
                var frame = frames[i];
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(indent);
                GUILayout.Label(i == parsed.AccFirstUserFrameIndex ? FIRST_USER_MARK : string.Empty, _markStyle, MARK_WIDTH);
                if (frame.AccCanJump)
                {
                    if (EditorGUILayout.LinkButton(frame.AccLabel))
                        Jump(frame);
                }
                else
                {
                    GUILayout.Label(frame.AccLabel, _dimStyle); // ジャンプできないフレーム（ファイルの情報が無い）は、薄く表示する
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
        }

        private ParsedTrace GetOrParse(string stack_trace)
        {
            stack_trace ??= string.Empty;
            if (!_cache.TryGetValue(stack_trace, out var parsed))
            {
                parsed = new ParsedTrace(StackTraceParser.Parse(stack_trace, _projectRoot));
                _cache[stack_trace] = parsed;
            }
            return parsed;
        }

        private void EnsureStyles()
        {
            if (_dimStyle != null) return;

            _dimStyle = new GUIStyle(EditorStyles.miniLabel);
            _dimStyle.normal.textColor = DIM_COLOR;
            _markStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
        }
    }
}
