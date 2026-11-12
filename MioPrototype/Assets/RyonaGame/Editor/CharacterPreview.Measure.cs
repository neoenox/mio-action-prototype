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
        /// 肌が露出している面積を数値で測る。服を作り直したときに「減ったか」を判定するため。
        /// 肌をマゼンタ、服と髪を黒で塗って描画し、シルエットに対する肌の割合を出す。
        /// 腕は横へ広げて、腹部の計測に手が混ざらないようにする。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Measure Exposure")]
        public static void MeasureExposure()
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
            anim.ForceState(CharacterAnimator.State.Idle, 0.5f);

            // 腕を真横へ広げる（腹部の計測に手が入らないように）
            Vector3 outL = rig.LUpperArm.position - rig.Hips.position;
            Vector3 outR = rig.RUpperArm.position - rig.Hips.position;
            outL.y = 0f;
            outR.y = 0f;
            rig.SetArm(true, outL.normalized, outL.normalized);
            rig.SetArm(false, outR.normalized, outR.normalized);

            var skinMat = new Material(Shader.Find("Unlit/Color")) { color = Color.magenta };
            var clothMat = new Material(Shader.Find("Unlit/Color")) { color = Color.black };
            int skinSlots = 0;

            foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.forceMatrixRecalculationPerRender = true;

                var mats = smr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    bool isSkin = mats[i] != null &&
                                  mats[i].name.ToLowerInvariant().Contains("skin");
                    if (isSkin)
                        skinSlots++;
                    mats[i] = isSkin ? skinMat : clothMat;
                }
                smr.sharedMaterials = mats;
            }

            var bounds = ComputeBounds(vrm.gameObject);
            float h = Mathf.Max(1f, bounds.size.y);

            var camGo = new GameObject("Exposure Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.15f, 0.35f, 0.15f);
            cam.fieldOfView = 32f;
            cam.nearClipPlane = 0.05f;

            const int w = 560;
            const int hgt = 900;

            // 0 = 正面, 1 = 右, 2 = 背, 3 = 左
            float[] angles = { 0f, 90f, 180f, 270f };

            float bellySkin = 0f, bellyAll = 0f;
            float shinSkin = 0f, shinAll = 0f;
            float totalSkin = 0f, totalAll = 0f;

            for (int v = 0; v < angles.Length; v++)
            {
                float rad = angles[v] * Mathf.Deg2Rad;
                cam.transform.position = new Vector3(
                    Mathf.Sin(rad) * h * 1.9f, h * 0.52f, Mathf.Cos(rad) * h * 1.9f);
                cam.transform.LookAt(new Vector3(0f, h * 0.50f, 0f));

                var tex = RenderCell(cam, w, hgt);

                MeasureSkinMask(tex, out float skin, out float all,
                    out float bSkin, out float bAll, out float sSkin, out float sAll);

                totalSkin += skin;
                totalAll += all;
                bellySkin += bSkin;
                bellyAll += bAll;
                shinSkin += sSkin;
                shinAll += sAll;

                if (v == 0)
                {
                    string root0 = Directory.GetParent(Application.dataPath).FullName;
                    File.WriteAllBytes(Path.Combine(root0, "exposure.png"), tex.EncodeToPNG());
                }

                Object.DestroyImmediate(tex);
            }

            Debug.Log($"[Exposure] 肌マテリアル数={skinSlots}");
            Debug.Log($"[Exposure] 全身の露出率 = {Pct(totalSkin, totalAll):F1}%");
            Debug.Log($"[Exposure] 腹部の露出率 = {Pct(bellySkin, bellyAll):F1}%" +
                      $"  （帯のうち肌の割合）");
            Debug.Log($"[Exposure] すねの露出率 = {Pct(shinSkin, shinAll):F1}%");

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(skinMat);
            Object.DestroyImmediate(clothMat);
            Object.DestroyImmediate(vrm.gameObject);
        }

        static float Pct(float part, float all) => all <= 0f ? 0f : part / all * 100f;

        /// <summary>マゼンタ＝肌。シルエットの内側だけを数える。</summary>
        static void MeasureSkinMask(Texture2D tex, out float skin, out float all,
            out float bellySkin, out float bellyAll, out float shinSkin, out float shinAll)
        {
            int w = tex.width;
            int h = tex.height;
            var px = tex.GetPixels();

            var bg = new Color(0.15f, 0.35f, 0.15f);
            bool IsBg(Color c) =>
                Mathf.Abs(c.r - bg.r) < 0.06f &&
                Mathf.Abs(c.g - bg.g) < 0.06f &&
                Mathf.Abs(c.b - bg.b) < 0.06f;

            bool IsSkin(Color c) => c.r > 0.55f && c.b > 0.55f && c.g < 0.35f;

            int minX = w, maxX = -1, minY = h, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    if (IsBg(px[row + x]))
                        continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            skin = 0f;
            all = 0f;
            bellySkin = 0f;
            bellyAll = 0f;
            shinSkin = 0f;
            shinAll = 0f;

            if (maxX < minX || maxY < minY)
                return;

            float fh = maxY - minY;
            float bellyLo = minY + fh * 0.56f;
            float bellyHi = minY + fh * 0.70f;
            float shinLo = minY + fh * 0.08f;
            float shinHi = minY + fh * 0.24f;

            // 腕を除外するため、シルエットの中央 60% だけを見る
            int cx0 = Mathf.RoundToInt(Mathf.Lerp(minX, maxX, 0.20f));
            int cx1 = Mathf.RoundToInt(Mathf.Lerp(minX, maxX, 0.80f));

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = cx0; x <= cx1; x++)
                {
                    Color c = px[y * w + x];
                    if (IsBg(c))
                        continue;

                    bool s = IsSkin(c);
                    all += 1f;
                    if (s) skin += 1f;

                    if (y >= bellyLo && y <= bellyHi)
                    {
                        bellyAll += 1f;
                        if (s) bellySkin += 1f;
                    }

                    if (y >= shinLo && y <= shinHi)
                    {
                        shinAll += 1f;
                        if (s) shinSkin += 1f;
                    }
                }
            }
        }

        /// <summary>
        /// ダウン姿勢の頭を四方から撮る。倒れたときに髪が顔を覆っているかを、
        /// 実行時の敵・カメラ・スプリングの影響から切り離して確認する。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Down Head")]
        public static void CaptureDownHead()
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

            anim.ForceState(CharacterAnimator.State.Down, 0.90f, 0.9f, StaggerTier.GuardBreak);

            // ダウン中は目を閉じている（PlayerFace と同じ重み 1）。
            // Expression は LateUpdate で反映されるため、エディタではブレンドシェイプを直接入れて見た目を確認する。
            int blinkSet = 0;
            foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = smr.sharedMesh;
                if (mesh == null)
                    continue;

                int idx = mesh.GetBlendShapeIndex("Fcl_EYE_Close");
                if (idx >= 0)
                {
                    smr.SetBlendShapeWeight(idx, 100f);
                    blinkSet++;
                }
            }

            Debug.Log($"[DownHead] eye close set on {blinkSet} renderer(s)");

            var head = rig.Head;
            Vector3 p = head.position;
            Debug.Log($"[DownHead] headPos={p} fwd={head.forward} up={head.up} right={head.right} " +
                      $"hipsY={rig.Hips.position.y:F2}");

            var camGo = new GameObject("Head Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.nearClipPlane = 0.02f;
            cam.fieldOfView = 30f;

            var dirs = new (string name, Vector3 dir)[]
            {
                ("face", head.forward),
                ("up", new Vector3(0.02f, 1f, 0.02f)),
                ("left", -head.right),
                ("right", head.right),
            };

            const int cell = 470;
            var sheet = new Texture2D(cell * dirs.Length, cell, TextureFormat.RGB24, false);

            for (int i = 0; i < dirs.Length; i++)
            {
                Vector3 d = dirs[i].dir.normalized;
                cam.transform.position = p + d * 0.80f;
                cam.transform.LookAt(p);

                var tex = RenderCell(cam, cell, cell);
                sheet.SetPixels(i * cell, 0, cell, cell, tex.GetPixels());
                Object.DestroyImmediate(tex);

                Debug.Log($"[DownHead] {dirs[i].name} cam={cam.transform.position} dir={d}");
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "down_head_sheet.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] down head sheet: {outPath}");
        }

        /// <summary>
        /// ダウン姿勢の腕を複数パターンで並べて撮る。倒れたときの腕の形を選ぶため。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Down Arms")]
        public static void CaptureDownArms()
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

            var variants = new (string name, Vector3 upper, Vector3 fore)[]
            {
                ("A current", new Vector3(0.30f, -1.00f, 0.32f), new Vector3(0.10f, -1.00f, 0.46f)),
                ("B wide", new Vector3(0.55f, -0.95f, 0.25f), new Vector3(0.45f, -0.85f, 0.35f)),
                ("C out", new Vector3(0.85f, -0.60f, 0.10f), new Vector3(0.70f, -0.50f, 0.30f)),
                ("D back", new Vector3(0.85f, -0.50f, -0.25f), new Vector3(0.60f, -0.30f, -0.55f)),
                ("E low", new Vector3(0.45f, -0.90f, -0.30f), new Vector3(0.35f, -0.70f, -0.60f)),
                ("F up", new Vector3(0.70f, -0.60f, 0.60f), new Vector3(0.50f, -0.40f, 0.75f)),
            };

            var camGo = new GameObject("Arm Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.nearClipPlane = 0.02f;
            cam.fieldOfView = 45f;

            const int cell = 420;
            var sheet = new Texture2D(cell * 2, cell * variants.Length, TextureFormat.RGB24, false);

            for (int i = 0; i < variants.Length; i++)
            {
                Vector3 upper = variants[i].upper;
                anim.DownArmLUpper = upper;
                anim.DownArmLFore = variants[i].fore;
                anim.DownArmRUpper = new Vector3(-upper.x, upper.y, -upper.z);
                anim.DownArmRFore = new Vector3(-variants[i].fore.x, variants[i].fore.y, -variants[i].fore.z);

                anim.ForceState(CharacterAnimator.State.Down, 0.90f, 0.9f, StaggerTier.GuardBreak, false);

                Vector3 hips = rig.Hips.position;
                var views = new[]
                {
                    ("front", hips + new Vector3(0.35f, 1.45f, 1.60f)),
                    ("side", hips + new Vector3(-1.70f, 1.00f, 0.60f)),
                };

                for (int v = 0; v < views.Length; v++)
                {
                    cam.transform.position = views[v].Item2;
                    cam.transform.LookAt(hips + Vector3.up * 0.05f);

                    var tex = RenderCell(cam, cell, cell);
                    sheet.SetPixels(v * cell, (variants.Length - 1 - i) * cell, cell, cell, tex.GetPixels());
                    Object.DestroyImmediate(tex);
                }

                Debug.Log($"[DownArms] {variants[i].name} handL={rig.LHand.position} handR={rig.RHand.position}");
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "down_arms_sheet.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] down arms sheet: {outPath}");
        }

        /// <summary>
        /// 立ち上がりモーションを 0.1 秒刻みで並べて撮る。姿勢の破綻を探すため。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Get Up")]
        public static void CaptureGetUp()
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

            const float duration = 0.80f;
            float[] times = { 0.02f, 0.18f, 0.34f, 0.48f, 0.62f, 0.78f };

            var camGo = new GameObject("GetUp Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.nearClipPlane = 0.02f;
            cam.fieldOfView = 45f;

            const int cell = 420;
            var sheet = new Texture2D(cell * times.Length, cell * 2, TextureFormat.RGB24, false);

            for (int i = 0; i < times.Length; i++)
            {
                anim.ForceState(CharacterAnimator.State.GetUp, times[i], duration,
                    StaggerTier.GuardBreak, false);

                Vector3 hips = rig.Hips.position;
                var views = new[]
                {
                    hips + new Vector3(0.60f, 1.55f, 1.85f),   // ゲームに近い前上方
                    hips + new Vector3(-1.85f, 0.85f, 0.55f),  // 横
                };

                for (int v = 0; v < views.Length; v++)
                {
                    cam.transform.position = views[v];
                    cam.transform.LookAt(hips + Vector3.up * 0.05f);

                    var tex = RenderCell(cam, cell, cell);
                    sheet.SetPixels(i * cell, (views.Length - 1 - v) * cell, cell, cell, tex.GetPixels());
                    Object.DestroyImmediate(tex);
                }

                Debug.Log($"[GetUp] t={times[i]:F2} hips={rig.Hips.position} " +
                          $"headY={rig.Head.position.y:F2} handL={rig.LHand.position} handR={rig.RHand.position} " +
                          $"footL={rig.LFoot.position.y:F2} footR={rig.RFoot.position.y:F2}");
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "getup_sheet.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] get up sheet: {outPath}");
        }

        /// <summary>
        /// 被弾ののけぞりを時間で並べて撮る。当たりの「振れ」を調整するため。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Hit Recoil")]
        public static void CaptureHitRecoil()
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

            const float duration = 0.45f;
            float[] times = { 0.01f, 0.03f, 0.06f, 0.10f, 0.16f, 0.26f, 0.40f };

            var camGo = new GameObject("Recoil Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.nearClipPlane = 0.02f;
            cam.fieldOfView = 42f;

            const int cell = 420;
            var sheet = new Texture2D(cell * times.Length, cell * 2, TextureFormat.RGB24, false);

            for (int i = 0; i < times.Length; i++)
            {
                anim.ForceState(CharacterAnimator.State.Stagger, times[i], duration,
                    StaggerTier.Medium, false);

                Vector3 hips = rig.Hips.position;
                var views = new[]
                {
                    hips + new Vector3(0.55f, 0.30f, 1.95f),   // 斜め前
                    hips + new Vector3(-2.05f, 0.22f, 0.12f),  // 真横
                };

                for (int v = 0; v < views.Length; v++)
                {
                    cam.transform.position = views[v];
                    cam.transform.LookAt(hips + Vector3.up * 0.22f);

                    var tex = RenderCell(cam, cell, cell);
                    sheet.SetPixels(i * cell, (views.Length - 1 - v) * cell, cell, cell, tex.GetPixels());
                    Object.DestroyImmediate(tex);
                }

                Debug.Log($"[Recoil] t={times[i]:F2} head={rig.Head.position} torsoPitchApplied");
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "hit_recoil_sheet.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] hit recoil sheet: {outPath}");
        }

        /// <summary>
        /// ダウン姿勢の「メッシュ最下点」と床の隙間を測る。
        /// 体が浮いて見えるときは、この数字を見て腰の高さを下げる。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Measure Down Clearance")]
        public static void MeasureDownClearance()
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

            anim.ForceState(CharacterAnimator.State.Down, 5f, 999f, StaggerTier.GuardBreak, false);

            float overall = float.MaxValue;
            string overallName = "?";

            foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var baked = new Mesh();
                smr.BakeMesh(baked, true);

                Vector3[] verts = baked.vertices;
                Matrix4x4 m = smr.transform.localToWorldMatrix;
                float min = float.MaxValue;
                Vector3 minPoint = Vector3.zero;

                for (int i = 0; i < verts.Length; i++)
                {
                    Vector3 w = m.MultiplyPoint3x4(verts[i]);
                    if (w.y < min)
                    {
                        min = w.y;
                        minPoint = w;
                    }
                }

                if (min < overall)
                {
                    overall = min;
                    overallName = smr.name;
                }

                Debug.Log($"[Clear] {smr.name} minY={min:F3} at={minPoint} verts={verts.Length}");
                Object.DestroyImmediate(baked);
            }

            Debug.Log($"[Clear] OVERALL minY={overall:F3} part={overallName} " +
                      $"head={rig.Head.position.y:F3} hips={rig.Hips.position.y:F3} " +
                      $"handL={rig.LHand.position.y:F3} handR={rig.RHand.position.y:F3} " +
                      $"footL={rig.LFoot.position.y:F3} footR={rig.RFoot.position.y:F3}");
        }

        static Texture2D RenderCell(Camera cam, int width, int height)        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);

            return tex;
        }

        static void SetupLights()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.34f, 0.35f, 0.38f);

            var key = new GameObject("Key").AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.15f;
            key.color = new Color(1f, 0.97f, 0.93f);
            key.transform.rotation = Quaternion.Euler(38f, -30f, 0f);
            key.shadows = LightShadows.Soft;

            var fill = new GameObject("Fill").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.45f;
            fill.color = new Color(0.80f, 0.86f, 1f);
            fill.transform.rotation = Quaternion.Euler(10f, 55f, 0f);

            var rim = new GameObject("Rim").AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.intensity = 0.70f;
            rim.color = new Color(0.85f, 0.90f, 1f);
            rim.transform.rotation = Quaternion.Euler(18f, 165f, 0f);
        }

        static void Shoot(Camera cam, Vector3 position, Vector3 lookAt,
            float fov, int width, int height, string fileName)
        {
            cam.transform.position = position;
            cam.transform.LookAt(lookAt);
            cam.fieldOfView = fov;

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            rt.Release();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, fileName);
            File.WriteAllBytes(outPath, tex.EncodeToPNG());

            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);

            Debug.Log($"[Ryona] wrote {outPath}");
        }

        static Bounds ComputeBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(Vector3.zero, Vector3.one);

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds;
        }
    }
}


