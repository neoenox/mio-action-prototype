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
        /// ダウン〜立ち上がりの関節を調べる。横・正面・真上から撮る。
        /// 「人体でありえない方向に曲がる」は、どの関節かを見ないと直せない。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Down Sheet")]
        public static void CaptureDownSheet()
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
            floorMat.color = new Color(0.24f, 0.25f, 0.28f);
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

            var camGo = new GameObject("Down Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.30f, 0.31f, 0.35f);
            cam.fieldOfView = 32f;
            cam.nearClipPlane = 0.02f;

            // (状態, time, duration)
            var poses = new[]
            {
                (name: "Down t=0.50", state: CharacterAnimator.State.Down, time: 0.50f, duration: 2.0f),
                (name: "Down t=0.70", state: CharacterAnimator.State.Down, time: 0.70f, duration: 2.0f),
                (name: "Down t=0.90", state: CharacterAnimator.State.Down, time: 0.90f, duration: 2.0f),
                (name: "Down t=1.10", state: CharacterAnimator.State.Down, time: 1.10f, duration: 2.0f),
                (name: "Down end", state: CharacterAnimator.State.Down, time: 2.0f, duration: 2.0f),
            };

            const int cell = 480;
            var sheet = new Texture2D(cell * 3, cell * poses.Length, TextureFormat.RGB24, false);

            for (int i = 0; i < poses.Length; i++)
            {
                var p = poses[i];
                anim.ForceState(p.state, p.time, p.duration, StaggerTier.None);
                anim.ApplyPose();

                Vector3 hips = rig.Hips.position;
                var views = new[]
                {
                    new Vector3(hips.x + 2.4f, hips.y + 0.55f, hips.z + 0.15f),   // 横（右）
                    new Vector3(hips.x + 0.25f, hips.y + 1.05f, hips.z + 2.3f),   // 正面・斜め上
                    new Vector3(hips.x - 0.05f, hips.y + 2.5f, hips.z + 0.05f),   // 真上
                };

                for (int v = 0; v < views.Length; v++)
                {
                    cam.transform.position = views[v];
                    cam.transform.LookAt(new Vector3(hips.x, hips.y, hips.z + 0.25f));

                    var tex = RenderCell(cam, cell, cell);
                    sheet.SetPixels(v * cell, (poses.Length - 1 - i) * cell, cell, cell, tex.GetPixels());
                    Object.DestroyImmediate(tex);
                }

                Debug.Log($"[Down] {p.name} hips=({hips.x:F2},{hips.y:F2},{hips.z:F2}) kneeL={Vector3.Angle(rig.LUpperLeg.position - rig.Hips.position, rig.LLowerLeg.position - rig.LUpperLeg.position):F0}deg kneeR={Vector3.Angle(rig.RUpperLeg.position - rig.Hips.position, rig.RLowerLeg.position - rig.RUpperLeg.position):F0}deg");
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "down_sheet.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] down sheet: {outPath}");
        }

        /// <summary>
        /// 構えの腕を 4 案で撮り比べる（正面と斜め前）。肘の張りと拳の高さを見る。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Stance Arm Sweep")]
        public static void CaptureStanceArmSweep()
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

            var camGo = new GameObject("Stance Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.28f, 0.29f, 0.33f);
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.02f;

            var variants = new[]
            {
                // 現状（肘が外に張って拳が耳の横）
                (name: "A current",
                    lu: new Vector3(0.26f, -0.70f, 0.46f), lf: new Vector3(0.02f, 0.52f, 0.82f),
                    ru: new Vector3(-0.26f, -0.76f, 0.26f), rf: new Vector3(-0.02f, 0.94f, 0.08f)),
                // 肘を脇へ落とし、拳を顔の前へ
                (name: "B tucked",
                    lu: new Vector3(0.18f, -0.86f, 0.34f), lf: new Vector3(0.00f, 0.62f, 0.72f),
                    ru: new Vector3(-0.18f, -0.88f, 0.22f), rf: new Vector3(0.00f, 0.80f, 0.42f)),
                // 拳を胸の高さまで下げて前に出す
                (name: "C low",
                    lu: new Vector3(0.16f, -0.92f, 0.30f), lf: new Vector3(0.00f, 0.34f, 0.92f),
                    ru: new Vector3(-0.16f, -0.94f, 0.18f), rf: new Vector3(0.00f, 0.52f, 0.78f)),
                // 肘は畳み、前の手だけ少し差し出す
                (name: "D lead",
                    lu: new Vector3(0.22f, -0.84f, 0.44f), lf: new Vector3(0.00f, 0.50f, 0.86f),
                    ru: new Vector3(-0.16f, -0.88f, 0.24f), rf: new Vector3(0.00f, 0.84f, 0.36f)),
            };

            const int cellW = 470;
            const int cellH = 640;
            var sheet = new Texture2D(cellW * variants.Length, cellH * 2, TextureFormat.RGB24, false);

            for (int i = 0; i < variants.Length; i++)
            {
                var v = variants[i];
                CharacterAnimator.StanceArmLUpper = v.lu;
                CharacterAnimator.StanceArmLFore = v.lf;
                CharacterAnimator.StanceArmRUpper = v.ru;
                CharacterAnimator.StanceArmRFore = v.rf;

                anim.ForceState(CharacterAnimator.State.Idle, 0.5f, 2f, StaggerTier.None);
                anim.ApplyPose();

                Vector3 head = rig.Head.position;
                var views = new[]
                {
                    new Vector3(head.x, head.y - 0.10f, head.z + 1.15f),
                    new Vector3(head.x + 0.70f, head.y - 0.06f, head.z + 0.85f),
                };

                for (int k = 0; k < views.Length; k++)
                {
                    cam.transform.position = views[k];
                    cam.transform.LookAt(new Vector3(head.x, head.y - 0.14f, head.z));

                    var tex = RenderCell(cam, cellW, cellH);
                    sheet.SetPixels(i * cellW, (1 - k) * cellH, cellW, cellH, tex.GetPixels());
                    Object.DestroyImmediate(tex);
                }

                Debug.Log($"[Stance] {v.name} handL=({rig.LHand.position.x:F2},{rig.LHand.position.y:F2},{rig.LHand.position.z:F2}) handR=({rig.RHand.position.x:F2},{rig.RHand.position.y:F2},{rig.RHand.position.z:F2}) headY={head.y:F2}");
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "stance_arm_sweep.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] stance arm sweep: {outPath}");
        }

        /// <summary>
        /// 構えを胸から上で撮り、手首のひねりを変えて見比べる。
        /// 左から 0 / 45 / 90 / 135 度。拳が「拳」に見えるかを全身の見え方で判断する。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Guard Roll Body")]
        public static void CaptureGuardRollBody()
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

            var camGo = new GameObject("Guard Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.28f, 0.29f, 0.33f);
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.02f;

            float[] rolls = { 0f, 45f, 90f, 135f };

            const int cellW = 460;
            const int cellH = 620;
            var sheet = new Texture2D(cellW * rolls.Length, cellH, TextureFormat.RGB24, false);

            for (int i = 0; i < rolls.Length; i++)
            {
                CharacterAnimator.StanceHandRollL = rolls[i];
                CharacterAnimator.StanceHandRollR = -rolls[i];

                anim.ForceState(CharacterAnimator.State.Idle, 0.5f, 2f, StaggerTier.None);
                anim.ApplyPose();

                Vector3 chest = rig.Head.position;
                cam.transform.position = new Vector3(chest.x + 0.35f, chest.y - 0.05f, chest.z + 1.15f);
                cam.transform.LookAt(new Vector3(chest.x, chest.y - 0.12f, chest.z));

                var tex = RenderCell(cam, cellW, cellH);
                sheet.SetPixels(i * cellW, 0, cellW, cellH, tex.GetPixels());
                Object.DestroyImmediate(tex);

                Debug.Log($"[GuardRoll] {rolls[i]:F0}deg");
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "guard_roll_body.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] guard roll body: {outPath}");
        }

        /// <summary>
        /// 手首のひねり（StanceHandRoll）を振って、拳に見える角度を探す。
        /// 上段=正面、下段=真上。左から 0 / 60 / 90 / 120 / 150 度。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Hand Roll Sweep")]
        public static void CaptureHandRollSweep()
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

            var camGo = new GameObject("Roll Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.fieldOfView = 26f;
            cam.nearClipPlane = 0.02f;

            float[] rolls = { 0f, 60f, 90f, 120f, 150f };

            const int cell = 420;
            var sheet = new Texture2D(cell * rolls.Length, cell * 2, TextureFormat.RGB24, false);

            for (int i = 0; i < rolls.Length; i++)
            {
                CharacterAnimator.StanceHandRollL = rolls[i];
                CharacterAnimator.StanceHandRollR = -rolls[i];

                anim.ForceState(CharacterAnimator.State.Idle, 0.5f, 2f, StaggerTier.None);
                anim.ApplyPose();

                Vector3 hand = rig.RHand.position;

                var views = new[]
                {
                    new Vector3(hand.x + 0.05f, hand.y + 0.08f, hand.z + 0.52f),
                    new Vector3(hand.x + 0.02f, hand.y + 0.50f, hand.z + 0.16f),
                };

                for (int v = 0; v < views.Length; v++)
                {
                    cam.transform.position = views[v];
                    cam.transform.LookAt(hand);

                    var tex = RenderCell(cam, cell, cell);
                    sheet.SetPixels(i * cell, (1 - v) * cell, cell, cell, tex.GetPixels());
                    Object.DestroyImmediate(tex);
                }

                Debug.Log($"[Roll] {rolls[i]:F0}deg hand=({hand.x:F2},{hand.y:F2},{hand.z:F2})");
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "hand_roll_sweep.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] hand roll sweep: {outPath}");
        }

        /// <summary>
        /// 手元の寄り（構えの拳）。Idle と Guard を正面・右横・真上から撮る。
        /// 「拳が猫の手に見える」問題は、ここを見ないと直せない。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Hand Grid")]
        public static void CaptureHandGrid()
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

            var camGo = new GameObject("Hand Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.fieldOfView = 24f;
            cam.nearClipPlane = 0.02f;

            const int cell = 560;
            var sheet = new Texture2D(cell * 3, cell * 2, TextureFormat.RGB24, false);

            var states = new[]
            {
                CharacterAnimator.State.Idle,
                CharacterAnimator.State.Guard,
            };

            for (int s = 0; s < states.Length; s++)
            {
                anim.ForceState(states[s], 0.5f, 2f, StaggerTier.None);
                anim.ApplyPose();

                // 握れているかを数値で確認する（開いた手 → 拳 で指先が手首へ寄る）
                float closedL = rig.FingertipDistanceToWrist(true);
                float closedR = rig.FingertipDistanceToWrist(false);
                float bendL = rig.FingerBend(true, 1);
                float bendR = rig.FingerBend(false, 1);
                rig.CurlFingers(true, 0f);
                rig.CurlFingers(false, 0f);
                Debug.Log($"[Hand] {states[s]} tip L {rig.FingertipDistanceToWrist(true):F3}->{closedL:F3} R {rig.FingertipDistanceToWrist(false):F3}->{closedR:F3} bend L={bendL:F1} R={bendR:F1}");
                anim.ApplyPose();

                Vector3 hand = rig.RHand.position;
                var views = new[]
                {
                    new Vector3(hand.x + 0.10f, hand.y + 0.02f, hand.z + 0.62f),
                    new Vector3(hand.x + 0.62f, hand.y + 0.02f, hand.z + 0.10f),
                    new Vector3(hand.x + 0.02f, hand.y + 0.55f, hand.z + 0.18f),
                };

                for (int v = 0; v < views.Length; v++)
                {
                    cam.transform.position = views[v];
                    cam.transform.LookAt(hand);

                    var tex = RenderCell(cam, cell, cell);
                    sheet.SetPixels(v * cell, (1 - s) * cell, cell, cell, tex.GetPixels());
                    Object.DestroyImmediate(tex);

                    Debug.Log($"[Hand] {states[s]} view={v} hand=({hand.x:F2},{hand.y:F2},{hand.z:F2})");
                }
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "hand_grid.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] hand grid: {outPath}");
        }

        /// <summary>
        /// 動きの確認用コンタクトシート（4 列 × 4 段）。
        /// 上から 歩行 / 被弾 / ダウン / 立ち上がり。1 コマずつ状態と時刻を変えて撮る。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Contact Sheet")]
        public static void CaptureContactSheet()
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
            floor.transform.localScale = Vector3.one * 8f;
            var floorMat = new Material(Shader.Find("Standard"));
            floorMat.color = new Color(0.24f, 0.25f, 0.28f);
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

            // エディットモードでは、これが無いとボーンの変更が描画に反映されない
            foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
                smr.forceMatrixRecalculationPerRender = true;

            var bounds = ComputeBounds(vrm.gameObject);
            float h = Mathf.Max(1f, bounds.size.y);

            var camGo = new GameObject("Contact Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.30f, 0.31f, 0.35f);
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.05f;

            // 立っている姿勢も、倒れて体が伸びる姿勢も入るように、少し引いて斜め上から見る
            cam.transform.position = new Vector3(h * 0.95f, h * 0.80f, h * 2.05f);
            cam.transform.LookAt(new Vector3(0f, h * 0.42f, -h * 0.18f));

            const int cellW = 420;
            const int cellH = 620;
            const int cols = 4;
            const int rows = 4;

            var sheet = new Texture2D(cellW * cols, cellH * rows, TextureFormat.RGB24, false);

            var shots = new (CharacterAnimator.State state, float time, float duration,
                             StaggerTier tier, string label)[]
            {
                (CharacterAnimator.State.Walk, 0.00f, 2f, StaggerTier.None, "walk 0.00"),
                (CharacterAnimator.State.Walk, 0.25f, 2f, StaggerTier.None, "walk 0.25"),
                (CharacterAnimator.State.Walk, 0.50f, 2f, StaggerTier.None, "walk 0.50"),
                (CharacterAnimator.State.Walk, 0.75f, 2f, StaggerTier.None, "walk 0.75"),

                (CharacterAnimator.State.Stagger, 0.04f, 0.55f, StaggerTier.Medium, "hit 0.04"),
                (CharacterAnimator.State.Stagger, 0.10f, 0.55f, StaggerTier.Medium, "hit 0.10"),
                (CharacterAnimator.State.Stagger, 0.20f, 0.55f, StaggerTier.Medium, "hit 0.20"),
                (CharacterAnimator.State.Stagger, 0.34f, 0.55f, StaggerTier.Medium, "hit 0.34"),

                (CharacterAnimator.State.Down, 0.20f, 0.9f, StaggerTier.GuardBreak, "down 0.20"),
                (CharacterAnimator.State.Down, 0.45f, 0.9f, StaggerTier.GuardBreak, "down 0.45"),
                (CharacterAnimator.State.Down, 0.72f, 0.9f, StaggerTier.GuardBreak, "down 0.72"),
                (CharacterAnimator.State.Down, 5.00f, 999f, StaggerTier.GuardBreak, "down 倒れきり"),

                (CharacterAnimator.State.GetUp, 0.10f, 0.8f, StaggerTier.GuardBreak, "getup 0.10"),
                (CharacterAnimator.State.GetUp, 0.30f, 0.8f, StaggerTier.GuardBreak, "getup 0.30"),
                (CharacterAnimator.State.GetUp, 0.52f, 0.8f, StaggerTier.GuardBreak, "getup 0.52"),
                (CharacterAnimator.State.GetUp, 0.72f, 0.8f, StaggerTier.GuardBreak, "getup 0.72"),
            };

            for (int i = 0; i < shots.Length; i++)
            {
                var s = shots[i];
                anim.ForceState(s.state, s.time, s.duration, s.tier);
                var cell = RenderCell(cam, cellW, cellH);

                int col = i % cols;
                int row = i / cols;
                // 画像は下が原点なので、上段から並ぶように反転して置く
                sheet.SetPixels(col * cellW, (rows - 1 - row) * cellH, cellW, cellH, cell.GetPixels());
                Object.DestroyImmediate(cell);

                Debug.Log($"[Contact] {i} {s.label} hipsY={rig.Hips.position.y:F3} " +
                          $"headY={rig.Head.position.y:F3} " +
                          $"footLz={rig.LFoot.position.z:F3} handRy={rig.RHand.position.y:F3}");
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "contact_sheet.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] contact sheet: {outPath}");
        }

        /// <summary>
        /// 指定した向きを SetArm に渡したとき、骨が実際にその向きになるかを確かめる。
        /// 指示と実測がずれるなら、ずれはリグ側（Aim / 肘のひねり）にある。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Probe Attack")]
        public static void ProbeAttack()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
            if (!File.Exists(vrmPath))
            {
                Debug.LogError($"[Ryona] VRM not found: {vrmPath}");
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Vrm10Instance vrm = Vrm10
                .LoadPathAsync(vrmPath, canLoadVrm0X: true, showMeshes: false)
                .GetAwaiter().GetResult();

            var rig = vrm.gameObject.AddComponent<ProceduralRig>();
            rig.Capture();
            var anim = vrm.gameObject.AddComponent<CharacterAnimator>();
            anim.Rig = rig;

            var data = AttackLibrary.Heavy();
            anim.AttackOverride = data;
            float total = data.TotalSeconds;

            for (float t = 0f; t <= Mathf.Min(total * 0.45f, 0.26f); t += 0.01f)
            {
                anim.ForceState(CharacterAnimator.State.Attack, t, total, StaggerTier.None);

                Vector3 actU = (rig.RLowerArm.position - rig.RUpperArm.position).normalized;
                Vector3 actF = (rig.RHand.position - rig.RLowerArm.position).normalized;

                Debug.Log($"[Probe] t={t:F3} elbowR={Vector3.Angle(actU, actF):F0} " +
                    $"reqU=({anim.DebugArmRUpper.x:F2},{anim.DebugArmRUpper.y:F2},{anim.DebugArmRUpper.z:F2}) " +
                    $"actU=({actU.x:F2},{actU.y:F2},{actU.z:F2}) " +
                    $"reqF=({anim.DebugArmRFore.x:F2},{anim.DebugArmRFore.y:F2},{anim.DebugArmRFore.z:F2}) " +
                    $"actF=({actF.x:F2},{actF.y:F2},{actF.z:F2}) " +
                    $"reqAng={Vector3.Angle(anim.DebugArmRUpper, anim.DebugArmRFore):F0}");
            }

            Object.DestroyImmediate(vrm.gameObject);
        }

        /// <summary>
        /// 打ち込みの「後ろ足のかかと上げ」確認用。足元だけを横から大きく描く。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Heel Sheet")]
        public static void CaptureHeelSheet()
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
            floor.transform.localScale = Vector3.one * 8f;
            var floorMat = new Material(Shader.Find("Standard"));
            floorMat.color = new Color(0.24f, 0.25f, 0.28f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;

            Vrm10Instance vrm = Vrm10
                .LoadPathAsync(vrmPath, canLoadVrm0X: true, showMeshes: true)
                .GetAwaiter().GetResult();

            var rig = vrm.gameObject.AddComponent<ProceduralRig>();
            rig.Capture();
            var anim = vrm.gameObject.AddComponent<CharacterAnimator>();
            anim.Rig = rig;

            var bounds = ComputeBounds(vrm.gameObject);
            float h = Mathf.Max(1f, bounds.size.y);

            // 画面外判定で消えないよう、先に身長を測ってから bounds を広げる。
            foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.forceMatrixRecalculationPerRender = true;
                smr.localBounds = new Bounds(Vector3.zero, Vector3.one * 6f);
            }

            var camGo = new GameObject("Heel Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.28f, 0.29f, 0.33f);
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.05f;

            // 横向き・低め。すねから下だけを大きく写す。
            cam.transform.position = new Vector3(h * 0.66f, h * 0.11f, h * 0.16f);
            cam.transform.LookAt(new Vector3(0f, h * 0.09f, -0.03f));

            const int cellW = 460;
            const int cellH = 420;
            const int cols = 4;

            var data = AttackLibrary.Heavy();
            anim.AttackOverride = data;

            float total = data.TotalSeconds;
            float cockEnd = Mathf.Max(0.015f, data.startupSeconds * 0.40f);
            float strikeEnd = Mathf.Max(data.startupSeconds, cockEnd + 0.060f);

            var times = new[] { 0f, cockEnd, strikeEnd, Mathf.Lerp(strikeEnd, total, 0.5f) };

            var sheet = new Texture2D(cellW * cols, cellH, TextureFormat.RGB24, false);

            for (int col = 0; col < cols; col++)
            {
                anim.ForceState(CharacterAnimator.State.Attack, times[col], total, StaggerTier.None);

                // かかとの高さ = 足首 + 足の向きから後ろへ下がった点
                Vector3 heel = rig.RFoot.position
                    + rig.RFoot.rotation * Vector3.forward * -0.06f
                    + rig.RFoot.rotation * Vector3.up * -0.045f;

                Debug.Log($"[Heel] t={times[col]:F3} footRy={rig.RFoot.position.y:F3} " +
                    $"footRz={rig.RFoot.position.z:F3} heelY={heel.y:F3} " +
                    $"toeRy={rig.RToe.position.y:F3} toeRz={rig.RToe.position.z:F3} " +
                    $"footRPitch={rig.RFoot.eulerAngles.x:F0}");

                var cell = RenderCell(cam, cellW, cellH);
                sheet.SetPixels(col * cellW, 0, cellW, cellH, cell.GetPixels());
                Object.DestroyImmediate(cell);
            }

            anim.AttackOverride = null;
            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "heel_sheet.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] heel sheet: {outPath}");
        }

        /// <summary>
        /// 攻撃（弱・強・崩し）の通し確認用シート。実データの時間配分で
        /// 「振りかぶり → 引き切り → 打ち込み → 戻し」の 4 コマを並べる。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Attack Sheet")]
        public static void CaptureAttackSheet()
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
            floor.transform.localScale = Vector3.one * 8f;
            var floorMat = new Material(Shader.Find("Standard"));
            floorMat.color = new Color(0.24f, 0.25f, 0.28f);
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

            var camGo = new GameObject("Attack Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.28f, 0.29f, 0.33f);
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.05f;

            // 斜め前やや高め。突き出した腕の長さと肘の向きが読める角度。
            // 正面寄りだと拳が手前へ飛んでくるので、横向き寄りにして伸びを見せる。
            cam.transform.position = new Vector3(h * 1.78f, h * 0.64f, h * 1.05f);
            cam.transform.LookAt(new Vector3(0f, h * 0.52f, h * 0.10f));

            const int cellW = 460;
            const int cellH = 640;
            const int cols = 4;

            var attacks = new (string name, AttackData data)[]
            {
                ("Light", AttackLibrary.Light()),
                ("Heavy", AttackLibrary.Heavy()),
                ("Break", AttackLibrary.BreakAttack()),
            };

            var sheet = new Texture2D(cellW * cols, cellH * attacks.Length, TextureFormat.RGB24, false);

            // 比較用の基準姿勢（アイドル＝構え）。
            anim.ForceState(CharacterAnimator.State.Idle, 0f, 1f, StaggerTier.None);
            Debug.Log($"[AtkRef] idle hipsY={rig.Hips.position.y:F3} " +
                $"handR=({rig.RHand.position.x:F2},{rig.RHand.position.y:F2},{rig.RHand.position.z:F2}) " +
                $"footL=({rig.LFoot.position.x:F2},{rig.LFoot.position.y:F2},{rig.LFoot.position.z:F2}) " +
                $"footR=({rig.RFoot.position.x:F2},{rig.RFoot.position.y:F2},{rig.RFoot.position.z:F2})");

            for (int row = 0; row < attacks.Length; row++)
            {
                var data = attacks[row].data;
                anim.AttackOverride = data;

                float total = data.TotalSeconds;
                float cockEnd = Mathf.Max(0.015f, data.startupSeconds * 0.40f);
                float strikeEnd = Mathf.Max(data.startupSeconds, cockEnd + 0.060f);

                var times = new[]
                {
                    0f,
                    cockEnd,
                    strikeEnd,
                    Mathf.Lerp(strikeEnd, total, 0.55f),
                };

                for (int col = 0; col < cols; col++)
                {
                    anim.ForceState(CharacterAnimator.State.Attack, times[col], total, StaggerTier.None);

                    var cell = RenderCell(cam, cellW, cellH);
                    sheet.SetPixels(col * cellW, (attacks.Length - 1 - row) * cellH, cellW, cellH,
                        cell.GetPixels());
                    Object.DestroyImmediate(cell);

                    Debug.Log($"[Atk] {attacks[row].name} t={times[col]:F3}/{total:F3} " +
                        $"torsoPitch={anim.DebugTorsoPitch:F1} " +
                        $"hipsY={rig.Hips.position.y:F3} " +
                        $"handR=({rig.RHand.position.x:F2},{rig.RHand.position.y:F2},{rig.RHand.position.z:F2}) " +
                        $"elbowR=({rig.RLowerArm.position.x:F2},{rig.RLowerArm.position.y:F2},{rig.RLowerArm.position.z:F2}) " +
                        $"footL=({rig.LFoot.position.x:F2},{rig.LFoot.position.y:F2},{rig.LFoot.position.z:F2}) " +
                        $"footR=({rig.RFoot.position.x:F2},{rig.RFoot.position.y:F2},{rig.RFoot.position.z:F2}) " +
                        $"toeR=({rig.RToe.position.x:F2},{rig.RToe.position.y:F2},{rig.RToe.position.z:F2})");
                }
            }

            anim.AttackOverride = null;

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "attack_sheet.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] attack sheet: {outPath}");
        }

        /// <summary>
        /// 目視確認用の大きなシート。1コマを大きく撮って、表情とポーズを判定する。
        /// 全身 → 顔 → 攻撃の当たり → 倒れ。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Showcase")]
        public static void CaptureShowcase()
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

            var camGo = new GameObject("Showcase Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.30f, 0.32f, 0.36f);
            cam.nearClipPlane = 0.03f;

            const int cellW = 620;
            const int cellH = 1080;

            var shots = new (CharacterAnimator.State state, float time, float duration,
                StaggerTier tier, Vector3 pos, Vector3 look, float fov, string label)[]
            {
                (CharacterAnimator.State.Idle, 0f, 1f, StaggerTier.None,
                    new Vector3(h * 0.16f, h * 0.95f, h * 2.05f),
                    new Vector3(0f, h * 0.52f, 0f), 34f, "全身"),

                (CharacterAnimator.State.Idle, 0f, 1f, StaggerTier.None,
                    new Vector3(h * 0.18f, h * 0.94f, h * 0.52f),
                    new Vector3(0f, h * 0.92f, 0.04f), 30f, "顔"),

                (CharacterAnimator.State.Attack, 0.050f, 0.6f, StaggerTier.None,
                    new Vector3(h * 1.50f, h * 0.62f, h * 0.62f),
                    new Vector3(0f, h * 0.54f, h * 0.22f), 34f, "攻撃"),

                (CharacterAnimator.State.Down, 5f, 999f, StaggerTier.GuardBreak,
                    new Vector3(h * 0.55f, h * 0.72f, h * 1.05f),
                    new Vector3(0f, h * 0.10f, -h * 0.12f), 40f, "倒れ"),

                // 仰向けかうつ伏せかを判定するための真上からの確認
                (CharacterAnimator.State.Down, 5f, 999f, StaggerTier.GuardBreak,
                    new Vector3(h * 0.02f, h * 1.45f, -h * 0.14f),
                    new Vector3(0f, 0f, -h * 0.14f), 40f, "倒れ(真上)"),
            };

            var sheet = new Texture2D(cellW * shots.Length, cellH, TextureFormat.RGB24, false);

            for (int i = 0; i < shots.Length; i++)
            {
                var s = shots[i];

                anim.AttackOverride = s.state == CharacterAnimator.State.Attack
                    ? AttackLibrary.Light()
                    : null;
                anim.ForceState(s.state, s.time, s.duration, s.tier);

                cam.fieldOfView = s.fov;
                cam.transform.position = s.pos;
                cam.transform.LookAt(s.look);

                var cell = RenderCell(cam, cellW, cellH);
                sheet.SetPixels(i * cellW, 0, cellW, cellH, cell.GetPixels());
                Object.DestroyImmediate(cell);

                Debug.Log($"[Showcase] {s.label} hipsY={rig.Hips.position.y:F3}");
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "showcase.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(floor);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] showcase: {outPath}");
        }

    }
}


