using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// <see cref="RyonaConfig.OnConfigChanged"/> を購読し、設定が変わったら <see cref="OnConfigChanged"/> で再適用する基底クラス。
    /// <para>
    /// <c>OnEnable</c> で購読し、<c>OnDisable</c> で解除する。無効化中に設定が変わった場合は、
    /// 再有効化時に現在の設定を 1 度だけ適用する。
    /// </para>
    /// </summary>
    public abstract class ConfigurableMonoBehaviour : MonoBehaviour
    {
        bool _everEnabled;

        /// <summary>
        /// 派生クラスが <c>override</c> するための初期化フック。
        /// <see cref="MonoBehaviour"/> の <c>Awake</c> は virtual ではないので、ここで口を開ける。
        /// </summary>
        protected virtual void Awake()
        {
        }

        protected virtual void OnEnable()
        {
            RyonaConfig.OnConfigChanged += OnConfigChanged;

            // 初回は派生クラスの Awake が適用済み。2 回目以降だけ、無効化中の変更を取り戻す。
            if (_everEnabled)
                OnConfigChanged(RyonaConfig.Instance);

            _everEnabled = true;
        }

        protected virtual void OnDisable()
        {
            RyonaConfig.OnConfigChanged -= OnConfigChanged;
        }

        /// <summary>
        /// 設定変更時に呼ばれる。派生クラスでオーバーライドして再適用ロジックを書く。
        /// </summary>
        public abstract void OnConfigChanged(RyonaConfig config);
    }
}
