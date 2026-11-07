using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 手続き的モーション。ここは「エディタ・撮影用のプレビュー API」だけを持つ。
    /// </summary>
    public sealed partial class CharacterAnimator
    {
        /// <summary>
        /// 検証シート用。実際の被弾を通さずに「どこから殴られたか」を固定する。
        /// 0=正面 / ±1=左右 / 2=背後。
        /// </summary>
        public void PreviewHurtSide(int side)
        {
            _hurtSide = side;
        }

        /// <summary>検証シート用。疲労度を固定する（0=平気 / 1=限界）。</summary>
        public void PreviewTired(float value)
        {
            _tired = Mathf.Clamp01(value);
            _tiredHold = true;
        }

        /// <summary>検証シート用。振りほどき直後の突き放しを任意の残り時間で出す。</summary>
        public void PreviewShove(float seconds)
        {
            _shoveTime = Mathf.Max(0f, seconds);
        }

        /// <summary>エディタのプレビュー用。ガードで押し返されている度合い（0〜1）を直接入れる。</summary>
        public void ForceGuardRecoil(float normalized)
        {
            _guardRecoil = Mathf.Clamp01(normalized) * GuardRecoilSeconds;
        }

        /// <summary>エディタのプレビュー用。状態を固定して即座に姿勢を作る。</summary>
        /// <param name="log">毎フレーム呼ぶときは false にしてログを止める。</param>
        public void ForceState(State state, float time, float duration = 1f, StaggerTier tier = StaggerTier.Medium, bool log = true)
        {
            _state = state;
            _stateTime = time;
            _stateDuration = duration;
            _tier = tier;
            _walkPhase = time * Mathf.PI * 2f;
            _planarSpeed = 1.4f;
            _blend = 1f;
            ApplyPose();

            if (log && Rig != null && Rig.Ready)
            {
                Debug.Log($"[Pose] {state} t={time:F2} hipsY={Rig.Hips.position.y:F3} " +
                          $"footLz={Rig.LFoot.position.z:F3} footLx={Rig.LFoot.position.x:F3} " +
                          $"handRy={Rig.RHand.position.y:F3} handRz={Rig.RHand.position.z:F3}");
            }
        }
    }
}
