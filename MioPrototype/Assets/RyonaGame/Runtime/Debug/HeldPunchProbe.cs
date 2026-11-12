using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 拘束中のタッグ顔面パンチの「動きの量」を毎フレーム数値で出す（<c>-punchlog</c>）。
    ///
    /// 見た目の印象（やる気を感じる／感じない）を、あとから比べられる数値にするための
    /// 計測用。殴る側・殴られる側の両方を、同じフレームで並べて出す。
    ///
    /// 見る値:
    /// <list type="bullet">
    /// <item>殴る側: 拳の速さ・拳の移動距離・肘の伸び・胸のひねり・腰の前進・踏み込み。
    /// 拳が「当たる瞬間に一番速い」かが分かる。</item>
    /// <item>殴られる側: 頭の振れ角・頭の速さ・上体の角度・頭の移動量。</item>
    /// </list>
    ///
    /// 出力は Player.log（Editor は Console）。1 発ごとに <c>[PunchSum]</c>、
    /// コンボ終了時に <c>[PunchCombo]</c> を出す。
    /// </summary>
    [DefaultExecutionOrder(26000)]
    public sealed class HeldPunchProbe : MonoBehaviour
    {
        PlayerStats _stats;
        CharacterAnimator _anim;
        ProceduralRig _rig;

        EnemyBrain _puncher;
        EnemyFigure _figure;
        bool _active;
        int _hitIndex;
        int _comboHits;
        float _lastActiveTime;
        float _lastWindup;
        float _lastLeft;
        float _lastFlick;

        Vector3 _prevFist;
        Vector3 _prevHead;
        Vector3 _hitStartHead;
        float _prevTime = -1f;

        // 1 発ぶんのピーク
        float _peakFistSpeed;
        float _fistTravel;
        float _peakReach;
        float _minElbow;
        float _peakHeadSpeed;
        float _peakHeadYaw;
        float _peakHeadShift;
        float _peakTorsoYaw;
        float _peakChestYaw;
        float _peakHipsForward;
        float _peakLunge;
        float _minFistHead;
        int _frames;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!LaunchOptions.Has("-punchlog"))
                return;

            var go = new GameObject("Held Punch Probe");
            DontDestroyOnLoad(go);
            go.AddComponent<HeldPunchProbe>();
        }

        void LateUpdate()
        {
            if (_stats == null)
            {
                _stats = FindFirstObjectByType<PlayerStats>();
                if (_stats == null)
                    return;
            }

            if (_anim == null)
                _anim = _stats.GetComponentInChildren<CharacterAnimator>();
            if (_rig == null && _anim != null)
                _rig = _anim.Rig;
            if (_anim == null || _rig == null || !_rig.Ready)
                return;

            EnemyBrain found = FindTagPuncher();
            if (found != null)
            {
                // 拳を引き直して次の一発が始まったら、前の一発を締める。
                float windup = found.WindupProgress;
                if (_active && windup + 0.08f < _lastWindup)
                    EndPunch();
                if (!_active || found != _puncher)
                    BeginPunch(found);

                _puncher = found;
                _lastActiveTime = Time.time;
                LogFrame();
                _lastWindup = windup;
            }
            else
            {
                if (_active && Time.time - _lastActiveTime > 0.25f)
                    EndPunch();
                if (_comboHits > 0 && Time.time - _lastActiveTime > 1.2f)
                {
                    Debug.Log($"[PunchCombo] hits={_comboHits}");
                    _comboHits = 0;
                }
            }
            _lastFlick = _anim.HeldFacePunchFlick;
        }

        EnemyBrain FindTagPuncher()
        {
            foreach (EnemyBrain brain in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (brain != null && brain.IsTagPunch && brain.Zone == PunchZone.Face)
                    return brain;
            }

            return null;
        }

        void BeginPunch(EnemyBrain puncher)
        {
            _puncher = puncher;
            _figure = puncher.GetComponent<EnemyFigure>();
            _active = true;
            _hitIndex++;
            _comboHits++;
            _lastFlick = 0f;
            _prevFist = FistPosition(puncher.UsesLeftPunch);
            _hitStartHead = _rig.Head.position;
            _prevHead = _hitStartHead;
            _prevTime = Time.time;
            _peakFistSpeed = 0f;
            _fistTravel = 0f;
            _peakReach = 0f;
            _minElbow = 180f;
            _peakHeadSpeed = 0f;
            _peakHeadYaw = 0f;
            _peakHeadShift = 0f;
            _peakTorsoYaw = 0f;
            _peakChestYaw = 0f;
            _peakHipsForward = 0f;
            _peakLunge = 0f;
            _minFistHead = 99f;
            _frames = 0;
        }

        void EndPunch()
        {
            if (_puncher == null || _figure == null)
            {
                _active = false;
                return;
            }

            float herHead = _rig.Head.position.y;
            Debug.Log($"[PunchSum] hit={_hitIndex} left={_lastLeft} frames={_frames}" +
                $" fistMax={_peakFistSpeed:F2}m/s travel={_fistTravel:F2}m" +
                $" reach={_peakReach:F2}m elbowMin={_minElbow:F0}deg" +
                $" minFistHead={_minFistHead:F2}m" +
                $" chestYaw={_peakChestYaw:F0}deg hipsFwd={_peakHipsForward:F2}m lunge={_peakLunge:F2}m" +
                $" herHeadYaw={_peakHeadYaw:F0}deg herHeadSpeed={_peakHeadSpeed:F2}m/s" +
                $" herHeadShift={_peakHeadShift:F2}m herTorsoYaw={_peakTorsoYaw:F0}deg" +
                $" headY={herHead:F2}");

            _active = false;
            _puncher = null;
            _figure = null;
        }

        Vector3 FistPosition(bool left)
        {
            if (_figure == null)
                return Vector3.zero;
            Transform fist = _figure.Fist(left);
            return fist != null ? fist.position : Vector3.zero;
        }

        void LogFrame()
        {
            if (_figure == null || _puncher == null)
                return;

            bool left = _puncher.UsesLeftPunch;
            _lastLeft = left ? 1f : 0f;

            float now = Time.time;
            float dt = Mathf.Max(1e-4f, now - _prevTime);
            _prevTime = now;
            _frames++;

            Transform fist = _figure.Fist(left);
            Transform upper = _figure.ShoulderBone(left);
            Transform elbow = _figure.ElbowBone(left);
            Transform wrist = _figure.WristBone(left);
            if (fist == null || upper == null || elbow == null || wrist == null)
                return;

            Vector3 fistPos = fist.position;
            Vector3 headPos = _rig.Head.position;
            float fistSpeed = (fistPos - _prevFist).magnitude / dt;
            _fistTravel += (fistPos - _prevFist).magnitude;

            float reach = Vector3.Distance(fistPos, upper.position);
            float elbowAngle = Vector3.Angle(elbow.position - upper.position,
                wrist.position - elbow.position);
            float fistHead = Vector3.Distance(fistPos, headPos);
            _peakFistSpeed = Mathf.Max(_peakFistSpeed, fistSpeed);
            _peakReach = Mathf.Max(_peakReach, reach);
            _minElbow = Mathf.Min(_minElbow, elbowAngle);
            _minFistHead = Mathf.Min(_minFistHead, fistHead);

            // 胸のひねり（腰から見た胸の向き）と、腰の前進・踏み込み。
            float chestYaw = _figure.ChestBone != null
                ? SignedYaw(_puncher.transform.forward, _figure.ChestBone.forward)
                : 0f;
            float hipsForward = _figure.HipsBone != null
                ? Vector3.Dot(_figure.HipsBone.position - _puncher.transform.position,
                    _puncher.transform.forward)
                : 0f;
            float gap = Vector3.Dot(_stats.transform.position - _puncher.transform.position,
                _puncher.transform.forward);
            _peakChestYaw = Mathf.Max(_peakChestYaw, Mathf.Abs(chestYaw));
            _peakHipsForward = Mathf.Max(_peakHipsForward, hipsForward);
            _peakLunge = Mathf.Max(_peakLunge, Mathf.Abs(_figure.LungeOffset));

            // 彼女の頭の振れ・速さ・移動量。
            float headSpeed = (headPos - _prevHead).magnitude / dt;
            float headYaw = _rig.Chest != null
                ? SignedYaw(_rig.Chest.forward, _rig.Head.forward)
                : 0f;
            float headShift = Vector3.Distance(headPos, _hitStartHead);
            _peakHeadSpeed = Mathf.Max(_peakHeadSpeed, headSpeed);
            _peakHeadYaw = Mathf.Max(_peakHeadYaw, Mathf.Abs(headYaw));
            _peakHeadShift = Mathf.Max(_peakHeadShift, headShift);
            _peakTorsoYaw = Mathf.Max(_peakTorsoYaw, Mathf.Abs(_anim.DebugTorsoYaw));

            Debug.Log($"[PunchLog] t={now:F2} phase={_puncher.Phase} windup={_puncher.WindupProgress:F2}" +
                $" left={left} coil={_puncher.TagFaceCoil:F2} drive={_puncher.TagFaceDrive:F2}" +
                $" fistSpeed={fistSpeed:F2} reach={reach:F2} elbow={elbowAngle:F0} fistHead={fistHead:F2}" +
                $" chestYaw={chestYaw:F0} hipsFwd={hipsForward:F2} gap={gap:F2}" +
                $" lunge={_figure.LungeOffset:F2}" +
                $" herHeadYaw={headYaw:F0} herHeadSpeed={headSpeed:F2} herHeadShift={headShift:F2}" +
                $" herTorsoPitch={_anim.DebugTorsoPitch:F0} flick={_anim.HeldFacePunchFlick:F2}" +
                $" impulse={_anim.HeldFacePunchImpulse:F2} brace={_anim.HeldFacePunchBrace:F2}" +
                $" hitstop={HitStop.IsActive}");

            _prevFist = fistPos;
            _prevHead = headPos;
        }

        static float SignedYaw(Vector3 from, Vector3 to)
        {
            from.y = 0f;
            to.y = 0f;
            if (from.sqrMagnitude < 1e-6f || to.sqrMagnitude < 1e-6f)
                return 0f;

            return Vector3.SignedAngle(from.normalized, to.normalized, Vector3.up);
        }
    }
}
