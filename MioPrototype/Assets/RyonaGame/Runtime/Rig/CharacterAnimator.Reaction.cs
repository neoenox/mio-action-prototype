using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class CharacterAnimator : MonoBehaviour
    {
        /// <summary>
        /// 殴ってきた相手の方向を覚えておく。
        /// 攻撃はキャラクターの forward へ出るので、VRM の見た目だけ回すと
        /// 「前を向いているのに攻撃が後ろへ飛ぶ」ことになる。体（root）ごと回す。
        /// </summary>
        /// <summary>
        /// どこから殴られたかを覚えておく（0=正面, ±1=左右, 2=背後）。
        /// 正面からの一撃だけを想定すると、後ろから殴られても「前へ反る」不自然な画になる。
        /// </summary>
        void CaptureHurtSide()
        {
            _hurtSide = 0;

            Transform src = Stats != null ? Stats.LastAttacker : null;
            if (src == null)
                return;

            Vector3 d = src.position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 0.0001f)
                return;

            Vector3 local = transform.InverseTransformDirection(d.normalized);

            // 後方 40° 以内は「背中から」とみなす
            if (local.z < -0.75f)
                _hurtSide = 2;
            else if (Mathf.Abs(local.x) > 0.35f)
                _hurtSide = local.x > 0f ? 1 : -1;
        }

        /// <summary>
        /// 顔を殴られたあとの首の動き。カメラのほうへ顔が弾かれて（0.05 秒）、
        /// しばらく横を向いたまま耐え、ゆっくり正面へ戻る（合計約 0.95 秒）。
        /// 怯みの長さとは別に時間を持つので、軽い一撃でも動きが最後まで見える。
        /// </summary>
        void ApplyFaceFlick(ref float pitch, ref float yaw, ref float roll)
        {
            float t = Time.time - _faceFlickAt;
            if (t < 0f || t > 0.95f)
                return;

            float env = Mathf.SmoothStep(0f, 1f, t / 0.05f)
                        * (1f - Mathf.SmoothStep(0f, 1f, (t - 0.45f) / 0.50f));

            float toCamera = CameraYawToMe();

            yaw = Mathf.Lerp(yaw, StanceHeadYaw + toCamera * 0.95f, env);
            roll += -Mathf.Sign(toCamera == 0f ? 1f : toCamera) * 10f * env;
            pitch -= 6f * env;

            // 振れきった瞬間の角度を 1 回だけ残す（見た目が「横を向いている」かを数字で確かめる）。
            if (!_faceFlickLogged && t >= 0.10f)
            {
                _faceFlickLogged = true;
                if (PlayerStats.StatLogging)
                    Debug.Log($"[FaceFlick] toCam={toCamera:F0} yaw={yaw:F0} " +
                        $"pitch={pitch:F0} roll={roll:F0} t={t:F2}");
            }
        }

        // --- 後ろ拘束で、殴りに来る敵を不安そうに見る -------------------------------

        HeldBellyPunchDirector _punchDirector;

        /// <summary>殴りに来る敵を見る重み（0〜1）。拘束中に正面の敵が選ばれると上がる。</summary>
        float _dreadLook;

        EnemyBrain _dreadEnemy;
        float _dreadYaw;
        float _dreadPitch;
        bool _hasDreadAngles;

        /// <summary>前のフレームで姿勢を組んだ後の頭の位置（ResetPose 前の値）。</summary>
        Vector3 _posedHeadPosition;
        bool _hasPosedHead;

        /// <summary>殴りに来る敵を見ている重み（検証用）。</summary>
        public float DreadLook => _dreadLook;
        internal float HeldFaceCaptorBrace { get; private set; }

        void UpdateDreadLook(float dt)
        {
            if (_punchDirector == null)
                _punchDirector = FindFirstObjectByType<HeldBellyPunchDirector>();

            bool backHold = _punchDirector != null && _punchDirector.isActiveAndEnabled
                && Grapple != null && Grapple.IsGrappled && !Grapple.IsHandingOff
                && Grapple.Kind == GrappleKind.Back;
            EnemyBrain target = backHold ? _punchDirector.DreadEnemy : null;

            bool faceCombo = backHold && _punchDirector.IsFaceCombo;
            HeldFaceCaptorBrace = Mathf.MoveTowards(HeldFaceCaptorBrace, faceCombo ? 1f : 0f,
                Mathf.Max(0f, dt) / 0.14f);

            if (target != _dreadEnemy)
            {
                _dreadEnemy = target;
                _hasDreadAngles = false;
            }

            if (target != null && !_hasDreadAngles && Rig != null && Rig.Head != null)
                CaptureDreadAngles(_punchDirector.DreadStartPoint);

            bool bracing = target != null && target.IsTagPunch &&
                (target.Phase == EnemyPhase.Attack ||
                 (target.Phase == EnemyPhase.Windup &&
                  target.WindupProgress >= (target.IsOpeningBellyPunch ? 0.15f : 0.55f)));
            bool look = target != null && _hasDreadAngles && !bracing;
            // 拳が出る前だけ視線を外す。被弾姿勢から戻るときは最初の角度へ戻す。
            float seconds = look ? 0.18f : bracing ? 0.085f : 0.35f;
            _dreadLook = Mathf.MoveTowards(_dreadLook, look ? 1f : 0f, Mathf.Max(0f, dt) / seconds);
        }

        /// <summary>
        /// 殴りに来る敵の顔へ向く首の角度（体のローカル）。yaw は右 +、pitch は上 −。
        /// 上体の前傾ぶんは呼び出し側で差し引く。
        /// </summary>
        void CaptureDreadAngles(Vector3 point)
        {
            Vector3 to = point - (_hasPosedHead ? _posedHeadPosition : Rig.Head.position);
            Vector3 local = transform.InverseTransformDirection(to);
            float flat = new Vector2(local.x, local.z).magnitude;
            if (flat < 0.05f)
                return;

            _dreadYaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -70f, 70f);
            _dreadPitch = Mathf.Clamp(-Mathf.Atan2(local.y, flat) * Mathf.Rad2Deg, -45f, 25f);
            _hasDreadAngles = true;
        }

        /// <summary>
        /// カメラがどちら側にいるか（度）。正面 0、右 +、左 −。
        /// 顔を「手前（カメラ側）へ背ける」向きを出すのに使う。
        /// </summary>
        float CameraYawToMe()
        {
            var cam = Camera.main;
            if (cam == null)
                return 0f;

            Vector3 to = cam.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude <= 1e-4f)
                return 0f;

            Vector3 local = transform.InverseTransformDirection(to.normalized);
            return Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -80f, 80f);
        }

        void CaptureFaceYaw()
        {
            _hasFaceYaw = false;

            Transform src = Stats != null ? Stats.LastAttacker : null;
            if (src == null)
                return;

            Vector3 d = src.position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 0.0001f)
                return;

            if (Motor != null && Motor.BeltFacing)
                d = Vector3.right * (Mathf.Abs(d.x) > 0.01f ? Mathf.Sign(d.x) : Motor.LaneFacingSign);
            _faceYaw = Quaternion.LookRotation(d.normalized, Vector3.up);
            _hasFaceYaw = true;

            Debug.Log($"[Turn] hit angle={Vector3.Angle(transform.forward, d):F0} " +
                      $"src={src.name}");
        }

    }
}
