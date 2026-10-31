using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 入力の薄いラッパ。新 Input System と旧 Input Manager の両方で動く。
    /// どちらが有効かは Project Settings の Active Input Handling で決まる。
    /// </summary>
    public static class InputReader
    {
        /// <summary>
        /// 自動検証用の入力注入。キーを押せない環境（バッチ起動・自動撮影）で
        /// 「歩き」「ガード」「攻撃」を再現する。-holdmove / -holdguard / -holdattack で立つ。
        /// </summary>
        public static bool ForceMove { get; private set; }

        /// <summary>ベルト（横スクロール）の確認用に、右（+X）へ歩き続ける（<c>-holdright</c>）。</summary>
        public static bool ForceRight { get; private set; }
        public static bool ForceGuard { get; private set; }

        /// <summary>弱 → 強 → 崩し を順番に自動で振る（攻撃の通し確認用）。</summary>
        public static bool ForceAttack { get; private set; }

        /// <summary>弱だけを押し続ける（3段コンボの確認用）。</summary>
        public static bool ForceLightOnly { get; private set; }

        /// <summary>弱を必ず左で打つ（2段目の姿勢確認用。-leftpunch）。</summary>
        public static bool ForceLeftPunch { get; private set; }

        static float _autoAttackTimer = 1.2f;
        static float _autoAttackInterval = 1.6f;
        static int _autoAttackStep;
        static bool _autoAttackPending;

        static readonly string[] AutoAttackNames = { "light", "heavy", "break" };

        /// <summary>
        /// 自動攻撃のタイマーを毎フレーム進めるためだけの部品。
        /// PlayerCombat は「硬直中・被弾中」に早期 return するので、
        /// そこから呼ぶと時間が進まない。専用の駆動役を置く。
        /// </summary>
        sealed class AutoAttackDriver : MonoBehaviour
        {
            void Update() => TickAutoAttack();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallAutoAttackDriver()
        {
            if (!ForceAttack && !ForceLightOnly)
                return;

            var go = new GameObject("Auto Attack Driver");
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<AutoAttackDriver>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ReadDebugArgs()
        {
            var args = System.Environment.GetCommandLineArgs();
            foreach (string a in args)
            {
                if (string.Equals(a, "-holdmove", System.StringComparison.OrdinalIgnoreCase))
                    ForceMove = true;
                else if (string.Equals(a, "-holdright", System.StringComparison.OrdinalIgnoreCase))
                    ForceRight = true;
                else if (string.Equals(a, "-holdguard", System.StringComparison.OrdinalIgnoreCase))
                    ForceGuard = true;
                else if (string.Equals(a, "-holdlight", System.StringComparison.OrdinalIgnoreCase))
                    ForceLightOnly = true;
                else if (string.Equals(a, "-leftpunch", System.StringComparison.OrdinalIgnoreCase))
                    ForceLeftPunch = true;
                else if (string.Equals(a, "-holdattack", System.StringComparison.OrdinalIgnoreCase))
                    ForceAttack = true;
                else if (string.Equals(a, "-holdmash", System.StringComparison.OrdinalIgnoreCase))
                    ForceMash = true;
                else if (a.StartsWith("-attackinterval=", System.StringComparison.OrdinalIgnoreCase))
                {
                    // 自動攻撃の間隔（秒）。硬直の食い込みを試すときに短くする。
                    if (float.TryParse(a.Substring("-attackinterval=".Length),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float interval)
                        && interval > 0.05f)
                        _autoAttackInterval = interval;
                }
            }

            if (ForceMove || ForceRight || ForceGuard || ForceAttack || ForceLightOnly)
                Debug.Log($"[Input] 強制入力 move={ForceMove} right={ForceRight} guard={ForceGuard} attack={ForceAttack} " +
                    $"interval={_autoAttackInterval:F2}");
        }

        /// <summary>1.6 秒ごとに「次に撃つ攻撃」を 1 つ予約する。</summary>
        static void TickAutoAttack()
        {
            if (!ForceAttack && !ForceLightOnly)
                return;

            _autoAttackTimer -= Time.deltaTime;
            if (_autoAttackTimer > 0f)
                return;

            _autoAttackTimer = _autoAttackInterval;
            _autoAttackPending = true;
        }

        /// <summary>
        /// 予約された攻撃が「今の種類」と一致したときだけ消費する。
        /// 硬直や被弾で押せない間は予約を保持し、空いた最初のフレームで撃つ。
        /// </summary>
        static bool AutoPressed(int kind)
        {
            if ((!ForceAttack && !ForceLightOnly) || !_autoAttackPending)
                return false;

            int want = ForceLightOnly ? 0 : _autoAttackStep % AutoAttackNames.Length;
            if (want != kind)
                return false;

            _autoAttackPending = false;
            _autoAttackStep++;

            Debug.Log($"[Input] auto attack {AutoAttackNames[want]}");
            return true;
        }

        public static Vector2 Move()
        {
            if (ForceMove)
                return Vector2.up;

            if (ForceRight)
                return Vector2.right;

            Vector2 v = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) v.x -= 1f;
            }

            // パッド対応。左スティックの遊び（0.2）を切ってから足す。
            // キーボードとパッドを同時に使っても壊れないよう、最後に長さ 1 へ丸める。
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (pad != null)
            {
                Vector2 stick = pad.leftStick.ReadValue();
                if (stick.sqrMagnitude > 0.04f)
                    v += stick;
            }

            if (kb != null || pad != null)
                return Vector2.ClampMagnitude(v, 1f);
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            v.x = Input.GetAxisRaw("Horizontal");
            v.y = Input.GetAxisRaw("Vertical");
#endif

            return v;
        }

        public static bool BlockHeld()
        {
            if (ForceGuard)
                return true;

#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.leftShiftKey.isPressed)
                return true;

            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (pad != null && (pad.leftTrigger.ReadValue() > 0.4f || pad.leftShoulder.isPressed))
                return true;

            if (kb != null || pad != null)
                return false;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(KeyCode.LeftShift);
#else
            return false;
#endif
        }

        public static bool LightPressed()
        {
            return AutoPressed(0) || KeyDown(KeyCode.J) || PadDown(0);
        }

        public static bool HeavyPressed()
        {
            return AutoPressed(1) || KeyDown(KeyCode.K) || PadDown(1);
        }

        public static bool BreakPressed()
        {
            return AutoPressed(2) || KeyDown(KeyCode.L) || PadDown(2);
        }

        public static bool RestartPressed() => KeyDown(KeyCode.R) || PadDown(3);

        /// <summary>
        /// パッドのボタン。0=下(A) 1=左(X) 2=上(Y) 3=スタート。
        /// 直接 Gamepad.current を見るので、キーボードが繋がっていなくても動く。
        /// </summary>
        static bool PadDown(int button)
        {
#if ENABLE_INPUT_SYSTEM
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (pad == null)
                return false;

            switch (button)
            {
                case 0: return pad.buttonSouth.wasPressedThisFrame;
                case 1: return pad.buttonWest.wasPressedThisFrame;
                case 2: return pad.buttonNorth.wasPressedThisFrame;
                case 3: return pad.startButton.wasPressedThisFrame;
                default: return false;
            }
#else
            return false;
#endif
        }

        /// <summary>自動検証用のレバガチャ（<c>-holdmash</c>）。掴まれたときの振りほどきを再現する。</summary>
        public static bool ForceMash { get; private set; }

        static float _nextMash;

        /// <summary>
        /// 掴まれた状態でのレバガチャ。攻撃キー（J/K/L）とスペースのどれでも 1 回と数える。
        /// 左右移動キーは移動とぶつかるので数えない。
        /// </summary>
        public static bool StrugglePressed()
        {
            if (AutoMash())
                return true;

            return LightPressed() || HeavyPressed() || BreakPressed() || KeyDown(KeyCode.Space);
        }

        static bool AutoMash()
        {
            if (!ForceMash)
                return false;

            if (Time.unscaledTime < _nextMash)
                return false;

            _nextMash = Time.unscaledTime + 0.11f;   // 1 秒に約 9 回
            return true;
        }

        /// <summary>マウスの移動量。カメラ回転に使う。</summary>
        public static Vector2 LookDelta()
        {
#if ENABLE_INPUT_SYSTEM
            Vector2 delta = Vector2.zero;

            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
                delta += mouse.delta.ReadValue();

            // 右スティック。カメラ側はマウスのピクセル量を前提にしているので、
            // 同じ桁になるよう 12 倍して渡す（-1..1 の生値では動かない）。
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (pad != null)
            {
                Vector2 stick = pad.rightStick.ReadValue();
                if (stick.sqrMagnitude > 0.04f)
                    delta += stick * 12f;
            }

            if (mouse != null || pad != null)
                return delta;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#else
            return Vector2.zero;
#endif
        }

        static bool KeyDown(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null)
                return false;

            UnityEngine.InputSystem.Controls.KeyControl control = null;
            switch (key)
            {
                case KeyCode.J: control = kb.jKey; break;
                case KeyCode.K: control = kb.kKey; break;
                case KeyCode.L: control = kb.lKey; break;
                case KeyCode.R: control = kb.rKey; break;
                case KeyCode.Space: control = kb.spaceKey; break;
            }
            return control != null && control.wasPressedThisFrame;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(key);
#else
            return false;
#endif
        }
    }
}
