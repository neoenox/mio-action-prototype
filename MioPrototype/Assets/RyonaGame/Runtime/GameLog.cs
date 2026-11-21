using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// ゲーム内ログの唯一の出口。**本番ビルドでは既定で黙る。**
    ///
    /// <para>
    /// これまで <c>Debug.Log</c> を直接呼んでいたため、リリースビルドでも毎攻撃・毎ヒット・
    /// 毎拘束のログが player log に書き出され、文字列生成の GC も発生していた。
    /// ここを通すことで、
    /// </para>
    /// <list type="bullet">
    /// <item>Editor / Development Build: 従来どおり常に出す（検証の手触りを変えない）</item>
    /// <item>リリースビルド: 起動引数 <c>-log</c> を付けたときだけ出す</item>
    /// <item>既存の検証フラグ（<c>-hitlog</c> / <c>-statlog</c> / <c>-pinlog</c> / <c>-jointlog</c>）
    /// を付けた場合も出す（そのフラグで見たかったログが消えないように）</item>
    /// </list>
    ///
    /// <para>
    /// 使い方: <c>GameLog.Log("[Combat] ...")</c>。<c>Debug.Log</c> を直接呼ばないこと。
    /// 毎フレーム呼ぶ箇所は、文字列を組み立てる前に <see cref="Enabled"/> で早期 return すると
    /// GC も抑えられる。
    /// </para>
    ///
    /// <para>
    /// <see cref="Debug.LogWarning"/> / <see cref="Debug.LogError"/> は異常系なのでここを通さず、
    /// 常に出したままにしている。
    /// </para>
    /// </summary>
    public static class GameLog
    {
        static bool _initialized;
        static bool _enabled;

        /// <summary>このビルドでログを出す設定か。重い文字列組み立ての前に見る。</summary>
        public static bool Enabled
        {
            get
            {
                EnsureInitialized();
                return _enabled;
            }
        }

        /// <summary>
        /// 起動時に 1 度だけ判定する。Editor / Development Build 以外は起動引数で決める。
        /// <c>Awake</c> より前に呼ばれても遅延初期化で間に合うようにしてある。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void EnsureInitialized()
        {
            if (_initialized)
                return;

            _initialized = true;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // 開発中は従来どおり全部出す。ログを見たいのに引数が必要、では手が止まる。
            _enabled = true;
#else
            // リリースビルドは既定で黙る。調査したいときだけ -log を付ける。
            // 既存の検証フラグでも有効にするのは、そのフラグで見ていたログ
            // （-hitlog の当たり判定など）をリリースビルドで再現できるようにするため。
            _enabled = LaunchOptions.Has("-log")
                || LaunchOptions.Has("-hitlog")
                || LaunchOptions.Has("-statlog")
                || LaunchOptions.Has("-pinlog")
                || LaunchOptions.Has("-jointlog")
                || LaunchOptions.Has("-facelog");

            // 既定で黙るビルドでだけ、ON にしたことを 1 行知らせる
            // （Editor / Development Build では毎回出るので出さない）。
            if (_enabled)
                Debug.Log("[Log] ログ出力 ON（リリースビルド既定 OFF）");
#endif
        }

        /// <summary>ログを出す。<see cref="Enabled"/> が false なら何もしない。</summary>
        public static void Log(string message)
        {
            if (!Enabled)
                return;

            Debug.Log(message);
        }
    }
}
