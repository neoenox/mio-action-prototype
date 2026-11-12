using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// カメラ追尾の実測用。<c>-camlog</c> 付きで起動したときだけ動く。
    /// 「カメラが付いてこない」を目視ではなく数字で切り分けるために置いてある。
    ///
    /// 出す値:
    /// <list type="bullet">
    /// <item><c>px</c> 彼女の X / <c>cx</c> カメラの追従目標 X / <c>sx</c> 実際に映っている中心 X</item>
    /// <item><c>vx</c> 画面内の横位置（0=左端, 0.5=中央, 1=右端。1 超で画面外）</item>
    /// <item><c>lock</c> 画面ロック中か / <c>gate</c> 関所が決めているカメラの可動範囲</item>
    /// <item><c>lim</c> 彼女が歩ける前後の上限（画面ロックが入れている値）</item>
    /// </list>
    /// </summary>
    public sealed class CameraProbe : MonoBehaviour
    {
        ThirdPersonCamera _rig;
        BeltScreenLock _lock;
        PlayerMotor _motor;
        Camera _cam;
        float _timer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!LaunchOptions.Has("-camlog"))
                return;

            var go = new GameObject("Camera Probe");
            go.AddComponent<CameraProbe>();
        }

        void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f)
                return;

            _timer = 0.25f;

            if (_rig == null)
                _rig = FindAnyObjectByType<ThirdPersonCamera>();
            if (_rig == null)
            {
                Debug.Log("[CamLog] ThirdPersonCamera が見つからない");
                return;
            }

            if (_cam == null)
                _cam = _rig.GetComponent<Camera>();
            if (_lock == null)
                _lock = FindAnyObjectByType<BeltScreenLock>();
            if (_motor == null)
                _motor = FindAnyObjectByType<PlayerMotor>();

            Transform target = _rig.Target;
            if (target == null)
            {
                Debug.Log("[CamLog] Target=null（追尾していない）");
                return;
            }

            Vector3 chest = target.position + Vector3.up * 1.0f;
            float vx = -1f;
            float vFeetY = -1f;
            float vHeadY = -1f;
            if (_cam != null)
            {
                Vector3 vp = _cam.WorldToViewportPoint(chest);
                vx = vp.z > 0f ? vp.x : -1f;   // 背後なら -1

                Vector3 feet = _cam.WorldToViewportPoint(target.position + Vector3.up * 0.02f);
                Vector3 head = _cam.WorldToViewportPoint(target.position + Vector3.up * 1.62f);
                vFeetY = feet.z > 0f ? feet.y : -1f;
                vHeadY = head.z > 0f ? head.y : -1f;
            }

            string gate = _rig.GateMinX == _rig.GateMaxX
                ? $"{_rig.GateMaxX:0.00}"
                : $"{Fmt(_rig.GateMinX)}..{Fmt(_rig.GateMaxX)}";

            string lim = _motor != null
                ? $"{Fmt(_motor.BackLimitX)}..{Fmt(_motor.ForwardLimitX)}"
                : "?";

            // いちばん近い敵。ロックの条件（近い / 画面に映っている）を数字で見るため。
            float enemyD = float.PositiveInfinity;
            float enemyVx = -1f;
            var brains = FindObjectsByType<EnemyBrain>(FindObjectsInactive.Exclude);
            for (int i = 0; i < brains.Length; i++)
            {
                var b = brains[i];
                if (b == null || !b.IsAlive)
                    continue;

                Vector3 d = b.transform.position - target.position;
                d.y = 0f;
                float m = d.magnitude;
                if (m >= enemyD)
                    continue;

                enemyD = m;
                enemyVx = -1f;
                if (_cam != null)
                {
                    Vector3 ev = _cam.WorldToViewportPoint(b.transform.position + Vector3.up * 1.0f);
                    enemyVx = ev.z > 0f ? ev.x : -1f;
                }
            }

            Debug.Log(
                $"[CamLog] px={target.position.x:0.00} cx={_rig.CurrentX:0.00} sx={_rig.RenderedX:0.00}" +
                $" vx={vx:0.000} vy=[{vFeetY:0.000}..{vHeadY:0.000}] lock={(_lock != null && _lock.IsLocked ? 1 : 0)} gate={gate} lim={lim}" +
                $" enemyD={(float.IsPositiveInfinity(enemyD) ? -1f : enemyD):0.00} enemyVx={enemyVx:0.000}" +
                $" campos=({_rig.transform.position.x:0.00},{_rig.transform.position.y:0.00},{_rig.transform.position.z:0.00})" +
                $" dist={_rig.Distance:0.00}" +
                $" fwd=({target.forward.x:0.00},{target.forward.z:0.00})" +
                $" pz={target.position.z:0.00}");
        }

        static string Fmt(float v)
        {
            if (float.IsPositiveInfinity(v))
                return "+inf";
            if (float.IsNegativeInfinity(v))
                return "-inf";
            return v.ToString("0.00");
        }
    }
}
