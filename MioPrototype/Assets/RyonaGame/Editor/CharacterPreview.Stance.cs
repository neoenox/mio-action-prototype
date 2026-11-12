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
        /// <summary>全身・顔・ポーズシートをまとめて出す。</summary>
        [MenuItem("Ryona/Capture All")]
        public static void CaptureAll()
        {
            Capture();
            CapturePoseSheet();
        }

        /// <summary>主要な状態を1枚に並べて確認するためのコンタクトシート。</summary>
        [MenuItem("Ryona/Capture Pose Sheet")]
        public static void CapturePoseSheet()
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

            // 同一フレーム内で何度も Camera.Render() するので、
            // スキンメッシュの行列を毎回計算し直させる（これをしないと1枚目以降が固まる）
            foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
                smr.forceMatrixRecalculationPerRender = true;

            var bounds = ComputeBounds(vrm.gameObject);
            float h = Mathf.Max(1f, bounds.size.y);

            var camGo = new GameObject("Pose Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.fieldOfView = 36f;
            cam.nearClipPlane = 0.05f;
            cam.transform.position = new Vector3(h * 0.62f, h * 0.52f, h * 2.35f);
            cam.transform.LookAt(new Vector3(0f, h * 0.40f, -0.12f));

            var shots = new (CharacterAnimator.State state, float time, float duration, StaggerTier tier)[]
            {
                (CharacterAnimator.State.Idle, 0.5f, 1f, StaggerTier.None),
                (CharacterAnimator.State.Walk, 0.25f, 1f, StaggerTier.None),
                (CharacterAnimator.State.Walk, 0.75f, 1f, StaggerTier.None),
                (CharacterAnimator.State.Stagger, 0.04f, 0.2f, StaggerTier.Small),
                (CharacterAnimator.State.Stagger, 0.05f, 0.4f, StaggerTier.Medium),
                (CharacterAnimator.State.Stagger, 0.08f, 0.8f, StaggerTier.Large),
                (CharacterAnimator.State.Down, 0.300f, 0.9f, StaggerTier.GuardBreak),
                (CharacterAnimator.State.Attack, 0.020f, 0.3f, StaggerTier.None),
                (CharacterAnimator.State.Attack, 0.050f, 0.3f, StaggerTier.None),
            };

            const int cellW = 380;
            const int cellH = 620;

            var sheet = new Texture2D(cellW * shots.Length, cellH, TextureFormat.RGB24, false);

            for (int i = 0; i < shots.Length; i++)
            {
                anim.ForceState(shots[i].state, shots[i].time, shots[i].duration, shots[i].tier);

                var cell = RenderCell(cam, cellW, cellH);
                sheet.SetPixels(i * cellW, 0, cellW, cellH, cell.GetPixels());
                Object.DestroyImmediate(cell);
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "pose_sheet.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] pose sheet: {outPath}");
        }

        [MenuItem("Ryona/Diagnostics/Capture Combat Strip")]
        public static void CaptureCombatStrip()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
            if (!File.Exists(vrmPath))
            {
                Debug.LogError($"[Ryona] VRM not found: {vrmPath}");
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLights();

            // 床。接地しているか（ダウンの膝・つま先）を目で判定するために置く。
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = Vector3.one * 6f;
            var floorMat = new Material(Shader.Find("Standard"));
            floorMat.color = new Color(0.22f, 0.23f, 0.26f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;

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

            var camGo = new GameObject("Combat Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.fieldOfView = 38f;
            cam.nearClipPlane = 0.05f;

            // view 0 = 横（攻撃の軌道が読める） / view 1 = 斜め前（ダウンの形が読める）
            var shots = new (CharacterAnimator.State state, float time, float duration,
                StaggerTier tier, int view, int kind, string label)[]
            {
                (CharacterAnimator.State.Attack, 0.000f, 0.6f, StaggerTier.None, 0, 0, "light 構え"),
                (CharacterAnimator.State.Attack, 0.040f, 0.6f, StaggerTier.None, 0, 0, "light 振りかぶり"),
                (CharacterAnimator.State.Attack, 0.100f, 0.6f, StaggerTier.None, 0, 0, "light 当たり"),
                (CharacterAnimator.State.Attack, 0.200f, 0.6f, StaggerTier.None, 0, 0, "light 戻し"),
                (CharacterAnimator.State.Attack, 0.090f, 0.6f, StaggerTier.None, 2, 1, "heavy 振りかぶり(正面)"),
                (CharacterAnimator.State.Attack, 0.040f, 0.6f, StaggerTier.None, 0, 3, "light2 左 振りかぶり"),
                (CharacterAnimator.State.Attack, 0.100f, 0.6f, StaggerTier.None, 0, 3, "light2 左 当たり"),
                (CharacterAnimator.State.Attack, 0.200f, 0.6f, StaggerTier.None, 0, 3, "light2 左 戻し"),
                // 正面から見ないと「右腕か左腕か」が判別できないので、当たりだけ正面も撮る
                (CharacterAnimator.State.Attack, 0.100f, 0.6f, StaggerTier.None, 2, 0, "light 右 当たり(正面)"),
                (CharacterAnimator.State.Attack, 0.100f, 0.6f, StaggerTier.None, 2, 3, "light2 左 当たり(正面)"),
                (CharacterAnimator.State.Attack, 0.200f, 0.6f, StaggerTier.None, 0, 1, "heavy 当たり"),
                (CharacterAnimator.State.Attack, 0.400f, 0.6f, StaggerTier.None, 0, 1, "heavy 戻し"),
                (CharacterAnimator.State.Attack, 0.120f, 0.6f, StaggerTier.None, 2, 2, "break 振りかぶり(正面)"),
                (CharacterAnimator.State.Attack, 0.300f, 0.6f, StaggerTier.None, 0, 2, "break 当たり"),
                (CharacterAnimator.State.Attack, 0.550f, 0.6f, StaggerTier.None, 0, 2, "break 戻し"),
                (CharacterAnimator.State.Down, 0.300f, 0.9f, StaggerTier.GuardBreak, 1, -1, "down 崩れ"),
                (CharacterAnimator.State.Down, 0.750f, 0.9f, StaggerTier.GuardBreak, 1, -1, "down 着地"),
                (CharacterAnimator.State.Down, 5.000f, 999f, StaggerTier.GuardBreak, 1, -1, "down 死亡"),
            };

            const int cellW = 360;
            const int cellH = 620;

            var sheet = new Texture2D(cellW * shots.Length, cellH, TextureFormat.RGB24, false);

            for (int i = 0; i < shots.Length; i++)
            {
                var s = shots[i];
                anim.AttackOverride = s.kind switch
                {
                    0 => AttackLibrary.Light(),
                    1 => AttackLibrary.Heavy(),
                    2 => AttackLibrary.BreakAttack(),
                    3 => AttackLibrary.Light(),
                    _ => null
                };
                // kind 3 は 3段コンボの 2 段目（左で打つ）
                anim.ComboStep = s.kind == 3 ? 1 : 0;
                anim.ForceState(s.state, s.time, s.duration, s.tier);

                if (s.view == 0)
                {
                    cam.transform.position = new Vector3(h * 2.35f, h * 0.52f, h * 0.10f);
                    cam.transform.LookAt(new Vector3(0f, h * 0.50f, 0f));
                }
                else if (s.view == 2)
                {
                    cam.transform.position = new Vector3(h * 0.30f, h * 0.55f, h * 2.20f);
                    cam.transform.LookAt(new Vector3(0f, h * 0.48f, 0f));
                }
                else
                {
                    cam.transform.position = new Vector3(h * 0.95f, h * 0.90f, h * 1.75f);
                    cam.transform.LookAt(new Vector3(0f, h * 0.16f, -0.18f));
                }

                var cell = RenderCell(cam, cellW, cellH);
                sheet.SetPixels(i * cellW, 0, cellW, cellH, cell.GetPixels());
                Object.DestroyImmediate(cell);

                Debug.Log($"[Strip] {s.label} hipsY={rig.Hips.position.y:F3} " +
                          $"handRy={rig.RHand.position.y:F3} handRz={rig.RHand.position.z:F3}");
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "combat_strip.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(floor);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] combat strip: {outPath}");
        }

        /// <summary>
        /// 拳の寄り。左右の指が「正しく握れているか」を目で見る。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Fist Closeup")]
        public static void CaptureFistCloseup()
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

            var camGo = new GameObject("Fist Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.fieldOfView = 20f;
            cam.nearClipPlane = 0.02f;

            // ガードの構えだと両手が顔の前に来るので、寄りで見やすい
            anim.AttackOverride = null;
            anim.ForceState(CharacterAnimator.State.Guard, 0.5f, 2f, StaggerTier.None);
            anim.ApplyPose();

            const int cellW = 460;
            const int cellH = 460;
            var sheet = new Texture2D(cellW * 2, cellH, TextureFormat.RGB24, false);

            // 正面
            cam.transform.position = new Vector3(0f, 1.33f, 1.45f);
            cam.transform.LookAt(new Vector3(0f, 1.33f, 0.25f));
            var front = RenderCell(cam, cellW, cellH);
            sheet.SetPixels(0, 0, cellW, cellH, front.GetPixels());
            Object.DestroyImmediate(front);

            // 真上
            cam.transform.position = new Vector3(0f, 2.55f, 0.25f);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var top = RenderCell(cam, cellW, cellH);
            sheet.SetPixels(cellW, 0, cellW, cellH, top.GetPixels());
            Object.DestroyImmediate(top);

            // 数値でも確認（指先と手首の距離）
            var at = vrm.GetComponent<Animator>();
            foreach (bool left in new[] { false, true })
            {
                var hand = at.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                var tip = at.GetBoneTransform(left ? HumanBodyBones.LeftMiddleDistal : HumanBodyBones.RightMiddleDistal);
                if (hand != null && tip != null)
                    Debug.Log($"[Fist] {(left ? "L" : "R")} tip-hand={Vector3.Distance(tip.position, hand.position):F4}");
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "fist_closeup.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] fist closeup: {outPath}");
        }

        /// <summary>
        /// 構え（Idle）の拳を、左右それぞれ 3 方向から撮る。
        /// 「猫の手」＝手首が曲がって拳が下を向く、を目と数値で確認する。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Stance Hands")]
        public static void CaptureStanceHands()
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
            anim.AttackOverride = null;

            var at = vrm.GetComponent<Animator>();

            anim.ForceState(CharacterAnimator.State.Idle, 0.5f, 2f, StaggerTier.None);
            anim.ApplyPose();

            foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
                smr.forceMatrixRecalculationPerRender = true;

            var camGo = new GameObject("Hand Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.24f, 0.25f, 0.28f);
            cam.fieldOfView = 22f;
            cam.nearClipPlane = 0.01f;

            const int cell = 560;
            var sheet = new Texture2D(cell * 5, cell * 2, TextureFormat.RGB24, false);

            // 親指の狙い付け（ThumbAim）を 5 案で振り、拳のどこに親指が載るかを見る。
            var aims = new[] { 0f, 0.25f, 0.5f, 0.75f, 1f };

            for (int row = 0; row < 2; row++)
            {
                bool left = row == 0;
                var hand = at.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                var midDist = at.GetBoneTransform(left ? HumanBodyBones.LeftMiddleDistal : HumanBodyBones.RightMiddleDistal);
                var indexMid = at.GetBoneTransform(left ? HumanBodyBones.LeftIndexIntermediate : HumanBodyBones.RightIndexIntermediate);
                var thumbDist = at.GetBoneTransform(left ? HumanBodyBones.LeftThumbDistal : HumanBodyBones.RightThumbDistal);

                for (int col = 0; col < aims.Length; col++)
                {
                    ProceduralRig.ThumbAim = aims[col];
                    anim.ForceState(CharacterAnimator.State.Idle, 0.5f, 2f, StaggerTier.None, log: false);

                    Vector3 p = hand.position;

                    Vector3 dir = (vrm.transform.forward * 0.92f
                        + vrm.transform.right * (left ? -0.30f : 0.30f)
                        + vrm.transform.up * 0.08f).normalized;

                    cam.fieldOfView = 24f;
                    cam.transform.position = p + dir * 0.55f;
                    cam.transform.LookAt(p);

                    var tex = RenderCell(cam, cell, cell);
                    sheet.SetPixels(col * cell, (1 - row) * cell, cell, cell, tex.GetPixels());
                    Object.DestroyImmediate(tex);

                    float thumbReach = Vector3.Distance(thumbDist.position, p);
                    float fingerReach = Vector3.Distance(midDist.position, p);
                    float dist3 = Vector3.Distance(thumbDist.position, indexMid.position);

                    Debug.Log($"[Thumb] {(left ? "L" : "R")} aim={aims[col]:F2} " +
                        $"thumbReach={thumbReach:F3} fingerReach={fingerReach:F3} " +
                        $"thumbToIndexMid={dist3:F3}");
                }
            }

            ProceduralRig.ThumbAim = 1f;

            sheet.Apply();

            // 掌を内側（体の中心）へ向けるための、前腕まわりのひねり角を数値で求める。
            CharacterAnimator.StanceHandRollL = 45f;
            CharacterAnimator.StanceHandRollR = -48f;

            foreach (bool left in new[] { true, false })
            {
                anim.ForceState(CharacterAnimator.State.Idle, 0.5f, 2f, StaggerTier.None, log: false);
                rig.CurlFingers(true, 1f);
                rig.CurlFingers(false, 1f);

                var hand = at.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                var lower = at.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                var index = at.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
                var thumb = at.GetBoneTransform(left ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal);

                Vector3 p = hand.position;
                Vector3 palm = rig.PalmNormalWorld(left);
                Vector3 fore = (p - lower.position).normalized;
                Vector3 inward = vrm.transform.right * (left ? 1f : -1f);

                float need = Vector3.SignedAngle(
                    Vector3.ProjectOnPlane(palm, fore),
                    Vector3.ProjectOnPlane(inward, fore),
                    fore);

                Debug.Log($"[Hands] roll {(left ? "L" : "R")} " +
                    $"roll={(left ? CharacterAnimator.StanceHandRollL : CharacterAnimator.StanceHandRollR):F0} " +
                    $"need={need:F0}deg palmInwardNow={Vector3.Dot(palm, inward):F2}");
            }

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "stance_hands.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] stance hands: {outPath}");
        }

        /// <summary>
        /// 構えの拳を手首のひねり角 4 案（0 / -45 / -90 / -135）で並べる。
        /// 段: 上=正面 / 下=斜め前。列が案。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Stance Roll Options")]
        public static void CaptureStanceRollOptions()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
            if (!File.Exists(vrmPath))
            {
                Debug.LogError($"[Ryona] VRM not found: {vrmPath}");
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLights();

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = Vector3.one * 6f;
            var floorMat = new Material(Shader.Find("Standard"));
            floorMat.color = new Color(0.30f, 0.31f, 0.34f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;

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
            anim.AttackOverride = null;

            foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.forceMatrixRecalculationPerRender = true;
                smr.localBounds = new Bounds(Vector3.zero, Vector3.one * 6f);
            }

            var camGo = new GameObject("Roll Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.33f, 0.34f, 0.37f);
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.02f;

            var rolls = new[] { 0f, -45f, -90f, -135f };
            const int cell = 700;
            var sheet = new Texture2D(cell * rolls.Length, cell * 2, TextureFormat.RGB24, false);

            for (int col = 0; col < rolls.Length; col++)
            {
                CharacterAnimator.StanceHandRollL = rolls[col];
                CharacterAnimator.StanceHandRollR = rolls[col];

                anim.ForceState(CharacterAnimator.State.Idle, 0.5f, 2f, StaggerTier.None, log: false);

                Vector3 chest = rig.Chest.position;

                // 上段: 正面
                cam.transform.position = chest + new Vector3(0f, 0.10f, 0.95f);
                cam.transform.LookAt(chest + new Vector3(0f, 0.04f, 0f));
                var top = RenderCell(cam, cell, cell);
                sheet.SetPixels(col * cell, cell, cell, cell, top.GetPixels());
                Object.DestroyImmediate(top);

                // 下段: 斜め前
                cam.transform.position = chest + new Vector3(-0.72f, 0.18f, 0.72f);
                cam.transform.LookAt(chest + new Vector3(0f, 0.02f, 0f));
                var bottom = RenderCell(cam, cell, cell);
                sheet.SetPixels(col * cell, 0, cell, cell, bottom.GetPixels());
                Object.DestroyImmediate(bottom);

                Debug.Log($"[RollOptions] col={col} roll={rolls[col]:F0}");
            }

            // 元に戻す（静的値なので実行中だけ書き換わるが、念のため）
            CharacterAnimator.StanceHandRollL = 45f;
            CharacterAnimator.StanceHandRollR = -48f;

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "stance_roll_options.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] stance roll options: {outPath}");
        }

        /// <summary>
        /// 三人称カメラ（背後 3.4m・俯角 10 度）から見た構えと、寄った手元。
        /// プレイヤーが実際に見ている角度で拳を判断するための確認用。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Game View")]
        public static void CaptureGameView()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
            if (!File.Exists(vrmPath))
            {
                Debug.LogError($"[Ryona] VRM not found: {vrmPath}");
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLights();

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = Vector3.one * 6f;
            var floorMat = new Material(Shader.Find("Standard"));
            floorMat.color = new Color(0.30f, 0.31f, 0.34f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;

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
            anim.AttackOverride = null;

            foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.forceMatrixRecalculationPerRender = true;
                smr.localBounds = new Bounds(Vector3.zero, Vector3.one * 6f);
            }

            anim.ForceState(CharacterAnimator.State.Idle, 0.5f, 2f, StaggerTier.None, log: false);

            var camGo = new GameObject("Game Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.33f, 0.34f, 0.37f);
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.05f;

            const int cell = 700;
            var sheet = new Texture2D(cell * 3, cell, TextureFormat.RGB24, false);

            // ThirdPersonCamera と同じ置き方（背後・ピボット 1.45m・俯角 10 度）
            void Look(float pivotY, float distance, float pitch, int index)
            {
                Vector3 pivot = vrm.transform.position + new Vector3(0f, pivotY, 0f);
                Quaternion rot = Quaternion.Euler(pitch, 0f, 0f);

                cam.transform.position = pivot - rot * Vector3.forward * distance;
                cam.transform.rotation = rot;

                var tex = RenderCell(cam, cell, cell);
                sheet.SetPixels(index * cell, 0, cell, cell, tex.GetPixels());
                Object.DestroyImmediate(tex);

                Debug.Log($"[GameView] {index} pivotY={pivotY} d={distance} pitch={pitch} cam={cam.transform.position}");
            }

            Look(1.45f, 3.4f, 10f, 0);   // 通常の引き
            Look(1.35f, 1.6f, 8f, 1);    // 寄り（操作で寄せたとき）
            Look(1.30f, 1.0f, 2f, 2);    // 手元

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "game_view.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] game view: {outPath}");
        }

        /// <summary>
        /// 直した 2 点（構えの拳・ダウンの目）を、ゲームに近い見え方で 1 枚にまとめる。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Fix Showcase")]
        public static void CaptureFixShowcase()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
            if (!File.Exists(vrmPath))
            {
                Debug.LogError($"[Ryona] VRM not found: {vrmPath}");
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLights();

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = Vector3.one * 6f;
            var floorMat = new Material(Shader.Find("Standard"));
            floorMat.color = new Color(0.30f, 0.31f, 0.34f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;

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
            anim.AttackOverride = null;

            foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.forceMatrixRecalculationPerRender = true;

                // バッチ実行では bounds が更新されず、寝た姿勢になると画面外と判定されて消える。
                smr.localBounds = new Bounds(Vector3.zero, Vector3.one * 6f);
            }
            var camGo = new GameObject("Fix Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.33f, 0.34f, 0.37f);
            cam.fieldOfView = 32f;
            cam.nearClipPlane = 0.02f;

            const int cell = 620;
            var sheet = new Texture2D(cell * 4, cell, TextureFormat.RGB24, false);

            void Shoot(int index)
            {
                var tex = RenderCell(cam, cell, cell);
                sheet.SetPixels(index * cell, 0, cell, cell, tex.GetPixels());
                Object.DestroyImmediate(tex);
            }

            // 1) 構え・正面（胸から上）
            anim.ForceState(CharacterAnimator.State.Idle, 0.5f, 2f, StaggerTier.None, log: false);

            Vector3 chest = rig.Chest.position;
            cam.transform.position = chest + new Vector3(0f, 0.12f, 1.05f);
            cam.transform.LookAt(chest + new Vector3(0f, 0.05f, 0f));
            Shoot(0);

            // 2) 構え・斜め前（拳の形が分かる角度）
            cam.transform.position = chest + new Vector3(-0.85f, 0.20f, 0.85f);
            cam.transform.LookAt(chest + new Vector3(0f, 0.03f, 0f));
            Shoot(1);

            // 3,4) ダウン（倒れきったところ）。目は閉じている。
            anim.ForceState(CharacterAnimator.State.Down, 1.75f, 2.0f, StaggerTier.GuardBreak, log: false);

            foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = smr.sharedMesh;
                if (mesh == null)
                    continue;

                int idx = mesh.GetBlendShapeIndex("Fcl_EYE_Close");
                if (idx >= 0)
                    smr.SetBlendShapeWeight(idx, 100f);
            }

            Vector3 headPos = rig.Head.position;
            Vector3 hipsPos = rig.Hips.position;
            Vector3 bodyDir = (headPos - hipsPos).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, bodyDir).normalized;

            // 全身（斜め上から）。骨の位置から寄り引きを決める。
            Vector3 center = (headPos + hipsPos) * 0.5f + Vector3.up * 0.05f;
            cam.transform.position = center + Vector3.up * 1.55f + side * 0.95f - bodyDir * 0.75f;
            cam.transform.LookAt(center + rig.Head.forward * 0.10f);
            Debug.Log($"[Fix] full cam={cam.transform.position} vCenter={cam.WorldToViewportPoint(center)}");
            Shoot(2);

            // 顔（目が閉じているか）。顔が向いている方向から、髪の外側まで下がって撮る。
            Vector3 faceDir = rig.Head.forward;
            Vector3 faceUp = rig.Head.up;
            cam.transform.position = headPos + faceDir * 0.75f + faceUp * 0.05f;
            cam.transform.LookAt(headPos);
            Debug.Log($"[Fix] face cam={cam.transform.position} vHead={cam.WorldToViewportPoint(headPos)} fwd={faceDir}");
            Shoot(3);

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "fix_showcase.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] fix showcase: {outPath}");
        }

        /// <summary>
        /// 敵の歩きで足が滑っていないかを測る。接地中の足の水平移動が理想は 0。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Scan Enemy Walk")]
        public static void ScanEnemyWalk()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            float[] speeds = { 0.8f, 1.2f, 1.8f, 2.4f };
            float[] angles = { 0f, 45f, 90f };   // 前 / 斜め / 真横（回り込み）

            foreach (float angle in angles)
            {
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                foreach (float speed in speeds)
                    MeasureWalk(dir, speed, angle);
            }
        }

        static void MeasureWalk(Vector3 dir, float speed, float angle)
        {
            const float dt = 1f / 60f;

            var go = new GameObject($"WalkTest_{angle}_{speed}");
            var fig = go.AddComponent<EnemyFigure>();

            // 姿勢が落ち着くまで助走
            for (int i = 0; i < 40; i++)
                fig.PreviewTick(dt, speed, dir);

            Transform footR = fig.Foot(false);
            Transform footL = fig.Foot(true);
            if (footR == null || footL == null)
            {
                Debug.LogError("[Walk] 足の Transform が取れない");
                Object.DestroyImmediate(go);
                return;
            }

            Vector3 prevR = footR.position;
            Vector3 prevL = footL.position;

            float slipSum = 0f;
            float slipMax = 0f;
            int stance = 0;
            float lowest = 9f;
            float highest = -9f;

            for (int i = 0; i < 300; i++)
            {
                fig.PreviewTick(dt, speed, dir);

                Vector3 r = footR.position;
                Vector3 l = footL.position;

                lowest = Mathf.Min(lowest, Mathf.Min(r.y, l.y));
                highest = Mathf.Max(highest, Mathf.Max(r.y, l.y));

                // 接地の中央だけを測る（踏み出し／離陸の境目は必ず動く）
                float u = fig.WalkPhase;
                float uL = Mathf.Repeat(u + 0.5f, 1f);

                if (u > 0.06f && u < 0.56f)
                    slipSum += StanceSlip(r, prevR, dt, ref stance, ref slipMax);
                if (uL > 0.06f && uL < 0.56f)
                    slipSum += StanceSlip(l, prevL, dt, ref stance, ref slipMax);

                prevR = r;
                prevL = l;
            }

            float avg = stance > 0 ? slipSum / stance : 0f;
            float ratio = speed > 0.01f ? avg / speed * 100f : 0f;

            Debug.Log($"[Walk] 向き={angle:F0}° 速さ={speed:F1} 接地中の足の移動 平均={avg:F3}m/s ({ratio:F1}%) " +
                      $"最大={slipMax:F3} 足の高さ min={lowest:F3} max={highest:F3}");

            Object.DestroyImmediate(go);
        }

    }
}


