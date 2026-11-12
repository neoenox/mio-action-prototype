using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class CharacterAnimator : MonoBehaviour
    {
        void SetState(State state, float duration)
        {
            _state = state;
            _stateTime = 0f;
            _stateDuration = duration;

            if (state == State.Down)
            {
                _groundRolled = false;
                // 吹き飛ばされて倒れるときは、倒れきる時刻を着地に合わせる。
                // 空中にいるのに「その場で崩れる」速さで倒れると、足が地面を滑って見える。
                _downForward = Stats != null && Stats.DownIsForward;
                _downLaunched = !_downForward && Motor != null && Motor.IsAirborne;

                // 吹き飛ばされるときは速く倒す。0.52 秒かけて回すと「ゆっくり傾いていく板」に見え、
                // 倒れるというより「倒されている途中を眺めている」画になる。0.32 秒で着地と同時に倒れきる。
                // 前へ崩れるときは腹を抱えて膝から落ちるので、少しだけ長く取る。
                _downFallSeconds = _downForward
                    ? DownFallSeconds * 0.79f
                    : (_downLaunched ? DownFallSeconds * 0.34f : DownFallSeconds);

                _airBlend = _downLaunched ? 1f : 0f;
                _downEndLay = _downForward ? 92f : -92f;
            }
        }

        internal void FinishGroundRoll()
        {
            Rig.SnapshotPose();
            _blend = 0f;
            _blendSeconds = 0.15f;
            _downForward = false;
            _groundRolled = true;
            _downLaunched = false;
            _airBlend = 0f;
            _downEndLay = -92f;
        }

        /// <summary>
        /// 攻撃の出始めに体を相手へ向ける（ソフトオートエイム）。
        /// キーボードだけだと、立ち止まったままでは向きを変えられず、
        /// 「前を向いたまま横の敵を殴って空振り」になるため。
        /// 振り向く角度は maxDegrees で制限する（真後ろまでは回らない）。
        /// </summary>
        public void CancelAim()
        {
            _hasAim = false;
            _hasFaceYaw = false;
        }

        public void AimAt(Vector3 worldPoint, float maxDegrees)
        {
            Vector3 d = worldPoint - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 0.0004f)
                return;

            Quaternion want = Quaternion.LookRotation(d.normalized, Vector3.up);
            Transform body = transform.root;

            _aimStart = body.rotation;
            _aimYaw = Quaternion.RotateTowards(body.rotation, want, Mathf.Max(0f, maxDegrees));
            _aimTime = 0f;

            float turn = Quaternion.Angle(_aimStart, _aimYaw);
            _hasAim = turn > 2f;

            if (_hasAim)
                Debug.Log($"[Aim] turn={turn:F0} angle={Vector3.Angle(transform.forward, d):F0}");
        }

    }
}
