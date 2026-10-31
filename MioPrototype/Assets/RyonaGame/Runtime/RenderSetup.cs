using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 影の設定。
    ///
    /// 既定の品質レベルでは影が切ってあり、キーライトにも影が設定されていないため、
    /// キャラクターが地面から浮いて見える（特に倒れたとき）。
    /// 起動時にまとめて有効にする。
    /// </summary>
    public static class RenderSetup
    {
        /// <summary>キーライトの仰角。高いほど影が短く、体の真下に落ちる。</summary>
        public const float KeyElevation = 60f;

        /// <summary>キーライトの方位。影を真後ろに出さず、少し横へ逃がす。</summary>
        public const float KeyYaw = -25f;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadowDistance = 14f;

            int enabledCount = 0;
            foreach (var light in Object.FindObjectsByType<Light>())
            {
                // リムライトは影を落とさない（正面のライトだけ）
                if (light.type != LightType.Directional || light.intensity < 0.9f)
                    continue;

                // 光を高くして影を短くする（低いと影が長く伸びて、体から離れて浮いて見える）
                light.transform.rotation = Quaternion.Euler(KeyElevation, KeyYaw, 0f);

                light.shadows = LightShadows.Soft;
                light.shadowStrength = 0.9f;
                light.shadowBias = 0.03f;
                light.shadowNormalBias = 0.35f;
                enabledCount++;
            }

            Debug.Log($"[Render] shadows={QualitySettings.shadows} dist={QualitySettings.shadowDistance} " +
                      $"lights={enabledCount}");
        }
    }
}
