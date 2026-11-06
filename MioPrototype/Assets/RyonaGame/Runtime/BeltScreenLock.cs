using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// ベルト（横スクロール）の画面ロック。ファイナルファイトと同じ作り。
    /// <para>
    /// 敵が生きているあいだは、カメラもプレイヤーも 1 画面ぶんの範囲から出られない。
    /// 全滅させるとロックが外れ、右へ歩いて次の画面まで進める。
    /// これがないと、走って敵を置き去りにできてしまい、ただの横移動になる。
    /// </para>
    /// <para>
    /// ロックするのは「戦いが画面に入ったとき」だけ。遠くに敵が湧いているだけで
    /// カメラを止めると、歩いても画面が付いてこない（実測でそうなっていた）。
    /// また、歩ける幅は固定値ではなくカメラの投影から測る。奥行きによって
    /// 見える幅は変わるので、固定値だと画面外まで歩けてしまう。
    /// </para>
    /// </summary>
    [AddComponentMenu("Ryona/Belt Screen Lock")]
    public sealed class BeltScreenLock : MonoBehaviour
    {
        [Header("References")]
        public ThirdPersonCamera Camera;
        public PlayerMotor Motor;
        public EnemyGroup Group;

        [Header("Tuning")]
        [Tooltip("画面の半幅（m）。カメラから測れないときだけ使う予備値")]
        public float HalfWidth = 3.2f;

        [Tooltip("画面の端からこれだけ内側まで歩ける（m）。端で体が切れない余地")]
        public float EdgeMargin = 0.9f;

        [Tooltip("ロック中にカメラが動ける範囲（m）。ロックした位置を中心に ±この値。" +
                 "構図づくりの余裕で、彼女が歩ける幅とは別物")]
        public float GateRange = 3.0f;

        [Tooltip("敵の生存を確認する間隔（秒）")]
        public float ScanSeconds = 0.1f;

        [Header("Engage (ロックを始める条件)")]
        [Tooltip("この距離以内に生きている敵がいれば、画面に映っていなくてもロックする（m）")]
        public float EngageRange = 4.5f;

        [Tooltip("画面の端からこの割合だけ外にいる敵もロックの対象にする（0 = 画面に入ってからロック）")]
        public float EngageScreenMargin = 0f;

        float _lockX = float.NaN;
        float _nextScan;
        bool _alive;
        Camera _cam;

        /// <summary>いま画面がロックされているか。</summary>
        public bool IsLocked => !float.IsNaN(_lockX);

        /// <summary>ロック中の画面の中心 X。</summary>
        public float LockX => _lockX;

        void LateUpdate()
        {
            if (Time.time >= _nextScan)
            {
                _nextScan = Time.time + Mathf.Max(0.02f, ScanSeconds);
                _alive = AnyEngaged();
            }

            if (!_alive)
            {
                _lockX = float.NaN;
            }
            else if (float.IsNaN(_lockX))
            {
                // 戦いが始まった場所で画面を固定する。彼女を画面の中央に置いてから
                // 止めないと、端に寄ったまま固まって見える。
                _lockX = PlayerX();
                if (Camera != null)
                    Camera.SnapTo(_lockX);

                Debug.Log($"[Screen] lock x={_lockX:F2}");
            }

            // プレイヤーを縛るのは「戦いの中心（ロックした位置）」。
            // カメラの実位置を使うと、カメラが彼女を追いかけた分だけ彼女も前に進めて
            // しまい、1m 歩いた所で止まる（自分の位置で自分を縛る形になる）。
            // 縛る範囲（1画面）と、カメラが動ける範囲（構図づくりの余裕）は別物。
            if (IsLocked)
            {
                float center = _lockX;

                // 歩ける幅は毎回カメラから測り直す。奥行き（z）が変わると
                // 画面に映る幅も変わるので、固定値では端からはみ出す。
                float half = Camera != null
                    ? Camera.ScreenHalfWidth(PlayerZ())
                    : HalfWidth;

                float inner = Mathf.Max(0.2f, half - Mathf.Max(0f, EdgeMargin));

                if (Motor != null)
                {
                    Motor.ForwardLimitX = center + inner;
                    Motor.BackLimitX = center - inner;
                }

                if (Camera != null)
                {
                    // ここを固定点（min == max）にすると、カメラが 1mm も動けず
                    // BeltLookAhead が clamp で潰れる。実測で彼女が画面の 71% に寄り、
                    // 左半分が空いていた（vx=0.712 / enemyVx=0.884）。
                    // 範囲にして追従を許すと、LookAhead と DeadZone の差だけ
                    // 彼女が中心より左に置かれる（vx 0.35〜0.40）。
                    float range = Mathf.Max(0.5f, GateRange);
                    Camera.GateMaxX = _lockX + range;
                    Camera.GateMinX = _lockX - range;
                }
            }
            else
            {
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
            }
        }

        float PlayerX()
        {
            Transform p = PlayerTransform();
            return p != null ? p.position.x : transform.position.x;
        }

        float PlayerZ()
        {
            Transform p = PlayerTransform();
            return p != null ? p.position.z : 0f;
        }

        Transform PlayerTransform()
        {
            if (Motor != null)
                return Motor.transform;
            if (Camera != null)
                return Camera.Target;
            return null;
        }

        /// <summary>
        /// 画面ロックを始めるべき敵がいるか。
        /// 「生きている」だけでは遠くの敵でロックしてしまうので、
        /// 近い（<see cref="EngageRange"/> 以内）か、画面に映っている敵だけを見る。
        /// </summary>
        bool AnyEngaged()
        {
            if (Group == null)
                return false;

            // 起動直後はカメラの向きがまだ置かれていない。この段階の投影で
            // 「画面に映っている」と判定すると、遠くの敵でロックしてしまう（実測）。
            if (Camera != null && !Camera.Framed)
                return false;

            Transform player = PlayerTransform();
            if (player == null)
                return false;

            var members = Group.Members;
            for (int i = 0; i < members.Count; i++)
            {
                var brain = members[i];
                if (brain == null || !brain.IsAlive)
                    continue;

                Vector3 d = brain.transform.position - player.position;
                d.y = 0f;
                if (d.magnitude <= Mathf.Max(0f, EngageRange))
                    return true;

                if (OnScreen(brain.transform.position + Vector3.up * 1.0f))
                    return true;
            }

            return false;
        }

        /// <summary>その点が画面に入っているか（端の外側 <see cref="EngageScreenMargin"/> まで許す）。</summary>
        bool OnScreen(Vector3 world)
        {
            if (_cam == null && Camera != null)
                _cam = Camera.GetComponent<Camera>();
            if (_cam == null)
                return false;

            float m = Mathf.Clamp01(EngageScreenMargin);
            Vector3 v = _cam.WorldToViewportPoint(world);

            return v.z > 0f &&
                   v.x >= -m && v.x <= 1f + m &&
                   v.y >= -m && v.y <= 1f + m;
        }
    }
}
