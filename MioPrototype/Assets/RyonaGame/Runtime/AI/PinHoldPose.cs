using UnityEngine;
using System.Collections.Generic;
using AttackId = Ryona.Game.EnemyBrain.AttackId;


namespace Ryona.Game
{
    /// <summary>
    /// 彼女の姿勢更新後、正面から両肩を押さえつける腕を実際の体格に合わせる。
    /// 手は肩へ置き、肘は外へ開く（腕を体の前で交差させると「抱きしめている」画になる）。
    /// 押さえつけ中はカメラも少し寄せる。
    /// </summary>
    [DefaultExecutionOrder(21000)]
    public sealed class PinHoldPose : MonoBehaviour
    {
        EnemyFigure _figure;
        EnemyBrain _brain;
        ProceduralRig _targetRig;
        ThirdPersonCamera _camera;
        float _splitPush;

        // Y 字移行中に拘束役の体がレンズを覆わないよう、候補の構図ごとに顔可視率を測る。
        Camera _faceProbe;
        EnemyFigure[] _faceFigures;
        float _nextFigureRefresh;
        static readonly float[] FaceCandidateTurns = { 0f, 28f, -28f, 55f, -55f, 80f, -80f };
        static readonly float[] FaceCandidateScales = { 1f, 1.35f, 1.7f };

        void OnDestroy()
        {
            if (_faceProbe != null)
                Destroy(_faceProbe.gameObject);
        }

        Camera FaceProbe()
        {
            if (_faceProbe == null)
            {
                var go = new GameObject("Wall Pin Face Probe");
                _faceProbe = go.AddComponent<Camera>();
                // 無効カメラは投影行列が更新されず WorldToViewportPoint が壊れる。
                // 何も描画しないカメラとして有効にしておく。
                _faceProbe.enabled = true;
                _faceProbe.cullingMask = 0;
                _faceProbe.clearFlags = CameraClearFlags.Nothing;
                _faceProbe.depth = -100f;
                _faceProbe.useOcclusionCulling = false;
            }
            var main = Camera.main;
            if (main != null)
            {
                _faceProbe.fieldOfView = main.fieldOfView;
                _faceProbe.nearClipPlane = main.nearClipPlane;
                _faceProbe.farClipPlane = main.farClipPlane;
            }
            _faceProbe.aspect = FaceVisibility.Aspect;
            return _faceProbe;
        }

        FaceVisibility.Result ProbeFace(Vector3 position, Vector3 faceTarget)
        {
            if (Time.time >= _nextFigureRefresh)
            {
                _nextFigureRefresh = Time.time + 0.25f;
                _faceFigures = FindObjectsByType<EnemyFigure>(FindObjectsSortMode.None);
            }

            Camera probe = FaceProbe();
            probe.transform.SetPositionAndRotation(position,
                Quaternion.LookRotation(faceTarget - position));
            return FaceVisibility.Measure(probe, _targetRig.Head, _faceFigures);
        }

        /// <summary>
        /// 壁押さえつけ（膝・猶予）のカメラ位置（彼女と拘束役の間の注視点からのずれ、m）。
        /// 以前の高さ 0.47m だと、拘束役の肩と腕越しに見下ろす形になり、彼女の顔が隠れた
        /// （顔の可視率テストで、顔が読めるコマ 41〜72%。隠しているのは全て拘束役）。
        /// 27 通りの候補を測り、見上げすぎずに顔が読める位置（85%）を選んだ
        /// （WallPinRuntimeTests の「調査_壁拘束の膝のカメラ候補ごとの顔の可視率」）。
        /// 再調査（2026-11-15）: y=0.15 だと見上げすぎで顔が隠れる。y=-0.10 に下げると 86% 達成。
        /// </summary>
        static readonly Vector3 WallHeldShot = new Vector3(-2.25f, -0.10f, -1.75f);

        /// <summary>膝が入る瞬間に寄るカメラ位置。高さは WallHeldShot と同じだけ下げる。</summary>
        static readonly Vector3 WallKneeShot = new Vector3(-1.35f, -0.10f, -1.65f);

        /// <summary>もがかれて肩が浮いた分（m）。手の狙いを上げる。</summary>
        float _lift;
        float _rearFraming;

        void LateUpdate()
        {
            if (_figure == null) _figure = GetComponent<EnemyFigure>();
            if (_brain == null) _brain = GetComponent<EnemyBrain>();
            if (_figure == null || _brain == null || _brain.Player == null)
                return;
            if (!_brain.IsPinning)
            {
                _rearFraming = 0f;
                return;
            }

            var hold = _brain.Player.GetComponent<PlayerGrapple>();
            if (hold == null || !hold.IsGrappled || hold.Captor != _brain || hold.Kind != GrappleKind.Pin)
                return;

            if (_targetRig == null)
                _targetRig = _brain.Player.GetComponentInChildren<ProceduralRig>();
            if (_targetRig == null || !_targetRig.Ready ||
                _targetRig.LUpperArm == null || _targetRig.RUpperArm == null)
                return;

            if (hold.IsWallTransition)
            {
                if (hold.WallPhase == WallStage.Grab ||
                    hold.WallPhase == WallStage.Rise)
                {
                    Solve(false, _targetRig.LUpperArm, _targetRig.LLowerArm, _targetRig.LHand, 0f);
                    Solve(true, _targetRig.RUpperArm, _targetRig.RLowerArm, _targetRig.RHand, 0f);
                }
                if (_camera == null)
                    _camera = FindFirstObjectByType<ThirdPersonCamera>();
                if (_camera != null)
                {
                    Vector3 player = _brain.Player.transform.position;
                    Vector3 target = new Vector3(player.x, 1.02f - 0.19f * hold.WallSlideWeight,
                        (player.z + hold.WallFaceZ - 0.4f) * 0.5f);
                    float close = hold.WallPhase == WallStage.Restrain
                        ? Mathf.SmoothStep(0f, 1f, hold.WallPhaseProgress) : 0f;
                    Vector3 offset = Vector3.Lerp(new Vector3(-2.75f, 0.48f, -1.35f),
                        WallHeldShot, close);
                    if (hold.IsWallToss)
                    {
                        float wide = hold.WallPhase == WallStage.TossWindup
                            ? hold.WallTossLower : 1f;
                        target = Vector3.Lerp(player, transform.position, 0.40f);
                        target.y = transform.position.y + Mathf.Lerp(1.02f, 0.86f, wide);
                        offset = Vector3.Lerp(WallHeldShot,
                            new Vector3(-2.70f, 0.52f, -2.20f), wide);
                    }
                    _camera.RequestCinematicShot(target + offset, target);
                }
                return;
            }

            // 折れた胴に沿って腹の正面を取り直し、膝頭の表面が当たる位置に関節を置く。
            if (hold.KneePulse > 0f && _targetRig.Spine != null && _targetRig.Chest != null)
            {
                Vector3 spine = _targetRig.Spine.position;
                Vector3 chest = _targetRig.Chest.position;
                // 腹の正面。壁では**水平**に取る。胴の線（chest - spine）へ射影すると、
                // 彼女が前傾しているときに狙いが下がり、膝が股間の高さへ落ちる（実測: 0.91m）。
                // レーンの押さえつけは従来どおり胴の線基準（既存の合わせを変えない）。
                Vector3 front = hold.IsWallPin
                    ? Vector3.ProjectOnPlane(_brain.Player.transform.forward, Vector3.up)
                    : Vector3.ProjectOnPlane(_brain.Player.transform.forward, chest - spine);
                if (front.sqrMagnitude < 1e-4f)
                    front = Vector3.ProjectOnPlane(_brain.Player.transform.forward, Vector3.up);
                front.Normalize();

                // 腹の表面。膝の関節（骨）ではなく**膝頭の球の表面**を当てる。
                // 関節をそのまま表面に置くと、膝球の半径（h*0.039）ぶん腹へ入って
                // 「膝が貫通している」画になる（実測）。球の半径ぶん手前で止め、
                // 1.5cm だけ沈ませて当たった感じを出す。
                // 壁以外の押さえつけは、この合わせ（関節を表面へ）で従来どおり決まる。
                float surface = 0.085f + _figure.Height * 0.039f;
                float kneeRadius = _figure.Height * 0.039f;
                const float squash = 0.015f;
                float reach = hold.IsWallPin ? surface + kneeRadius - squash : surface;

                // 当てる高さ。腰（spine）の少し上を腹とする。spine と chest の間を
                // そのまま使うと、前傾したときに股間の高さまで下がる（実測 0.91m）。
                Vector3 axis = Vector3.Lerp(spine, chest, 0.60f);
                if (hold.IsWallPin)
                    axis.y = spine.y - 0.03f;
                Vector3 belly = axis + front * reach;

                var cam = Camera.main;
                bool left = cam != null && Vector3.Dot(cam.transform.position - transform.position, transform.right) < 0f;
                float kick = Mathf.SmoothStep(0f, 1f, hold.KneePulse);
                Vector3 preKnee = _figure.KneeBone(left).position;
                _figure.AlignPinKnee(left, belly, kick);

                if (_pinLog && hold.KneePulse > 0.02f)
                {
                    Vector3 axisPoint = Vector3.Lerp(spine, chest, 0.60f);
                    Vector3 knee = _figure.KneeBone(left).position;
                    float depth = Vector3.Dot(knee - axisPoint, front);
                    float preDepth = Vector3.Dot(preKnee - axisPoint, front);
                    float penetration = surface + kneeRadius - depth;
                    GameLog.Log($"[Knee] pulse={hold.KneePulse:F2} depth={depth:F3} pre={preDepth:F3} pen={penetration:F3} " +
                              $"reach={_figure.LastPinKneeReach:F3} shift={_figure.LastPinKneeShift:F3} " +
                              $"hipsY={_figure.HipsBone.position.y - transform.position.y:F2} " +
                              $"kneeY={knee.y:F2} spineY={spine.y:F2} chestY={chest.y:F2} bellyY={belly.y:F2} " +
                              $"surface={surface:F3} kneeR={kneeRadius:F3}");
                }
                // 膝を届かせる骨盤の踏み込みと同じフレームに、跳ね上がる顔から胸と顎を引く。
                _figure.BraceHoldUpperBody(33f * kick, 0f, 20f * kick);
            }

            // 彼は彼女と正面で向かい合っている。彼の右手は彼女の左腕、左手は右腕を掴む。
            // 同じ側（右→右）にすると腕が交差して「左右が逆」に見える（実測）。
            if (!hold.IsWallPin)
            {
                Solve(false, _targetRig.LUpperArm, _targetRig.LLowerArm, _targetRig.LHand, hold.GrabContactWeight);
                Solve(true, _targetRig.RUpperArm, _targetRig.RLowerArm, _targetRig.RHand, hold.GrabContactWeight);
            }

            // もがかれるほど彼女の肩が浮く（押し返されている）。手は毎フレーム腕を追う。
            _lift = 0.12f * Mathf.Clamp01(hold.Progress);

            // 演出中だけ寄る。HoldFocus は「毎フレーム呼んで構図を保つ」API で、
            // PushDistance のように減衰で薄まらない（実測: Push だと 0.28 を指定しても 0.37 までしか寄らなかった）。
            if (_camera == null)
                _camera = FindFirstObjectByType<ThirdPersonCamera>();
            if (_camera != null)
            {
                // 壁押さえつけは、部屋側（-Z）から斜めに覗く固定ショット。
                // レーンのカメラのままだと、壁が奥に隠れて「押し付けている」ことが読めない。
                if (hold.IsWallPin)
                {
                    Transform pinned = _brain.Player.transform;

                    // 二体がかり（Y 字）は二人と彼女が並ぶので、少し斜めから引いた固定ショット。
                    // 角度・距離は WallSplitCamera が起動引数で切り替える
                    // （-wallsplitcam=hold なら壁押さえつけのショットのまま動かさない）。
                    // 壁押さえつけのショットを共通の基準として作る。
                    // Y 字へ移るときはこの画から角度・距離で寄せる（直線補間だと敵の体を通る）。
                    Vector3 her = pinned.position + Vector3.up * 1.02f;
                    Vector3 him = transform.position + Vector3.up * 1.02f;
                    Vector3 heldTarget = Vector3.Lerp(her, him, 0.45f);
                    float knee = Mathf.SmoothStep(0f, 1f, hold.KneePulse);
                    Vector3 heldOffset = Vector3.Lerp(WallHeldShot, WallKneeShot, knee);

                    // 背の低い拘束役は、彼の頭と肩が彼女の顔の高さに来て顔を隠す。
                    // カメラを彼女の側へ寄せて、彼の体の横から顔を覗く。
                    float shortT = _figure != null
                        ? Mathf.InverseLerp(1.79f, 1.72f, _figure.Height)
                        : 0f;
                    heldOffset += new Vector3(0.65f, -0.15f, 0.55f) * shortT;
                    Vector3 heldShot = heldTarget + heldOffset;

                    if (hold.WallSplitWeight > 0.01f)
                    {
                        // アシストが離れた直後（重みが抜ける 0.2 秒）は、拘束役を彼女の反対側へ
                        // 鏡映して代用する。ここで別ショットへ落とすとカメラが跳ねる。
                        Vector3 assist = hold.WallAssist != null
                            ? hold.WallAssist.transform.position
                            : pinned.position + (pinned.position - transform.position);
                        WallSplitCamera.Build(pinned.position, pinned.forward,
                            transform.position, assist,
                            out Vector3 splitShot, out Vector3 splitTarget,
                            CombatConstants.WallGangCameraBack * hold.WallGangWeight,
                            CombatConstants.WallGangCameraShift * hold.WallGangWeight);

                        // 段階が進むほど彼女へ寄る（序盤は引き、締めで大きく寄る）。
                        _splitPush = Mathf.MoveTowards(_splitPush, hold.WallSplitCameraPushTarget,
                            Time.unscaledDeltaTime * 0.7f);
                        float lowering = Mathf.InverseLerp(0.10f, 0.15f, _splitPush);
                        splitTarget.y += 0.03f * lowering;
                        splitShot.y += 0.06f * lowering;
                        splitShot = splitTarget + (splitShot - splitTarget) * (1f - 0.40f * _splitPush);

                        float w = Mathf.Clamp01(hold.WallSplitWeight);
                        Vector3 pivot = pinned.position + Vector3.up * 1.02f;
                        Vector3 from = heldShot - pivot;
                        Vector3 to = splitShot - pivot;
                        Vector3 direction = Vector3.Slerp(
                            from.sqrMagnitude > 1e-6f ? from.normalized : Vector3.back,
                            to.sqrMagnitude > 1e-6f ? to.normalized : Vector3.back, w);
                        float radius = Mathf.Lerp(from.magnitude, to.magnitude, w);
                        Vector3 shot = pivot + direction * radius;
                        Vector3 target = Vector3.Lerp(heldTarget, splitTarget, w);

                        // 角度補間だけだと拘束役の背中を通ることがある（実測 #193〜#197）。
                        // 周囲の候補を顔可視率で採点し、隠れない経路へ逃がす。
                        Vector3 faceTarget = _targetRig.Head.position + Vector3.up * 0.06f;
                        float bestVisible = -1f;
                        Vector3 bestShot = shot;
                        bool avoid = hold.WallGangWeight <= 0.01f;
                        float[] turns = FaceCandidateTurns;
                        for (int i = 0; avoid && i < turns.Length; i++)
                        {
                            for (int j = 0; j < FaceCandidateScales.Length; j++)
                            {
                                Vector3 candidate = pivot +
                                    Quaternion.AngleAxis(turns[i], Vector3.up) * (shot - pivot) * FaceCandidateScales[j];
                                candidate.y += j * 0.35f;
                                var reading = ProbeFace(candidate, faceTarget);
                                float visible = reading.Visible + (reading.Readable ? 0.5f : 0f);
                                if (visible > bestVisible)
                                {
                                    bestVisible = visible;
                                    bestShot = candidate;
                                }
                            }
                        }
                        // 途中の経路がすべて塞がっている場合は、実測で顔が読める確定構図へ逃がす。
                        if (avoid && w >= 0.05f)
                        {
                            var splitReading = ProbeFace(splitShot, faceTarget);
                            float splitVisible = splitReading.Visible + (splitReading.Readable ? 0.5f : 0f);
                            if (splitVisible > bestVisible)
                            {
                                bestVisible = splitVisible;
                                bestShot = splitShot;
                            }
                        }

                        // 追従補間だと避けたはずの敵の体を通ってしまう。回避したフレームは即切替にする。
                        bool dodged = (bestShot - shot).sqrMagnitude > 0.01f;
                        _camera.RequestCinematicShot(bestShot, target, cut: dodged);
                        return;
                    }

                    _splitPush = 0f;
                    _camera.RequestCinematicShot(heldShot, heldTarget);
                    return;
                }

                if (hold.KneePulse > 0f)
                    _kneeCamUntil = Time.time + 0.30f;

                Transform player = _brain.Player.transform;
                float facing = player.forward.x;
                float sign = Mathf.Abs(facing) > 0.1f ? Mathf.Sign(facing) : 1f;

                // 画面の中心を彼女の正面方向へ少しずらす。彼女が画面の左（逆向きなら右）へ寄り、
                // 正面に立つ相手と膝が入る余白ができる。
                float x = player.position.x + sign * 0.30f;

                _rearFraming = Mathf.MoveTowards(_rearFraming, hold.IsPreparingPinHandOff ? 1f : 0f,
                    Time.unscaledDeltaTime * 3.5f);
                if (_rearFraming > 0f)
                {
                    float frame = Mathf.SmoothStep(0f, 1f, _rearFraming);
                    _camera.HoldFocus(Mathf.Lerp(KneeCamDistance, 0.50f, frame), KneeCamPivot,
                        Mathf.Lerp(x, player.position.x, frame), 0.5f,
                        KneeCamPitch, Mathf.Lerp(_kneeCamYaw, -18f, frame) * sign);
                }
                else
                {
                    // 膝が 0.65 秒間隔で続く間は、寄った横の画を保つ。
                    // パルスごとに戻すとカメラが通常の画と往復して落ち着かない（実測）。
                    if (hold.KneePulse > 0.05f)
                    {
                        _kneeShot = Mathf.MoveTowards(_kneeShot, 1f, Time.unscaledDeltaTime / 0.22f);
                        _kneeShotIdle = 0f;
                    }
                    else
                    {
                        _kneeShotIdle += Time.unscaledDeltaTime;
                        if (_kneeShotIdle > 1.2f)
                            _kneeShot = Mathf.MoveTowards(_kneeShot, 0f, Time.unscaledDeltaTime / 0.45f);
                    }

                    // 膝の間は「二人の中点」を横から見る。彼女を中心に回すと、彼が正面 0.7m に
                    // 立っているため、どちらへ回しても彼か彼女が膝を隠す（-45/-70/-90/+20/+50/+80 で実測）。
                    float k = Mathf.SmoothStep(0f, 1f, _kneeShot);
                    if (k > 0.02f)
                    {
                        if (!_kneeShotHeld)
                        {
                            _kneeShotHeld = true;
                            _normalShotPos = _camera.transform.position;
                            _normalShotTarget = player.position + Vector3.up * 1.00f + player.forward * 0.30f;
                        }

                        Vector3 her = player.position + Vector3.up * 0.95f;
                        Vector3 him = transform.position + Vector3.up * 0.95f;
                        Vector3 mid = Vector3.Lerp(her, him, 0.45f);
                        // 顔も同じ画に入れる（カメラは彼女の顔が映ることを前提にする）。
                        // 腹と膝だけに寄ると顔が上で切れる。
                        Vector3 herFace = player.position + Vector3.up * 1.42f;
                        Vector3 axis = Vector3.ProjectOnPlane(him - her, Vector3.up);
                        if (axis.sqrMagnitude < 1e-4f)
                            axis = Vector3.ProjectOnPlane(player.forward, Vector3.up);
                        axis.Normalize();
                        Vector3 side = Vector3.Cross(Vector3.up, axis);
                        if (side.sqrMagnitude < 1e-4f)
                            side = player.right;
                        side.Normalize();
                        // いまカメラがいる側を保つ（跨ぐと画が跳ねる）
                        Vector3 camFlat = Vector3.ProjectOnPlane(_camera.transform.position - mid, Vector3.up);
                        if (Vector3.Dot(side, camFlat) < 0f)
                            side = -side;

                        Vector3 lookAtFace = Vector3.Lerp(herFace, mid, 0.42f);
                        // 真横だと彼女の背中側になり、正面すぎると彼が彼女の顔を覆う。
                        // 二人の関係軸から斜め正面へ寄せ、彼を画面の外側に置いて顔と腹を同時に捉える。
                        Vector3 shot = mid + side * 1.55f + axis * 0.88f + Vector3.up * 0.38f;
                        _camera.RequestCinematicShot(
                            Vector3.Lerp(_normalShotPos, shot, k),
                            Vector3.Lerp(_normalShotTarget, lookAtFace, k));
                    }
                    else
                    {
                        _kneeShotHeld = false;
                        _camera.HoldFocus(0.46f, 0.08f, x, 0.5f, 2f, -22f * sign);
                    }
                }

                if (_pinLog && Time.time >= _pinCamLogAt)
                {
                    _pinCamLogAt = Time.time + 0.2f;
                    var cam = _camera.GetComponent<Camera>();
                    if (cam != null)
                    {
                        Vector3 v = cam.WorldToViewportPoint(player.position + Vector3.up * 1.0f);
                        GameLog.Log($"[PinCenter] near={Time.time < _kneeCamUntil} " +
                                  $"vp=({v.x:F2},{v.y:F2}) focusX={x:F2} beltX={_camera.CurrentX:F2}");
                    }
                }
            }
        }

        // 膝蹴りの演出カメラ。寄りは腹パンと同じ 0.28 だが、彼女は前傾しているので
        // 画面の中心を胸（+0.27）ではなく体の真ん中あたり（+0.05）へ下げ、
        // 頭から腰までが入るようにする。
        const float KneeCamDistance = 0.28f;
        const float KneeCamPivot = 0.05f;
        const float KneeCamPitch = 2f;

        /// <summary>
        /// 膝の瞬間のヨー。彼の胴が膝の通り道を隠すので、横へ回り込む量を調整する。
        /// 符号で回る側が変わる（負で大きくすると彼の背中側へ回って逆効果・実測）。
        /// </summary>
        float _kneeCamYaw = KneeCamYawDefault;
        const float KneeCamYawDefault = -32f;

        /// <summary>膝が続く間、横の画へ寄せ切った度合い（0..1）。</summary>
        float _kneeShot;
        float _kneeShotIdle;
        bool _kneeShotHeld;
        Vector3 _normalShotPos;
        Vector3 _normalShotTarget;

        /// <summary>膝蹴りの寄りを保つ終了時刻。</summary>
        float _kneeCamUntil;

        /// <summary>次の [PinCenter] ログまで（一時診断）。</summary>
        float _pinCamLogAt;

        /// <summary>構図の中心をログに出す（-pinlog）。</summary>
        bool _pinLog;

        void Awake()
        {
            _pinLog = LaunchOptions.Has("-pinlog");
            // 検証用: 膝の瞬間のカメラのヨーを差し替えて撮り比べる。
            if (LaunchOptions.TryFloat("-kneecamyaw", out float yaw))
                _kneeCamYaw = yaw;
        }

        void Solve(bool left, Transform shoulder, Transform lowerArm, Transform hand, float contact)
        {
            var upper = _figure.ShoulderBone(left);
            var elbow = _figure.ElbowBone(left);
            var wrist = _figure.WristBone(left);
            if (upper == null || elbow == null || wrist == null || lowerArm == null)
                return;

            Transform player = _brain.Player.transform;

            // 手は彼女の前腕（手首寄り）を掴む。肩を押さえるより「押さえ込んでいる」形になり、
            // 彼女が腕を振りほどこうとする動きとも噛み合う。
            Vector3 grip = hand != null
                ? Vector3.Lerp(lowerArm.position, hand.position, 0.75f)
                : lowerArm.position;
            Vector3 goal = grip + Vector3.up * 0.02f + _lift * Vector3.up;

            // 肘は外へ開き、少し下へ。真横から見て「押し込んでいる」形にする。
            float side = left ? -1f : 1f;
            Vector3 pole = grip
                + transform.right * (side * 0.55f)
                - Vector3.up * 0.30f;

            goal = Vector3.Lerp(goal, shoulder.position - transform.forward * 0.045f, contact);
            pole = Vector3.Lerp(pole, upper.position + transform.right * (side * 0.25f) - transform.up * 0.24f, contact);
            ProceduralRig.SolveTwoBone(upper, elbow, wrist, goal, pole, transform.right);
            EnemyFigure.SettleElbowHinge(upper, elbow, wrist);
        }
    }
}
