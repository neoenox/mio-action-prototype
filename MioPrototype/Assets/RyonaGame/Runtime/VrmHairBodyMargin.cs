using UniVRM10;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 髪の毛束（板）が体・服にめり込むのを防ぐため、胴と腕のスプリングボーンコライダーを
    /// 少しだけ広げる。
    ///
    /// 計測（<see cref="Ryona.Tests.HairBodyRuntimeTests"/>）では、髪ジョイントは
    /// 体のコライダーに 1 本も入っていない。しかし 2cm の余裕を足すと 11 本が内側に入る。
    /// ジョイントは外でも、毛束のメッシュ（板）は関節より外へ張り出すため、
    /// 見た目では髪が服を貫通する。壁で <see cref="VrmHairWallClamp"/> が使っているのと同じ考えで、
    /// 体側の判定を毛束のぶんだけ外へ出す。
    ///
    /// 頭・首は髪の根元なので広げない（広げると髪全体が浮いて頭が大きく見える）。
    /// </summary>
    [AddComponentMenu("Ryona/VRM Hair Body Margin")]
    [DefaultExecutionOrder(25900)]
    public sealed class VrmHairBodyMargin : MonoBehaviour
    {
        [Tooltip("胴・腕のコライダーを広げる量（m）。毛束の板の張り出しぶん")]
        public float Margin = 0.035f;

        Vrm10Instance _vrm;
        bool _applied;

        void Start() => Apply();

        /// <summary>
        /// VRM の読み込み直後はコライダーがまだ無いことがあるので、
        /// 見つかるまで数フレーム試す。
        /// </summary>
        void LateUpdate()
        {
            if (!_applied)
                Apply();
        }

        void Apply()
        {
            if (_applied)
                return;

            _vrm = GetComponent<Vrm10Instance>();
            if (_vrm == null || _vrm.Runtime == null || _vrm.Runtime.SpringBone == null)
                return;

            var colliders = GetComponentsInChildren<VRM10SpringBoneCollider>(true);
            if (colliders == null || colliders.Length == 0)
                return;

            int changed = 0;
            for (int i = 0; i < colliders.Length; i++)
            {
                var col = colliders[i];
                if (col == null || !ShouldWiden(col.transform.name))
                    continue;

                // Radius はモデルのローカル単位。世界の Margin に直して足す。
                float scale = Mathf.Max(0.0001f, col.transform.lossyScale.x);
                col.Radius = Mathf.Min(1f, col.Radius + Margin / scale);
                changed++;
            }

            if (changed == 0)
            {
                // コライダーがまだ揃っていない。次フレームで再試行する。
                return;
            }

            _vrm.Runtime.SpringBone.ReconstructSpringBone();
            _applied = true;
            GameLog.Log($"[HairBody] 胴・腕のコライダーを +{Margin:F3}m 広げた（{changed} 個）");
        }

        static bool ShouldWiden(string bone)
        {
            if (string.IsNullOrEmpty(bone))
                return false;

            // 胸・肩・腕。背骨は服の上から髪が落ちる位置なので含める。
            return bone == "J_Bip_C_Spine"
                || bone == "J_Bip_C_UpperChest"
                || bone == "J_Bip_L_UpperArm"
                || bone == "J_Bip_R_UpperArm";
        }
    }
}
