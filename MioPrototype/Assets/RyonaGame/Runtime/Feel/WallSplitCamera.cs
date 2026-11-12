using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 壁の二体がかり（Y 字拘束）の固定ショット。
    /// <para>
    /// 正面ぴったりだと平たく「二人がかり」の圧が読めないので、既定は少し斜め（14°）から 1.8m。彼女の顔と腹（殴られているところ）が入る近さで、二人の肩の間から覗く。
    /// 拘束に入ったときのカメラから動かしたくない場合は <c>-wallsplitcam=hold</c> で
    /// 壁押さえつけのショットをそのまま使う（カットも入らない）。
    /// </para>
    /// <para>
    /// 角度・距離は再ビルドせずに撮り比べられる:
    /// <c>-wallsplitcam=angle|hold|front</c> / <c>-wallsplitcamangle=</c> /
    /// <c>-wallsplitcamdist=</c> / <c>-wallsplitcamheight=</c> /
    /// <c>-wallsplitcamtarget=</c> / <c>-wallsplitcamlat=</c>。
    /// </para>
    /// </summary>
    public static class WallSplitCamera
    {
        public enum Shot
        {
            /// <summary>少し斜め（既定）。</summary>
            Angle,
            /// <summary>壁押さえつけのショットのまま動かさない。</summary>
            Hold,
            /// <summary>正面。</summary>
            Front,
        }

        public static Shot Mode { get { Load(); return _mode; } }
        public static float AngleDeg { get { Load(); return _angleDeg; } }
        public static float Distance { get { Load(); return _distance; } }

        static bool _loaded;
        static Shot _mode = Shot.Angle;
        static float _angleDeg = 14f;
        static float _distance = 1.80f;
        static float _height = 0.40f;
        static float _targetHeight = 1.05f;
        static float _lateral = 0.12f;

        /// <summary>壁押さえつけ（1 体）のカメラ位置。hold でそのまま使う。</summary>
        static readonly Vector3 PinOffset = new Vector3(-2.25f, 0.47f, -1.75f);
        const float PinOffsetHorizontal = 2.8508f;   // |(-2.25, -1.75)|

        static void Load()
        {
            if (_loaded)
                return;
            _loaded = true;

            foreach (string a in System.Environment.GetCommandLineArgs())
            {
                if (TryRead(a, "-wallsplitcam=", out string text))
                {
                    if (text.Equals("hold", System.StringComparison.OrdinalIgnoreCase))
                        _mode = Shot.Hold;
                    else if (text.Equals("front", System.StringComparison.OrdinalIgnoreCase))
                        _mode = Shot.Front;
                    else if (text.Equals("angle", System.StringComparison.OrdinalIgnoreCase))
                        _mode = Shot.Angle;
                }
                else if (TryRead(a, "-wallsplitcamangle=", out float angle))
                    _angleDeg = Mathf.Clamp(angle, 0f, 45f);
                else if (TryRead(a, "-wallsplitcamdist=", out float dist))
                    _distance = Mathf.Clamp(dist, 1.6f, 4.0f);
                else if (TryRead(a, "-wallsplitcamheight=", out float height))
                    _height = Mathf.Clamp(height, 0f, 1.2f);
                else if (TryRead(a, "-wallsplitcamtarget=", out float target))
                    _targetHeight = Mathf.Clamp(target, 0.6f, 1.8f);
                else if (TryRead(a, "-wallsplitcamlat=", out float lateral))
                    _lateral = Mathf.Clamp(lateral, -0.4f, 0.4f);
            }
        }

        static bool TryRead(string arg, string key, out float value)
        {
            value = 0f;
            return arg.StartsWith(key, System.StringComparison.OrdinalIgnoreCase)
                && float.TryParse(arg.Substring(key.Length),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        static bool TryRead(string arg, string key, out string value)
        {
            value = null;
            if (!arg.StartsWith(key, System.StringComparison.OrdinalIgnoreCase))
                return false;
            value = arg.Substring(key.Length).Trim();
            return value.Length > 0;
        }

        /// <summary>
        /// 二体がかりのショットを組む。彼女の顔・両腕と、左右の二人の上半身が同じ画に入る
        /// 距離まで引く（左右の敵は画面端で切れやすい）。
        /// </summary>
        public static void Build(Vector3 herPos, Vector3 herForward,
            Vector3 captorPos, Vector3 assistPos,
            out Vector3 position, out Vector3 target)
        {
            Load();

            Vector3 front = Vector3.ProjectOnPlane(herForward, Vector3.up);
            front = front.sqrMagnitude > 1e-4f ? front.normalized : Vector3.back;
            Vector3 right = Vector3.Cross(Vector3.up, front).normalized;

            Vector3 herChest = herPos + Vector3.up * _targetHeight;
            Vector3 captorChest = captorPos + Vector3.up * 1.02f;
            Vector3 assistChest = assistPos + Vector3.up * 1.02f;

            // 壁押さえつけのショットから動かさない（分割開始時のカットも入れない）。
            if (_mode == Shot.Hold)
            {
                Vector3 herMid = herPos + Vector3.up * 1.02f;
                Vector3 himMid = captorPos + Vector3.up * 1.02f;
                Vector3 mid = Vector3.Lerp(herMid, himMid, 0.45f);
                position = mid + PinOffset;
                target = mid;
                return;
            }

            // 彼女を主役に、二人の胸も混ぜて画の中心を作る。
            target = herChest * 0.70f + (captorChest + assistChest) * 0.15f;

            // 二人の広がりに合わせて引く（近いと画面端で切れる）。
            float spread = Mathf.Abs(Vector3.Dot(captorChest - assistChest, right));
            float dist = _distance + 0.35f * Mathf.Max(0f, spread - 1.6f);

            float angle = _mode == Shot.Front ? 0f : _angleDeg;
            float radians = angle * Mathf.Deg2Rad;

            // 斜めは拘束役と反対（アシスト側）から。打つ腕が手前に来て腹パンが読める。
            float camSide = Mathf.Sign(Vector3.Dot(captorChest - herChest, right));
            if (Mathf.Abs(camSide) < 0.01f)
                camSide = 1f;
            Vector3 direction = front * Mathf.Cos(radians) + right * (Mathf.Sin(radians) * -camSide);

            // 彼女を手前の敵から離れた側へ寄せる（顔と近い側の敵が重ならない）。
            target += right * (-camSide * _lateral);

            position = target + direction * dist + Vector3.up * _height;
        }
    }
}
