using System.IO;
using Ryona.Game;
using UniVRM10;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using AttackId = Ryona.Game.EnemyBrain.AttackId;

namespace Ryona.EditorTools
{
    public static partial class CharacterPreview
    {
        /// <summary>
        /// 指を曲げる軸が左右で正しいかを実測する。
        /// 正しい軸なら「指先が手のひら（手首）に近づく」＝距離が縮む。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Scan Finger Axis")]
        public static void ScanFingerAxis()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
            if (!File.Exists(vrmPath))
            {
                Debug.LogError($"[Ryona] VRM not found: {vrmPath}");
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Vrm10Instance vrm = Vrm10
                .LoadPathAsync(vrmPath, canLoadVrm0X: true, showMeshes: true)
                .GetAwaiter().GetResult();

            if (vrm == null)
            {
                Debug.LogError("[Ryona] VRM load returned null");
                return;
            }

            var rig = vrm.gameObject.AddComponent<ProceduralRig>();
            rig.Capture();
            var anim = vrm.gameObject.AddComponent<CharacterAnimator>();
            anim.Rig = rig;
            anim.ForceState(CharacterAnimator.State.Idle, 0.5f, 2f, StaggerTier.None);
            anim.ApplyPose();

            var at = vrm.GetComponent<Animator>();

            var axes = new (string name, Vector3 v)[]
            {
                ("+X", Vector3.right), ("-X", Vector3.left),
                ("+Y", Vector3.up), ("-Y", Vector3.down),
                ("+Z", Vector3.forward), ("-Z", Vector3.back),
            };

            foreach (bool left in new[] { false, true })
            {
                var hand = at.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                var tip = at.GetBoneTransform(left ? HumanBodyBones.LeftMiddleDistal : HumanBodyBones.RightMiddleDistal);
                if (hand == null || tip == null)
                {
                    Debug.LogError("[FingerAxis] 指ボーンが見つからない");
                    return;
                }

                rig.CurlFingers(left, 0f, Vector3.forward);
                float open = Vector3.Distance(tip.position, hand.position);

                string best = "";
                float bestRatio = 99f;

                foreach (var a in axes)
                {
                    rig.CurlFingers(left, 1f, a.v);
                    float closed = Vector3.Distance(tip.position, hand.position);
                    float ratio = closed / Mathf.Max(0.0001f, open);
                    Debug.Log($"[FingerAxis] {(left ? "L" : "R")} {a.name}: open={open:F4} closed={closed:F4} ratio={ratio:F2}");
                    if (ratio < bestRatio)
                    {
                        bestRatio = ratio;
                        best = a.name;
                    }
                }

                Debug.Log($"[FingerAxis] {(left ? "L" : "R")} BEST = {best} (ratio {bestRatio:F2})");
            }

            Object.DestroyImmediate(vrm.gameObject);
        }

        /// <summary>
        /// 真上から見て、左右の手の前後関係を確認する。
        /// 画像上は「上が前（+Z）、右が世界の +X」。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Hand Top View")]
        public static void CaptureHandTopView()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
            if (!File.Exists(vrmPath))
            {
                Debug.LogError($"[Ryona] VRM not found: {vrmPath}");
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLights();

            Vrm10Instance vrm = Vrm10
                .LoadPathAsync(vrmPath, canLoadVrm0X: true, showMeshes: true)
                .GetAwaiter().GetResult();

            if (vrm == null)
            {
                Debug.LogError("[Ryona] VRM load returned null");
                return;
            }

            var rig = vrm.gameObject.AddComponent<ProceduralRig>();
            rig.Capture();
            var anim = vrm.gameObject.AddComponent<CharacterAnimator>();
            anim.Rig = rig;

            foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
                smr.forceMatrixRecalculationPerRender = true;

            var bounds = ComputeBounds(vrm.gameObject);
            float h = Mathf.Max(1f, bounds.size.y);

            var camGo = new GameObject("Top Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.05f;

            var shots = new (CharacterAnimator.State state, float time, AttackData attack, string label)[]
            {
                (CharacterAnimator.State.Idle, 0.5f, null, "idle"),
                (CharacterAnimator.State.Guard, 0.5f, null, "guard"),
                (CharacterAnimator.State.Attack, 0.10f, AttackLibrary.Light(), "light 当たり"),
                (CharacterAnimator.State.Attack, 0.20f, AttackLibrary.Heavy(), "heavy 当たり"),
                (CharacterAnimator.State.Attack, 0.30f, AttackLibrary.BreakAttack(), "break 当たり"),
            };

            const int cellW = 360;
            const int cellH = 420;

            var sheet = new Texture2D(cellW * shots.Length, cellH, TextureFormat.RGB24, false);

            for (int i = 0; i < shots.Length; i++)
            {
                var s = shots[i];
                anim.AttackOverride = s.attack;
                anim.ForceState(s.state, s.time, 1.2f, StaggerTier.None);
                anim.ApplyPose();

                cam.transform.position = new Vector3(0f, h * 2.6f, 0.15f);
                cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

                var cell = RenderCell(cam, cellW, cellH);
                sheet.SetPixels(i * cellW, 0, cellW, cellH, cell.GetPixels());
                Object.DestroyImmediate(cell);

                Vector3 chest = rig.Chest.position;
                Vector3 hr = vrm.transform.InverseTransformPoint(rig.RHand.position);
                Vector3 hl = vrm.transform.InverseTransformPoint(rig.LHand.position);
                Vector3 cr = vrm.transform.InverseTransformPoint(chest);

                Debug.Log($"[Hands] {s.label}: R(x={hr.x:F3} z={hr.z:F3}) L(x={hl.x:F3} z={hl.z:F3}) " +
                          $"chestZ={cr.z:F3} Rforward={hr.z - cr.z:F3} Lforward={hl.z - cr.z:F3}");
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "hand_top.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] hand top view: {outPath}");
        }

        /// <summary>
        /// 全状態を 0.02 秒刻みで走査し、腕が真横に伸びる（T ポーズに見える）瞬間を探す。
        /// 画像では 1 コマしか写らないので、数値で全フレームを見る。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Scan Arm Anomalies")]
        public static void ScanArmAnomalies()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
            if (!File.Exists(vrmPath))
            {
                Debug.LogError($"[Ryona] VRM not found: {vrmPath}");
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Vrm10Instance vrm = Vrm10
                .LoadPathAsync(vrmPath, canLoadVrm0X: true, showMeshes: true)
                .GetAwaiter().GetResult();

            if (vrm == null)
            {
                Debug.LogError("[Ryona] VRM load returned null");
                return;
            }

            var rig = vrm.gameObject.AddComponent<ProceduralRig>();
            rig.Capture();
            var anim = vrm.gameObject.AddComponent<CharacterAnimator>();
            anim.Rig = rig;

            var cases = new (string name, CharacterAnimator.State state, AttackData attack, StaggerTier tier, float duration)[]
            {
                ("idle", CharacterAnimator.State.Idle, null, StaggerTier.None, 2.0f),
                ("walk", CharacterAnimator.State.Walk, null, StaggerTier.None, 2.0f),
                ("light", CharacterAnimator.State.Attack, AttackLibrary.Light(), StaggerTier.None, 0.8f),
                ("heavy", CharacterAnimator.State.Attack, AttackLibrary.Heavy(), StaggerTier.None, 0.9f),
                ("break", CharacterAnimator.State.Attack, AttackLibrary.BreakAttack(), StaggerTier.None, 1.0f),
                ("stagger-s", CharacterAnimator.State.Stagger, null, StaggerTier.Small, 0.5f),
                ("stagger-m", CharacterAnimator.State.Stagger, null, StaggerTier.Medium, 0.7f),
                ("stagger-l", CharacterAnimator.State.Stagger, null, StaggerTier.Large, 0.9f),
                ("down", CharacterAnimator.State.Down, null, StaggerTier.GuardBreak, 1.4f),
                ("guard", CharacterAnimator.State.Guard, null, StaggerTier.None, 1.0f),
            };

            int totalBad = 0;

            foreach (var c in cases)
            {
                int bad = 0;
                float worst = 0f;
                float worstTime = 0f;
                string worstSide = "";
                float worstY = 0f;

                for (float t = 0f; t <= c.duration; t += 0.02f)
                {
                    anim.AttackOverride = c.attack;
                    anim.ForceState(c.state, t, Mathf.Max(0.1f, c.duration), c.tier);
                    anim.ForceGuardRecoil(c.state == CharacterAnimator.State.Guard ? 0f : 0f);
                    anim.ApplyPose();

                    if (!CheckSide(rig, true, out float lateralL, out float dyL))
                    {
                        bad++;
                        if (lateralL > worst)
                        {
                            worst = lateralL;
                            worstTime = t;
                            worstSide = "L";
                            worstY = dyL;
                        }
                    }

                    if (!CheckSide(rig, false, out float lateralR, out float dyR))
                    {
                        bad++;
                        if (lateralR > worst)
                        {
                            worst = lateralR;
                            worstTime = t;
                            worstSide = "R";
                            worstY = dyR;
                        }
                    }
                }

                totalBad += bad;

                if (bad > 0)
                    Debug.LogWarning($"[ArmScan] {c.name}: BAD {bad} samples / worst {worstSide} " +
                                     $"t={worstTime:F2} lateral={worst:F3} dy={worstY:F3}");
                else
                    Debug.Log($"[ArmScan] {c.name}: ok");
            }

            Object.DestroyImmediate(vrm.gameObject);
            Debug.Log($"[Ryona] arm scan done. badSamples={totalBad}");
        }

        /// <summary>
        /// 歩行中に腕が構えの高さを保っているかを測る。
        /// 腕を下ろすと「構えていた人が急に歩き出した」ように見えるので、
        /// 手の高さと前への張り出しがアイドルの構えと揃っているかを見る。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Scan Walk Guard")]
        public static void ScanWalkGuard()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
            if (!File.Exists(vrmPath))
            {
                Debug.LogError($"[Ryona] VRM not found: {vrmPath}");
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Vrm10Instance vrm = Vrm10
                .LoadPathAsync(vrmPath, canLoadVrm0X: true, showMeshes: true)
                .GetAwaiter().GetResult();

            if (vrm == null)
            {
                Debug.LogError("[Ryona] VRM load returned null");
                return;
            }

            var rig = vrm.gameObject.AddComponent<ProceduralRig>();
            rig.Capture();
            var anim = vrm.gameObject.AddComponent<CharacterAnimator>();
            anim.Rig = rig;

            // アイドル＝構え。これを基準にする
            anim.ForceState(CharacterAnimator.State.Idle, 0.6f, 2f, StaggerTier.None, false);
            anim.ApplyPose();

            float idleHeadY = rig.Head.position.y;
            float idleDropL = idleHeadY - rig.LHand.position.y;
            float idleDropR = idleHeadY - rig.RHand.position.y;
            float idleHipsY = rig.Hips.position.y;
            float idleFootLx = rig.LFoot.position.x;
            float idleFootRx = rig.RFoot.position.x;
            float idleHeadYaw = Vector3.SignedAngle(rig.Hips.forward, rig.Head.forward, Vector3.up);

            Debug.Log($"[WalkGuard] idle dropL={idleDropL:F3} dropR={idleDropR:F3} " +
                      $"frontL={rig.LHand.position.z - rig.Hips.position.z:F3} " +
                      $"frontR={rig.RHand.position.z - rig.Hips.position.z:F3} " +
                      $"hipsY={idleHipsY:F3} footLx={idleFootLx:F3} footRx={idleFootRx:F3} " +
                      $"headYaw={idleHeadYaw:F1}");

            float worstDrop = 0f;
            float worstFront = 9f;
            float worstT = 0f;
            float worstHips = 0f;
            float worstFootX = 0f;
            float worstYaw = 0f;

            for (float t = 0f; t < 1f; t += 0.02f)
            {
                anim.ForceState(CharacterAnimator.State.Walk, t, 2f, StaggerTier.None, false);
                anim.ApplyPose();

                float headY = rig.Head.position.y;
                float dropL = headY - rig.LHand.position.y;
                float dropR = headY - rig.RHand.position.y;
                float frontL = rig.LHand.position.z - rig.Hips.position.z;
                float frontR = rig.RHand.position.z - rig.Hips.position.z;

                // 基準より「下がった量」。プラスが大きいほど腕が落ちている
                float d = Mathf.Max(dropL - idleDropL, dropR - idleDropR);
                if (d > worstDrop)
                {
                    worstDrop = d;
                    worstT = t;
                }

                worstFront = Mathf.Min(worstFront, Mathf.Min(frontL, frontR));

                // 腰の高さ・足の幅・頭の向きも構えから離れていないか
                worstHips = Mathf.Max(worstHips, Mathf.Abs(rig.Hips.position.y - idleHipsY));
                worstFootX = Mathf.Max(worstFootX,
                    Mathf.Max(Mathf.Abs(rig.LFoot.position.x - idleFootLx),
                              Mathf.Abs(rig.RFoot.position.x - idleFootRx)));

                float yaw = Vector3.SignedAngle(rig.Hips.forward, rig.Head.forward, Vector3.up);
                worstYaw = Mathf.Max(worstYaw, Mathf.Abs(yaw - idleHeadYaw));
            }

            // 手の高さが構えから 6cm 以内、かつ体より前に残っていること。
            // 前への張り出しは歩行の揺れで数 cm 変わるので、5cm を下限にする。
            // 腰の高さは 3cm、足の幅は 1cm、頭の向きは 8° 以内。
            bool ok = worstDrop < 0.06f && worstFront > 0.05f
                      && worstHips < 0.03f && worstFootX < 0.01f && worstYaw < 8f;

            Debug.Log($"[WalkGuard] walk worstDrop={worstDrop:F3}m at t={worstT:F2} " +
                      $"minFront={worstFront:F3} hips={worstHips:F3}m " +
                      $"footX={worstFootX:F3}m headYaw={worstYaw:F1}deg -> {(ok ? "OK" : "NG")}");

            Object.DestroyImmediate(vrm.gameObject);
        }

        /// <summary>
        /// HUD の日本語が化けないかを、フォントが取れるかで確かめる。
        /// ビルドでは OS のフォントを動的に掴むので、この環境で取れる必要がある。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Scan Japanese Font")]
        public static void ScanJapaneseFont()
        {
            var font = Font.CreateDynamicFontFromOSFont(
                new[] { "Yu Gothic UI", "Yu Gothic", "Meiryo UI", "Meiryo", "MS Gothic", "Noto Sans JP" }, 12);

            if (font == null)
            {
                Debug.LogError("[Font] OS の日本語フォントが取れない");
                return;
            }

            var builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            Debug.Log($"[Font] os={font.name} あ={font.HasCharacter('あ')} 倒={font.HasCharacter('倒')} " +
                      $"ヒ={font.HasCharacter('ヒ')} builtin={builtin?.name} " +
                      $"builtinあ={(builtin != null && builtin.HasCharacter('あ'))}");
        }

        /// <summary>まとめて確認する（バッチから 1 回で呼ぶ用）。</summary>
        public static void ScanRound5()
        {
            ScanJapaneseFont();
            ScanWalkGuard();
            ScanHeadShake();
        }

        /// <summary>
        /// 被弾時に首が小さく揺れるか（振り子のように往復しているか）を測る。
        /// 揺れは「戻る途中で 1 回以上向きが変わる」ことで確かめられる。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Scan Head Shake")]
        public static void ScanHeadShake()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
            if (!File.Exists(vrmPath))
            {
                Debug.LogError($"[Ryona] VRM not found: {vrmPath}");
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Vrm10Instance vrm = Vrm10
                .LoadPathAsync(vrmPath, canLoadVrm0X: true, showMeshes: true)
                .GetAwaiter().GetResult();

            if (vrm == null)
            {
                Debug.LogError("[Ryona] VRM load returned null");
                return;
            }

            var rig = vrm.gameObject.AddComponent<ProceduralRig>();
            rig.Capture();
            var anim = vrm.gameObject.AddComponent<CharacterAnimator>();
            anim.Rig = rig;

            anim.ForceState(CharacterAnimator.State.Idle, 0.6f, 2f, StaggerTier.None, false);
            float baseRoll = HeadRoll(rig);
            float baseYaw = HeadYaw(rig);

            foreach (var tier in new[] { StaggerTier.Small, StaggerTier.Medium, StaggerTier.Large })
            {
                int flips = 0;
                float maxDev = 0f;
                float prevRoll = 0f;
                float prevDelta = 0f;
                var series = new System.Text.StringBuilder();

                for (int i = 0; i <= 36; i++)
                {
                    float t = i / 60f;
                    anim.ForceState(CharacterAnimator.State.Stagger, t, 0.55f, tier, false);

                    float roll = HeadRoll(rig) - baseRoll;
                    float yaw = HeadYaw(rig) - baseYaw;
                    maxDev = Mathf.Max(maxDev, Mathf.Abs(roll));

                    float delta = roll - prevRoll;
                    if (i > 4 && Mathf.Sign(delta) != Mathf.Sign(prevDelta) && Mathf.Abs(prevDelta) > 0.05f)
                        flips++;
                    prevDelta = delta;
                    prevRoll = roll;

                    if (i % 4 == 0 || tier == StaggerTier.Medium)
                        series.Append($"{t:F2}:{roll:F1}/{yaw:F1} ");
                }

                Debug.Log($"[HeadShake] {tier} maxRoll={maxDev:F1}deg flips={flips} {series}");
            }

            Object.DestroyImmediate(vrm.gameObject);
        }

        /// <summary>
        /// 頭の傾き（ロール）。頭の右側 10cm の点の高さで測る。
        /// クォータネイションから角度を抜くと、後ろへ反ったとき（forward が上を向く）に
        /// 符号が破綻して 90° 近い値が出るので、位置で測る。
        /// </summary>
        static float HeadRoll(ProceduralRig rig) =>
            Mathf.Asin(Mathf.Clamp((rig.Head.position + rig.Head.right * 0.10f).y - rig.Head.position.y, -0.1f, 0.1f) / 0.10f)
            * Mathf.Rad2Deg;

        /// <summary>
        /// 頭の向き（ヨー）。水平面に落としてから測る（上下の揺れに影響されない）。
        /// </summary>
        static float HeadYaw(ProceduralRig rig)
        {
            Vector3 head = rig.Head.forward;
            Vector3 hips = rig.Hips.forward;
            head.y = 0f;
            hips.y = 0f;
            return Vector3.SignedAngle(hips, head, Vector3.up);
        }

        /// <summary>
        /// 体を回したときに腕の姿勢が崩れないかを測る。
        /// 腕の向きは「キャラクター基準の外・上・前」で決まるので、
        /// 体が何度回っても手のローカル位置は同じでなければならない。
        /// ずれるなら、どこかに向きをワールドで覚えている場所がある。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Scan Arm Yaw")]
        public static void ScanArmYaw()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
            if (!File.Exists(vrmPath))
            {
                Debug.LogError($"[Ryona] VRM not found: {vrmPath}");
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Vrm10Instance vrm = Vrm10
                .LoadPathAsync(vrmPath, canLoadVrm0X: true, showMeshes: true)
                .GetAwaiter().GetResult();

            if (vrm == null)
            {
                Debug.LogError("[Ryona] VRM load returned null");
                return;
            }

            var rig = vrm.gameObject.AddComponent<ProceduralRig>();
            rig.Capture();
            var anim = vrm.gameObject.AddComponent<CharacterAnimator>();
            anim.Rig = rig;

            var cases = new (string name, CharacterAnimator.State state, StaggerTier tier, float time)[]
            {
                ("idle", CharacterAnimator.State.Idle, StaggerTier.None, 0.5f),
                ("walk", CharacterAnimator.State.Walk, StaggerTier.None, 0.5f),
                ("stagger-m", CharacterAnimator.State.Stagger, StaggerTier.Medium, 0.10f),
                ("stagger-l", CharacterAnimator.State.Stagger, StaggerTier.Large, 0.12f),
                ("down", CharacterAnimator.State.Down, StaggerTier.GuardBreak, 0.90f),
                ("guard", CharacterAnimator.State.Guard, StaggerTier.None, 0.5f),
            };

            float worst = 0f;

            foreach (var c in cases)
            {
                Vector3 refL = Vector3.zero;
                Vector3 refR = Vector3.zero;
                float caseWorst = 0f;
                float caseAt = 0f;

                foreach (float yaw in new[] { 0f, 45f, 90f, 135f, 180f, 270f })
                {
                    // 最初の 1 回（yaw 0）で腕の軸がキャッシュされる。以降は体だけ回す。
                    vrm.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    anim.ForceState(c.state, Mathf.Max(0.05f, c.time), 1.0f, c.tier, log: false);

                    // 手の位置を体のローカルで測る（体が回っても値は変わらないはず）
                    Vector3 l = vrm.transform.InverseTransformPoint(rig.LHand.position);
                    Vector3 r = vrm.transform.InverseTransformPoint(rig.RHand.position);

                    if (yaw <= 0.01f)
                    {
                        refL = l;
                        refR = r;
                        continue;
                    }

                    float d = Mathf.Max(Vector3.Distance(l, refL), Vector3.Distance(r, refR));
                    if (d > caseWorst)
                    {
                        caseWorst = d;
                        caseAt = yaw;
                    }
                }

                worst = Mathf.Max(worst, caseWorst);

                string note = caseWorst > 0.03f ? "NG" : "ok";
                Debug.Log($"[ArmYaw] {c.name}: {note} max drift {caseWorst * 100f:F1}cm " +
                          $"(yaw {caseAt:F0})");
            }

            Object.DestroyImmediate(vrm.gameObject);
            Debug.Log($"[Ryona] arm yaw scan done. worst={worst * 100f:F1}cm");
        }

    }
}


