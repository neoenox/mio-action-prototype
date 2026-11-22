using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Ryona.EditorTools
{
    /// <summary>
    /// スクリプトがコンパイルされたあと、エディタが落ち着いてから Windows プレイヤーを
    /// 自動で作り直す。<c>Build/MioPrototype.exe</c> を起動すれば、それだけで
    /// 常に最新のコードが動いている状態を保つため。
    ///
    /// - 変更が無ければ何もしない（<see cref="PlayerBuild.IsStale"/>）
    /// - Play 中はビルドしない（終了後に改めて予約する）
    /// - エディタが非アクティブ（他アプリ操作中）の間はビルドしない（戻ってから作り直す）
    /// - メニュー <c>Ryona/Auto Build Player</c> でオン・オフできる（既定はオン）
    /// </summary>
    [InitializeOnLoad]
    static class AutoPlayerBuild
    {
        const string PrefKey = "Ryona.AutoBuildPlayer";

        /// <summary>コンパイル直後にエディタが触られるかもしれないので、少し置いてから動く。</summary>
        const double DebounceSeconds = 15.0;

        /// <summary>
        /// エディタが非アクティブな間の見直し間隔。コンパイル待ち（2 秒）と同じくらいの短さにして、
        /// エディタに戻ってから数秒以内に差分ビルドが始まるようにする。
        /// 非アクティブ中はこの間隔で時刻を見るだけなので、毎フレーム作り直すより負荷が低い。
        /// </summary>
        const double InactiveRetrySeconds = 3.0;

        static double _dueAt = -1.0;
        static bool _building;

        static AutoPlayerBuild()
        {
            // バッチ実行（テスト・CLI ビルド）では自動ビルドしない。呼び出し側が明示的に作る。
            if (Application.isBatchMode)
                return;

            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;

            // static コンストラクタはドメインリロード（＝コンパイル）のたびに走る。
            if (Enabled)
                Schedule(DebounceSeconds);
        }

        static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        static void Schedule(double delay)
        {
            _dueAt = EditorApplication.timeSinceStartup + Mathf.Max(0.5f, (float)delay);
        }

        [MenuItem("Ryona/Auto Build Player (On Compile)", false, 40)]
        static void Toggle()
        {
            Enabled = !Enabled;
            Debug.Log($"[Ryona] 自動ビルド: {(Enabled ? "オン" : "オフ")}");
            if (Enabled)
                Schedule(1.0);
        }

        [MenuItem("Ryona/Auto Build Player (On Compile)", true)]
        static bool ToggleValidate()
        {
            Menu.SetChecked("Ryona/Auto Build Player (On Compile)", Enabled);
            return true;
        }

        static void Tick()
        {
            if (_dueAt < 0.0 || EditorApplication.timeSinceStartup < _dueAt)
                return;

            _dueAt = -1.0;

            if (!Enabled || _building || Application.isBatchMode)
                return;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                // Play 中はビルドできない。終わってから出直す。
                Schedule(30.0);
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                Schedule(2.0);
                return;
            }

            // エディタが非アクティブ（他のアプリを操作中）の間はビルドしない。
            // 連続してコードを直している最中にビルドが走ると、そのたびにエディタが固まって
            // 保存もままならなくなるため。ここで一度見送り、戻ってきたら改めて差分を見る
            // （ファイルを見に行く IsStale() も、アクティブになるまで呼ばない）。
            //
            // UnityEditor.EditorApplication.isFocused はメインウィンドウのフォーカスを見るので、
            // Inspector など別のエディタウィンドウを触っただけでも false になりうる。ここで欲しいのは
            // 「アプリ自体が前面か」なので、アプリ全体の状態を返す
            // UnityEditorInternal.InternalEditorUtility.isApplicationActive を使う
            // （Unity 6.4 / 6000.4.5f1 の UnityEditor.dll に public static bool の getter があることを確認済み）。
            if (!InternalEditorUtility.isApplicationActive)
            {
                Schedule(InactiveRetrySeconds);
                return;
            }

            if (!PlayerBuild.IsStale())
                return;

            _building = true;
            try
            {
                Debug.Log("[Ryona] 自動ビルドを開始（Build/MioPrototype.exe を最新にする）");
                PlayerBuild.BuildWindows();
            }
            finally
            {
                _building = false;
            }
        }
    }
}
