using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// アリーナ（ウェーブ）の画面ロック。中心は <see cref="LockAt"/> で外から決める。
    /// <para>
    /// 歩ける範囲はカメラの投影から毎回測る。奥行きにより見える幅が変わるため、
    /// 固定値だと画面外まで歩けてしまう。カメラのゲート幅は別物で、構図づくりの
    /// 僅かな追従を許す（min == max にすると LookAhead が潰れる実測あり）。
    /// </para>
    /// </summary>
    [AddComponentMenu("Ryona/Belt Screen Lock")]
    public sealed class BeltScreenLock : ConfigurableMonoBehaviour
    {
        [Header("References")]
        public ThirdPersonCamera Camera;
        public PlayerMotor Motor;

        [Header("Tuning")]
        [Tooltip("画面の半幅（m）。カメラから測れないときだけ使う予備値")]
        [System.NonSerialized] public float HalfWidth = 3.2f;

        [Tooltip("画面の端からこれだけ内側まで歩ける（m）。端で体が切れない余地")]
        [System.NonSerialized] public float EdgeMargin = 0.5f;

        [Tooltip("ロック中にカメラが動ける範囲（m）")]
        [System.NonSerialized] public float GateRange = 0.5f;

        float _lockX = float.NaN;
        bool _widthLogged;

        /// <summary>いまアリーナがロックされているか。</summary>
        public bool IsLocked => !float.IsNaN(_lockX);

        /// <summary>ロック中のアリーナ中心 X。</summary>
        public float LockX => _lockX;

        /// <summary>アリーナをロックする。initial=true のときだけカメラを即座に中心へ寄せる。</summary>
        public void LockAt(float centerX, bool initial = false)
        {
            _lockX = centerX;
            _widthLogged = false;
            if (initial && Camera != null)
                Camera.SnapTo(_lockX);
            GameLog.Log($"[Screen] lock x={_lockX:F2} initial={initial}");
        }

        /// <summary>ロックを外し、歩行とカメラの制限を戻す。</summary>
        public void Release()
        {
            if (!IsLocked)
                return;

            _lockX = float.NaN;
            if (Motor != null)
            {
                Motor.ForwardLimitX = float.PositiveInfinity;
                Motor.BackLimitX = float.NegativeInfinity;
            }
            if (Camera != null)
            {
                Camera.GateMaxX = float.PositiveInfinity;
                Camera.GateMinX = float.NegativeInfinity;
            }
            GameLog.Log("[Screen] unlock");
        }

        /// <summary>調整値は <see cref="RyonaConfig.BeltLock"/> が真実源。シーンの保存値は使わない。</summary>
        void ApplySettings(RyonaConfig.BeltLockSettings s)
        {
            HalfWidth = s.HalfWidth;
            EdgeMargin = s.EdgeMargin;
            GateRange = s.GateRange;
        }

        protected override void Awake()
        {
            ApplySettings(RyonaConfig.Instance.BeltLock);
        }

        public override void OnConfigChanged(RyonaConfig config)
        {
            ApplySettings(config.BeltLock);
            GameLog.Log("[BeltScreenLock] Config hot-reloaded");
        }

        void LateUpdate()
        {
            if (!IsLocked)
                return;

            float center = _lockX;

            // 歩ける幅は毎回カメラから測り直す。奥行き（z）が変わると
            // 画面に映る幅も変わるので、固定値では端からはみ出す。
            float half = Camera != null
                ? Camera.ScreenHalfWidth(PlayerZ())
                : HalfWidth;
            float inner = Mathf.Max(0.2f, half - Mathf.Max(0f, EdgeMargin));
            if (!_widthLogged)
            {
                _widthLogged = true;
                GameLog.Log($"[Screen] walkable={2f * inner:F2}m half={half:F2} margin={EdgeMargin:F2}");
            }

            if (Motor != null)
            {
                Motor.ForwardLimitX = center + inner;
                Motor.BackLimitX = center - inner;
            }

            if (Camera != null)
            {
                float range = Mathf.Max(0.5f, GateRange);
                Camera.GateMaxX = center + range;
                Camera.GateMinX = center - range;
            }
        }

        float PlayerZ()
        {
            Transform p = Motor != null ? Motor.transform
                : (Camera != null ? Camera.Target : null);
            return p != null ? p.position.z : 0f;
        }
    }
}

