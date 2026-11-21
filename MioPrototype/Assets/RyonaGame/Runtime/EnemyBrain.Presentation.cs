using System;
using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class EnemyBrain : MonoBehaviour
    {
        void ShowTelegraph()
        {
            if (Telegraph == null)
                return;

            EnsureTelegraphMaterial();
            Telegraph.gameObject.SetActive(true);
        }

        void HideTelegraph()
        {
            if (Telegraph != null)
                Telegraph.gameObject.SetActive(false);
        }

        /// <summary>
        /// 予備動作の進捗（0〜1）に合わせて球を拡大し、色を変える。
        /// 最後の <see cref="TelegraphMaxHoldSeconds"/> は最大サイズで固定し、「ここで来る」を確定させる。
        /// </summary>
        void ApplyTelegraphVisual(float t)
        {
            if (Telegraph == null)
                return;

            float grow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

            float maxScale = Mathf.Max(0.01f, S.TelegraphMaxScale);
            float minScale = Mathf.Clamp(S.TelegraphStartScale, 0.01f, maxScale);
            float scale = Mathf.Lerp(minScale, maxScale, grow);

            if (_timer <= Mathf.Max(0f, S.TelegraphMaxHoldSeconds))
                scale = maxScale;

            Telegraph.localScale = _baseTelegraphScale * scale;
            ApplyTelegraphColor(Color.Lerp(S.TelegraphStartColor, S.TelegraphColor, grow));
        }

        void EnsureTelegraphMaterial()
        {
            if (_telegraphMaterial != null || Telegraph == null)
                return;

            if (_telegraphRenderer == null)
                _telegraphRenderer = Telegraph.GetComponentInChildren<Renderer>(true);

            if (_telegraphRenderer != null)
                _telegraphMaterial = _telegraphRenderer.material;   // 実行時インスタンス。アセットは汚さない
        }

        void ApplyTelegraphColor(Color color)
        {
            if (_telegraphMaterial == null)
                return;

            // Built-in の Standard は _Color、URP Lit は _BaseColor を見る。両対応しておく。
            if (_telegraphMaterial.HasProperty(ColorId))
                _telegraphMaterial.SetColor(ColorId, color);
            if (_telegraphMaterial.HasProperty(BaseColorId))
                _telegraphMaterial.SetColor(BaseColorId, color);
        }

    }
}
