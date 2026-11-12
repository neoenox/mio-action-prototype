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
        /// 表情のモーフが実際に頂点を動かしているかを測る。
        /// ウェイトは入っているのに顔が動かないときは、モーフ側が空。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Scan Face Blends")]
        public static void ScanFaceBlends()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Vrm10Instance vrm = Vrm10
                .LoadPathAsync(vrmPath, canLoadVrm0X: true, showMeshes: true)
                .GetAwaiter().GetResult();

            if (vrm == null)
            {
                Debug.LogError("[Ryona] VRM load returned null");
                return;
            }

            foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = smr.sharedMesh;
                if (mesh == null || mesh.blendShapeCount == 0)
                    continue;

                Debug.Log($"[Blend] mesh={smr.name} shapes={mesh.blendShapeCount} verts={mesh.vertexCount}");

                for (int b = 0; b < mesh.blendShapeCount; b++)
                {
                    string name = mesh.GetBlendShapeName(b);
                    if (!name.StartsWith("Fcl_ALL_") && !name.StartsWith("Fcl_BRW_") &&
                        !name.StartsWith("Fcl_EYE_") && !name.StartsWith("Fcl_MTH_A") &&
                        !name.StartsWith("Fcl_MTH_Close"))
                        continue;

                    int frames = mesh.GetBlendShapeFrameCount(b);
                    if (frames == 0)
                        continue;

                    var delta = new Vector3[mesh.vertexCount];
                    mesh.GetBlendShapeFrameVertices(b, frames - 1, delta, null, null);

                    float max = 0f;
                    int moved = 0;
                    foreach (var d in delta)
                    {
                        float m = d.magnitude;
                        if (m > max) max = m;
                        if (m > 0.0001f) moved++;
                    }

                    Debug.Log($"[Blend] {name}: frames={frames} maxDelta={max * 1000f:F1}mm moved={moved}");
                }
            }

            Object.DestroyImmediate(vrm.gameObject);
            Debug.Log("[Ryona] face blend scan done");
        }

        /// <summary>
        /// エディットモードの描画でモーフが効くかを確かめる。
        /// 直接 SetBlendShapeWeight して、前後で画素が変わるかを見る。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Scan Face Render")]
        public static void ScanFaceRender()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
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
            anim.ForceState(CharacterAnimator.State.Idle, 0.5f, 2f, StaggerTier.None, log: false);

            foreach (var s in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
                s.forceMatrixRecalculationPerRender = true;

            var camGo = new GameObject("Face Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.fieldOfView = 26f;
            cam.nearClipPlane = 0.02f;

            Vector3 face = rig.Head.position + rig.Head.up * 0.055f + rig.Head.forward * 0.05f;
            cam.transform.position = face + new Vector3(0.09f, 0.01f, 0.78f);
            cam.transform.LookAt(face);

            SkinnedMeshRenderer target = null;
            int angryIndex = -1;
            foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (smr.sharedMesh == null) continue;
                for (int b = 0; b < smr.sharedMesh.blendShapeCount; b++)
                {
                    if (smr.sharedMesh.GetBlendShapeName(b) == "Fcl_ALL_Angry")
                    {
                        target = smr;
                        angryIndex = b;
                    }
                }
            }

            if (target == null)
            {
                Debug.LogError("[Ryona] Fcl_ALL_Angry not found");
                return;
            }

            const int size = 320;
            var before = RenderCell(cam, size, size);

            target.SetBlendShapeWeight(angryIndex, 100f);
            Debug.Log($"[FaceRender] set weight, readback={target.GetBlendShapeWeight(angryIndex):F1}");
            var after = RenderCell(cam, size, size);

            var pa = before.GetPixels();
            var pb = after.GetPixels();
            float maxDiff = 0f;
            int changed = 0;
            for (int i = 0; i < pa.Length; i++)
            {
                float d = Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b);
                if (d > maxDiff) maxDiff = d;
                if (d > 0.02f) changed++;
            }

            Debug.Log($"[FaceRender] diff max={maxDiff:F3} changedPixels={changed}");

            // 変化が見えない場合に備えて、両方書き出す
            string root = Directory.GetParent(Application.dataPath).FullName;
            File.WriteAllBytes(Path.Combine(root, "face_render_before.png"), before.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(root, "face_render_after.png"), after.EncodeToPNG());

            Object.DestroyImmediate(before);
            Object.DestroyImmediate(after);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);
            Debug.Log("[Ryona] face render scan done");
        }

        /// <summary>
        /// 苦悶の表情の候補を並べて書き出す。眉・口・目の重ね方を目で選ぶためのもの。
        /// 上から A〜H、最後の 1 マスは無表情。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Face Sheet")]
        public static void CaptureFaceSheet()
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
            anim.ForceState(CharacterAnimator.State.Idle, 0.5f, 2f, StaggerTier.None, log: false);

            // エディットモードでは、これが無いとモーフの結果が描画に反映されない
            foreach (var s in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
                s.forceMatrixRecalculationPerRender = true;

            var camGo = new GameObject("Face Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.fieldOfView = 24f;
            cam.nearClipPlane = 0.02f;

            // 顔の中心は head ボーンより少し上・前。ボーン位置をそのまま見ると顎と首が画の中心に来る。
            Vector3 face = rig.Head.position + rig.Head.up * 0.055f + rig.Head.forward * 0.05f;
            cam.transform.position = face + new Vector3(0.09f, 0.01f, 0.78f);
            cam.transform.LookAt(face);

            var variants = new (string name, float angry, float sad, float mouth, float grit, float blink)[]
            {
                ("A 採用中(食いしばり)", 0.82f, 0.50f, 0.18f, 0.85f, 0.32f),
                ("B 食いしばり最大", 0.85f, 0.45f, 0.10f, 1.00f, 0.40f),
                ("C 開いた口(旧)", 0.82f, 0.50f, 0.95f, 0.00f, 0.32f),
                ("D 食いしばり弱", 0.80f, 0.55f, 0.25f, 0.55f, 0.28f),
                ("E 眉強め", 0.90f, 0.30f, 0.15f, 0.85f, 0.45f),
                ("F 悲しみ寄り", 0.35f, 0.70f, 0.15f, 0.85f, 0.22f),
                ("G 歯だけ", 0.55f, 0.50f, 0.05f, 1.00f, 0.55f),
                ("H 口を結ぶ", 0.45f, 0.30f, 0.10f, 0.30f, 0.35f),
                ("none 無表情", 0f, 0f, 0f, 0f, 0f),
            };

            const int cellW = 380;
            const int cellH = 380;
            const int cols = 3;
            int rows = (variants.Length + cols - 1) / cols;

            var sheet = new Texture2D(cellW * cols, cellH * rows, TextureFormat.RGB24, false);

            for (int i = 0; i < variants.Length; i++)
            {
                var v = variants[i];
                var e = vrm.Runtime.Expression;
                e.SetWeight(ExpressionKey.Angry, v.angry);
                e.SetWeight(ExpressionKey.Sad, v.sad);
                e.SetWeight(ExpressionKey.Aa, v.mouth);
                e.SetWeight(ExpressionKey.Ih, v.grit);
                e.SetWeight(ExpressionKey.Blink, v.blink);
                vrm.Runtime.Process();

                // エディットモードでは Process() を呼ばないと描画に乗らない。
                // 乗っているかを 1 回だけ確認できるよう、実効値を出す。
                if (i == 0)
                {
                    float actual = e.ActualWeights.TryGetValue(ExpressionKey.Angry, out var aw) ? aw : -1f;
                    Debug.Log($"[Face] keys={e.ExpressionKeys.Count} angry actual={actual:F2}");
                }

                var cell = RenderCell(cam, cellW, cellH);
                int cx = i % cols;
                int cy = rows - 1 - (i / cols);   // 上から並べる
                sheet.SetPixels(cx * cellW, cy * cellH, cellW, cellH, cell.GetPixels());
                Object.DestroyImmediate(cell);

                Debug.Log($"[Face] {i} {v.name}: angry={v.angry:F2} sad={v.sad:F2} " +
                          $"mouth={v.mouth:F2} grit={v.grit:F2} blink={v.blink:F2}");
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "face_sheet.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] face sheet: {outPath}");
        }

        /// <summary>
        /// 顔ヒットと腹パンで苦悶の表情がどう変わるかを並べて書き出す。
        /// <see cref="PlayerFace.DefaultFacePain"/> / <see cref="PlayerFace.DefaultBellyPain"/> を
        /// そのまま使うので、コードの値を変えたらこのシートで見た目を確認できる。
        /// 左から 顔・強 / 腹・弱 / 腹・中 / 腹・強 / 無表情。
        /// 「弱」は StaggerTier.Small の濃さ（0.66 + BellyPeakBonus）。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Pain Faces")]
        public static void CapturePainFaces()
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
            anim.ForceState(CharacterAnimator.State.Idle, 0.5f, 2f, StaggerTier.None, log: false);

            // エディットモードでは、これが無いとモーフの結果が描画に反映されない
            foreach (var s in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
                s.forceMatrixRecalculationPerRender = true;

            var camGo = new GameObject("Face Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.fieldOfView = 24f;
            cam.nearClipPlane = 0.02f;

            Vector3 face = rig.Head.position + rig.Head.up * 0.055f + rig.Head.forward * 0.05f;
            cam.transform.position = face + new Vector3(0.09f, 0.01f, 0.78f);
            cam.transform.LookAt(face);

            var belly = PlayerFace.DefaultBellyPain;
            var none = default(PlayerFace.PainShape);

            var variants = new (string name, PlayerFace.PainShape shape)[]
            {
                ("顔・強(現行)", PlayerFace.DefaultFacePain),
                ("腹・弱", PlayerFace.PainShape.Lerp(none, belly, 0.76f)),
                ("腹・中", PlayerFace.PainShape.Lerp(none, belly, 0.98f)),
                ("腹・強", belly),
                ("無表情", none),
            };

            const int cellW = 380;
            const int cellH = 380;
            int cols = variants.Length;
            int rows = 1;

            var sheet = new Texture2D(cellW * cols, cellH * rows, TextureFormat.RGB24, false);

            for (int i = 0; i < variants.Length; i++)
            {
                var v = variants[i];
                var e = vrm.Runtime.Expression;
                e.SetWeight(ExpressionKey.Angry, v.shape.Angry);
                e.SetWeight(ExpressionKey.Sad, v.shape.Sad);
                e.SetWeight(ExpressionKey.Aa, v.shape.Mouth);
                e.SetWeight(ExpressionKey.Ih, v.shape.Grit);
                e.SetWeight(ExpressionKey.Blink, v.shape.Blink);
                vrm.Runtime.Process();

                var cell = RenderCell(cam, cellW, cellH);
                int cx = i % cols;
                int cy = rows - 1 - (i / cols);
                sheet.SetPixels(cx * cellW, cy * cellH, cellW, cellH, cell.GetPixels());
                Object.DestroyImmediate(cell);

                Debug.Log($"[Pain] {i} {v.name}: angry={v.shape.Angry:F2} sad={v.shape.Sad:F2} " +
                          $"mouth={v.shape.Mouth:F2} grit={v.shape.Grit:F2} blink={v.shape.Blink:F2}");
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "pain_faces.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] pain faces: {outPath}");
        }

        /// <summary>
        /// 腕が「肩の高さで真横に伸びている」なら false を返す（T ポーズ判定）。
        /// 体の向きに左右されないよう、腰のローカル座標で測る。
        /// </summary>
        static bool CheckSide(ProceduralRig rig, bool left, out float sideways, out float dy)
        {
            var shoulder = left ? rig.LUpperArm : rig.RUpperArm;
            var hand = left ? rig.LHand : rig.RHand;

            Vector3 v = rig.Hips.InverseTransformDirection(hand.position - shoulder.position);
            sideways = Mathf.Abs(v.x);
            dy = v.y;

            // 上腕そのものが真横を向いていたら、Aim が効かず休止姿勢（T ポーズ）のまま
            var elbow = left ? rig.LLowerArm : rig.RLowerArm;
            Vector3 upper = rig.Hips.InverseTransformDirection((elbow.position - shoulder.position).normalized);
            if (Mathf.Abs(upper.x) > 0.85f)
                return false;

            bool straightOut = sideways > 0.26f               // 横に伸びている
                && Mathf.Abs(v.y) < 0.10f                      // 肩と同じ高さ
                && Mathf.Abs(v.z) < 0.16f;                     // 前にも後ろにも出ていない
            return !straightOut;
        }

        /// <summary>
        /// ガードの姿勢を確認する。Shift を押している間の構えと、殴られた瞬間の押し返され。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Guard Strip")]
        public static void CaptureGuardStrip()
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

            var camGo = new GameObject("Guard Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.fieldOfView = 38f;
            cam.nearClipPlane = 0.05f;

            var shots = new (CharacterAnimator.State state, float recoil, int view, string label)[]
            {
                (CharacterAnimator.State.Idle, 0f, 0, "idle 横"),
                (CharacterAnimator.State.Guard, 0f, 0, "guard 横"),
                (CharacterAnimator.State.Guard, 0f, 2, "guard 正面"),
                (CharacterAnimator.State.Guard, 1f, 0, "guard 被弾"),
            };

            const int cellW = 380;
            const int cellH = 640;

            var sheet = new Texture2D(cellW * shots.Length, cellH, TextureFormat.RGB24, false);

            for (int i = 0; i < shots.Length; i++)
            {
                var s = shots[i];
                anim.AttackOverride = null;
                anim.ForceState(s.state, 0.5f, 2f, StaggerTier.None);
                anim.ForceGuardRecoil(s.recoil);
                anim.ApplyPose();

                if (s.view == 0)
                {
                    cam.transform.position = new Vector3(h * 2.30f, h * 0.52f, h * 0.28f);
                    cam.transform.LookAt(new Vector3(0f, h * 0.50f, 0f));
                }
                else
                {
                    cam.transform.position = new Vector3(h * 0.22f, h * 0.55f, h * 2.15f);
                    cam.transform.LookAt(new Vector3(0f, h * 0.50f, 0f));
                }

                var cell = RenderCell(cam, cellW, cellH);
                sheet.SetPixels(i * cellW, 0, cellW, cellH, cell.GetPixels());
                Object.DestroyImmediate(cell);

                Debug.Log($"[GuardStrip] {s.label} handRy={rig.RHand.position.y:F3} handRz={rig.RHand.position.z:F3} " +
                          $"handLy={rig.LHand.position.y:F3} handLz={rig.LHand.position.z:F3} " +
                          $"hipsY={rig.Hips.position.y:F3}");
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "guard_strip.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(floor);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] guard strip: {outPath}");
        }

        /// <summary>
        /// 立ち姿（アイドル＝ファイティングスタンス）を 4 方向から並べて確認する。
        /// 構えが「様になっているか」は正面・横・斜め・後ろを見ないと判断できない。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Stance Sheet")]
        public static void CaptureStanceSheet()
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

            var camGo = new GameObject("Stance Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.fieldOfView = 36f;
            cam.nearClipPlane = 0.05f;

            var shots = new (CharacterAnimator.State state, float time, int view, string label)[]
            {
                (CharacterAnimator.State.Idle, 0.5f, 0, "idle 正面"),
                (CharacterAnimator.State.Idle, 0.5f, 1, "idle 斜め"),
                (CharacterAnimator.State.Idle, 0.5f, 2, "idle 横"),
                (CharacterAnimator.State.Idle, 0.5f, 3, "idle 後ろ"),
                (CharacterAnimator.State.Guard, 0.5f, 0, "guard 正面"),
                (CharacterAnimator.State.Guard, 0.5f, 2, "guard 横"),
            };

            const int cellW = 380;
            const int cellH = 640;

            var sheet = new Texture2D(cellW * shots.Length, cellH, TextureFormat.RGB24, false);

            for (int i = 0; i < shots.Length; i++)
            {
                var s = shots[i];
                anim.AttackOverride = null;
                anim.ForceState(s.state, s.time, 2f, StaggerTier.None);

                Vector3 eye;
                switch (s.view)
                {
                    case 0: eye = new Vector3(h * 0.18f, h * 0.55f, h * 2.15f); break;   // 正面
                    case 1: eye = new Vector3(h * 1.35f, h * 0.55f, h * 1.65f); break;   // 斜め前
                    case 2: eye = new Vector3(h * 2.30f, h * 0.52f, h * 0.10f); break;   // 横
                    default: eye = new Vector3(h * 0.10f, h * 0.55f, -h * 2.15f); break; // 後ろ
                }

                cam.transform.position = eye;
                cam.transform.LookAt(new Vector3(0f, h * 0.48f, 0f));

                var cell = RenderCell(cam, cellW, cellH);
                sheet.SetPixels(i * cellW, 0, cellW, cellH, cell.GetPixels());
                Object.DestroyImmediate(cell);

                Debug.Log($"[Stance] {s.label} handL=({rig.LHand.position.x:F2},{rig.LHand.position.y:F2},{rig.LHand.position.z:F2}) " +
                          $"handR=({rig.RHand.position.x:F2},{rig.RHand.position.y:F2},{rig.RHand.position.z:F2}) " +
                          $"hipsY={rig.Hips.position.y:F3} headY={rig.Head.position.y:F3} " +
                          $"footL={rig.LFoot.position} footR={rig.RFoot.position}");
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "stance_sheet.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(floor);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] stance sheet: {outPath}");
        }

        /// <summary>
        /// 敵の人型モーションを並べて確認する。予備動作 → 振り抜き → 戻しが読めるかを見る。
        /// </summary>
        /// <summary>
        /// 胸の X 回転だけを振って「+ が前傾か後傾か」を実測する。
        /// この符号は何度も取り違えているので、シートで確定させる。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Torso Pitch")]
        public static void CaptureTorsoPitch()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLights();

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = Vector3.one * 6f;
            var floorMat = new Material(Shader.Find("Standard"));
            floorMat.color = new Color(0.22f, 0.23f, 0.26f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;

            var skin = new Material(Shader.Find("Standard"));
            skin.color = new Color(0.63f, 0.51f, 0.45f);
            var cloth = new Material(Shader.Find("Standard"));
            cloth.color = new Color(0.35f, 0.30f, 0.28f);

            var figureGo = new GameObject("Enemy Figure");
            var figure = figureGo.AddComponent<EnemyFigure>();
            figure.BodyMaterial = skin;
            figure.ClothMaterial = cloth;

            var camGo = new GameObject("Enemy Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.fieldOfView = 38f;
            cam.nearClipPlane = 0.05f;

            float h = 1.75f;
            var angles = new[] { -40f, -20f, 0f, 20f, 40f };
            const int cellW = 360;
            const int cellH = 620;
            var sheet = new Texture2D(cellW * angles.Length, cellH, TextureFormat.RGB24, false);

            figure.Preview(EnemyPhase.Chase, 0f, AttackId.Jab, 0f, 0f);
            Transform chest = figure.ChestBone;
            Quaternion rest = chest.localRotation;

            for (int i = 0; i < angles.Length; i++)
            {
                chest.localRotation = rest * Quaternion.Euler(angles[i], 0f, 0f);

                cam.transform.position = new Vector3(h * 1.95f, h * 0.55f, h * 0.30f);
                cam.transform.LookAt(new Vector3(0f, h * 0.50f, 0f));

                var cell = RenderCell(cam, cellW, cellH);
                sheet.SetPixels(i * cellW, 0, cellW, cellH, cell.GetPixels());
                Object.DestroyImmediate(cell);

                Debug.Log($"[TorsoPitch] cell {i} angle={angles[i]}");
            }

            chest.localRotation = rest;
            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "torso_pitch.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(figureGo);
            Object.DestroyImmediate(floor);

            Debug.Log($"[Ryona] torso pitch: {outPath}");
        }

        /// <summary>
        /// よろけ（食らい）の姿勢を時間方向に並べて確認する。
        /// 「のけぞるだけ」に見えないか、頭と腕が遅れて付いてくるかを見る。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Stagger")]
        public static void CaptureStagger()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLights();

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = Vector3.one * 6f;
            var floorMat = new Material(Shader.Find("Standard"));
            floorMat.color = new Color(0.22f, 0.23f, 0.26f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;

            var skin = new Material(Shader.Find("Standard"));
            skin.color = new Color(0.63f, 0.51f, 0.45f);
            var cloth = new Material(Shader.Find("Standard"));
            cloth.color = new Color(0.35f, 0.30f, 0.28f);

            var figureGo = new GameObject("Enemy Figure");
            var figure = figureGo.AddComponent<EnemyFigure>();
            figure.BodyMaterial = skin;
            figure.ClothMaterial = cloth;

            var camGo = new GameObject("Enemy Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.fieldOfView = 38f;
            cam.nearClipPlane = 0.05f;

            float h = 1.75f;
            var times = new[] { 0.00f, 0.04f, 0.08f, 0.13f, 0.19f, 0.26f, 0.34f, 0.44f };

            const int cellW = 300;
            const int cellH = 620;
            var sheet = new Texture2D(cellW * times.Length, cellH, TextureFormat.RGB24, false);

            for (int i = 0; i < times.Length; i++)
            {
                figure.PreviewStagger(times[i]);

                cam.transform.position = new Vector3(h * 1.95f, h * 0.55f, h * 0.30f);
                cam.transform.LookAt(new Vector3(0f, h * 0.50f, 0f));

                var cell = RenderCell(cam, cellW, cellH);
                sheet.SetPixels(i * cellW, 0, cellW, cellH, cell.GetPixels());
                Object.DestroyImmediate(cell);
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "enemy_stagger.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(figureGo);
            Object.DestroyImmediate(floor);

            Debug.Log($"[Ryona] enemy stagger: {outPath}");
        }

    }
}


