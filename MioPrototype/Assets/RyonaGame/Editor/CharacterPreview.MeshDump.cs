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
        /// レンダラーを通さず、光線と三角形の交差だけで
        /// 「股間の各点で、肌と布のどちらが手前か」を判定する。
        /// 描画順・深度・マテリアルの影響を完全に排除できる。
        /// </summary>
        [MenuItem("Ryona/Debug/Surface Map")]
        public static void SurfaceMap()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
            if (!File.Exists(vrmPath))
                return;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Vrm10Instance vrm = Vrm10
                .LoadPathAsync(vrmPath, canLoadVrm0X: true, showMeshes: true)
                .GetAwaiter().GetResult();

            var body = System.Array.Find(vrm.GetComponentsInChildren<SkinnedMeshRenderer>(), r => r.name == "Body");
            if (body == null)
            {
                Debug.LogError("[Ryona] Body mesh not found");
                return;
            }

            Mesh mesh = body.sharedMesh;
            Vector3[] verts = mesh.vertices;
            Matrix4x4 m = body.transform.localToWorldMatrix;

            int skinSlot = -1, pantsSlot = -1;
            for (int i = 0; i < body.sharedMaterials.Length; i++)
            {
                if (body.sharedMaterials[i] == null) continue;
                string n = body.sharedMaterials[i].name.ToLowerInvariant();
                if (n.Contains("skin")) skinSlot = i;
                if (n.Contains("bottoms")) pantsSlot = i;
            }

            Vector3[][] Tri(int sub)
            {
                int[] t = mesh.GetTriangles(sub);
                var arr = new Vector3[t.Length / 3][];
                for (int i = 0; i < t.Length; i += 3)
                {
                    arr[i / 3] = new[]
                    {
                        m.MultiplyPoint3x4(verts[t[i]]),
                        m.MultiplyPoint3x4(verts[t[i + 1]]),
                        m.MultiplyPoint3x4(verts[t[i + 2]])
                    };
                }
                return arr;
            }

            Vector3[][] skinTris = Tri(skinSlot);
            Vector3[][] pantsTris = Tri(pantsSlot);

            static float Hit(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 e1 = b - a, e2 = c - a;
                Vector3 p = Vector3.Cross(d, e2);
                float det = Vector3.Dot(e1, p);
                if (Mathf.Abs(det) < 1e-9f) return -1f;

                float inv = 1f / det;
                Vector3 tv = o - a;
                float u = Vector3.Dot(tv, p) * inv;
                if (u < 0f || u > 1f) return -1f;

                Vector3 q = Vector3.Cross(tv, e1);
                float v = Vector3.Dot(d, q) * inv;
                if (v < 0f || u + v > 1f) return -1f;

                float t = Vector3.Dot(e2, q) * inv;
                return t > 0f ? t : -1f;
            }

            // 上から下へ走査して、各点で最も手前の面が何かを 1 文字で出す
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[Map]  S=肌が手前  P=布が手前  .=何も無い   (x: -6cm..+6cm, y: 96cm..70cm)");

            for (float y = 0.96f; y >= 0.70f; y -= 0.01f)
            {
                var row = new System.Text.StringBuilder();
                row.Append($"y={y:F2} ");
                for (float x = -0.06f; x <= 0.061f; x += 0.012f)
                {
                    Vector3 o = new Vector3(x, y, 3f);
                    Vector3 d = new Vector3(0f, 0f, -1f);
                    float bestS = float.MaxValue, bestP = float.MaxValue;

                    foreach (var tri in skinTris)
                    {
                        float t = Hit(o, d, tri[0], tri[1], tri[2]);
                        if (t > 0f && t < bestS) bestS = t;
                    }
                    foreach (var tri in pantsTris)
                    {
                        float t = Hit(o, d, tri[0], tri[1], tri[2]);
                        if (t > 0f && t < bestP) bestP = t;
                    }

                    if (bestS == float.MaxValue && bestP == float.MaxValue) row.Append('.');
                    else if (bestS <= bestP) row.Append('S');
                    else row.Append('P');
                }
                sb.AppendLine(row.ToString());
            }

            Debug.Log(sb.ToString());

            Object.DestroyImmediate(vrm.gameObject);
            Debug.Log("[Ryona] surface map done");
        }

        /// <summary>
        /// 同じ構図で「布だけ」「肌だけ」「両方を平坦色」の3枚を出して、
        /// 見えているのが本当に手前の面かどうかを確認する。
        /// </summary>
        [MenuItem("Ryona/Debug/Flat Test")]
        public static void FlatTest()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
            if (!File.Exists(vrmPath))
                return;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLights();

            Vrm10Instance vrm = Vrm10
                .LoadPathAsync(vrmPath, canLoadVrm0X: true, showMeshes: true)
                .GetAwaiter().GetResult();

            var rig = vrm.gameObject.AddComponent<ProceduralRig>();
            rig.Capture();
            var animator = vrm.gameObject.AddComponent<CharacterAnimator>();
            animator.Rig = rig;
            animator.ForceState(CharacterAnimator.State.Idle, 0.5f);

            var body = System.Array.Find(vrm.GetComponentsInChildren<SkinnedMeshRenderer>(), r => r.name == "Body");
            if (body == null)
                return;

            body.forceMatrixRecalculationPerRender = true;

            int skinSlot = -1, pantsSlot = -1;
            for (int i = 0; i < body.sharedMaterials.Length; i++)
            {
                if (body.sharedMaterials[i] == null) continue;
                string n = body.sharedMaterials[i].name.ToLowerInvariant();
                if (n.Contains("skin")) skinSlot = i;
                if (n.Contains("bottoms")) pantsSlot = i;
            }

            var bounds = ComputeBounds(vrm.gameObject);
            float h = Mathf.Max(1f, bounds.size.y);
            float focusY = bounds.center.y;

            var camGo = new GameObject("Flat Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.05f, 0.07f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 30f;

            Vector3 camPos = new Vector3(0f, focusY + h * 0.05f, h * 2.05f);
            Vector3 lookAt = new Vector3(0f, focusY, 0f);

            var flat = Shader.Find("Unlit/Color");

            // 1) 両方を平坦色で（布=赤 / 肌=緑）
            var mats = (Material[])body.sharedMaterials.Clone();
            for (int i = 0; i < mats.Length; i++)
            {
                Color c = Color.gray;
                if (i == pantsSlot) c = Color.red;
                else if (i == skinSlot) c = Color.green;
                else if (mats[i] != null && mats[i].name.ToLowerInvariant().Contains("tops")) c = Color.blue;
                else if (mats[i] != null && mats[i].name.ToLowerInvariant().Contains("shoes")) c = Color.yellow;
                else if (mats[i] != null && mats[i].name.ToLowerInvariant().Contains("hair")) c = new Color(0.5f, 0.3f, 0.1f);
                mats[i] = new Material(flat) { color = c };
            }
            body.sharedMaterials = mats;
            Shoot(cam, camPos, lookAt, 30f, 720, 1080, "flat_both.png");

            // 2) 布だけ（メッシュから抽出）
            var pantsGo = new GameObject("Pants Only");
            pantsGo.transform.SetParent(body.transform.parent, false);
            pantsGo.transform.localPosition = body.transform.localPosition;
            pantsGo.transform.localRotation = body.transform.localRotation;
            pantsGo.transform.localScale = body.transform.localScale;

            var psr = pantsGo.AddComponent<SkinnedMeshRenderer>();
            psr.sharedMesh = ExtractSubMesh(body.sharedMesh, pantsSlot);
            psr.bones = body.bones;
            psr.rootBone = body.rootBone;
            psr.forceMatrixRecalculationPerRender = true;
            psr.sharedMaterial = new Material(flat) { color = Color.red };

            // 3) 肌だけ
            var skinGo = new GameObject("Skin Only");
            skinGo.transform.SetParent(body.transform.parent, false);
            skinGo.transform.localPosition = body.transform.localPosition;
            skinGo.transform.localRotation = body.transform.localRotation;
            skinGo.transform.localScale = body.transform.localScale;

            var ssr = skinGo.AddComponent<SkinnedMeshRenderer>();
            ssr.sharedMesh = ExtractSubMesh(body.sharedMesh, skinSlot);
            ssr.bones = body.bones;
            ssr.rootBone = body.rootBone;
            ssr.forceMatrixRecalculationPerRender = true;
            ssr.sharedMaterial = new Material(flat) { color = Color.green };

            body.enabled = false;
            skinGo.SetActive(false);
            Shoot(cam, camPos, lookAt, 30f, 720, 1080, "flat_pants.png");

            skinGo.SetActive(true);
            pantsGo.SetActive(false);
            Shoot(cam, camPos, lookAt, 30f, 720, 1080, "flat_skin.png");

            Object.DestroyImmediate(pantsGo);
            Object.DestroyImmediate(skinGo);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log("[Ryona] flat test done");
        }

        /// <summary>
        /// 何も付けずに読み込んだだけ（VRMの素の姿勢）で股間がどう見えるか。
        /// 私のリグ／ポーズを通した場合と比べて、原因が私の側かモデル側かを分ける。
        /// </summary>
        [MenuItem("Ryona/Debug/Raw Render")]
        public static void RawRender()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
            if (!File.Exists(vrmPath))
                return;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLights();

            Vrm10Instance vrm = Vrm10
                .LoadPathAsync(vrmPath, canLoadVrm0X: true, showMeshes: true)
                .GetAwaiter().GetResult();

            // リグもアニメータも付けない
            var bounds = ComputeBounds(vrm.gameObject);
            float h = Mathf.Max(1f, bounds.size.y);
            float focusY = bounds.center.y;

            var camGo = new GameObject("Raw Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.30f, 0.31f, 0.34f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 30f;

            Shoot(cam, new Vector3(0f, focusY + h * 0.05f, h * 2.05f),
                new Vector3(0f, focusY, 0f), 30f, 720, 1080, "raw_full.png");

            Shoot(cam, new Vector3(0f, 0.88f, 0.95f), new Vector3(0f, 0.82f, 0f),
                30f, 720, 720, "raw_crotch.png");

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log("[Ryona] raw render done");
        }

        /// <summary>
        /// 私のポーズ処理のどこで股間が壊れるかを1段ずつ切り分ける。
        /// A: 素 / B: Capture直後 / C: ResetPose後 / D: Idleポーズ適用後
        /// </summary>
        [MenuItem("Ryona/Debug/Bisect Pose")]
        public static void BisectPose()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
            if (!File.Exists(vrmPath))
                return;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLights();

            Vrm10Instance vrm = Vrm10
                .LoadPathAsync(vrmPath, canLoadVrm0X: true, showMeshes: true)
                .GetAwaiter().GetResult();

            var camGo = new GameObject("Bisect Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.30f, 0.31f, 0.34f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 30f;

            Vector3 camPos = new Vector3(0f, 0.88f, 0.95f);
            Vector3 lookAt = new Vector3(0f, 0.82f, 0f);

            Shoot(cam, camPos, lookAt, 30f, 720, 720, "bisect_a_raw.png");

            var rig = vrm.gameObject.AddComponent<ProceduralRig>();
            rig.Capture();

            var hips = rig.Hips;
            Debug.Log($"[Bisect] capture hipsPos={hips.localPosition} hipsRot={hips.localRotation.eulerAngles}");

            Shoot(cam, camPos, lookAt, 30f, 720, 720, "bisect_b_capture.png");

            rig.ResetPose();
            Debug.Log($"[Bisect] reset    hipsPos={hips.localPosition} hipsRot={hips.localRotation.eulerAngles}");
            Debug.Log($"[Bisect] thighL rot={rig.LUpperLeg.localRotation.eulerAngles}");
            Shoot(cam, camPos, lookAt, 30f, 720, 720, "bisect_c_reset.png");

            var anim = vrm.gameObject.AddComponent<CharacterAnimator>();
            anim.Rig = rig;
            anim.ForceState(CharacterAnimator.State.Idle, 0.5f);
            Debug.Log($"[Bisect] idle     hipsPos={hips.localPosition} hipsRot={hips.localRotation.eulerAngles}");
            Debug.Log($"[Bisect] thighL rot={rig.LUpperLeg.localRotation.eulerAngles}");
            Shoot(cam, camPos, lookAt, 30f, 720, 720, "bisect_d_idle.png");

            // 腕を付けただけで壊れるか（脚は触らない）
            rig.ResetPose();
            Shoot(cam, camPos, lookAt, 30f, 720, 720, "bisect_e_after_idle_reset.png");

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log("[Ryona] bisect done");
        }

        /// <summary>
        /// 服のポリゴンの裏表（カリング）が原因かを確認する。
        /// A: 通常 / B: 服を両面描画。Bで股間が塞がれば法線か巻き順の問題。
        /// </summary>
        [MenuItem("Ryona/Debug/Cull Test")]
        public static void CullTest()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
            if (!File.Exists(vrmPath))
                return;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLights();

            Vrm10Instance vrm = Vrm10
                .LoadPathAsync(vrmPath, canLoadVrm0X: true, showMeshes: true)
                .GetAwaiter().GetResult();

            var rig = vrm.gameObject.AddComponent<ProceduralRig>();
            rig.Capture();
            var animator = vrm.gameObject.AddComponent<CharacterAnimator>();
            animator.Rig = rig;
            animator.ForceState(CharacterAnimator.State.Idle, 0.5f);

            var bounds = ComputeBounds(vrm.gameObject);
            float h = Mathf.Max(1f, bounds.size.y);
            float focusY = bounds.center.y;

            var camGo = new GameObject("Cull Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.30f, 0.31f, 0.34f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 30f;

            Vector3 camPos = new Vector3(0f, focusY + h * 0.05f, h * 2.05f);
            Vector3 lookAt = new Vector3(0f, focusY, 0f);

            Shoot(cam, camPos, lookAt, 30f, 720, 1080, "cull_a_normal.png");

            // 服（Bottoms / Tops）だけ両面にする
            int changed = 0;
            foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                foreach (var mat in smr.materials)
                {
                    if (mat == null)
                        continue;
                    if (!mat.name.ToLowerInvariant().Contains("cloth"))
                        continue;

                    if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", 0);
                    if (mat.HasProperty("_CullMode")) mat.SetInt("_CullMode", 0);
                    if (mat.HasProperty("_DoubleSided")) mat.SetFloat("_DoubleSided", 1f);
                    changed++;
                    Debug.Log($"[Cull] double-sided: {mat.name} shader={mat.shader.name}");
                }
            }
            Debug.Log($"[Cull] changed {changed} cloth materials");

            Shoot(cam, camPos, lookAt, 30f, 720, 1080, "cull_b_doublesided.png");

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log("[Ryona] cull test done");
        }

        /// <summary>
        /// 私のポーズ処理が、意図していないボーンを動かしていないかを調べる。
        /// Capture → ResetPose の前後で全ヒューマノイドボーンの値を比較する。
        /// </summary>
        [MenuItem("Ryona/Debug/Pose Diff")]
        public static void PoseDiff()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
            if (!File.Exists(vrmPath))
                return;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Vrm10Instance vrm = Vrm10
                .LoadPathAsync(vrmPath, canLoadVrm0X: true, showMeshes: true)
                .GetAwaiter().GetResult();

            var animatorComp = vrm.GetComponentInChildren<Animator>();
            if (animatorComp == null || !animatorComp.isHuman)
            {
                Debug.LogError("[Diff] humanoid animator not found");
                return;
            }

            var ids = (HumanBodyBones[])System.Enum.GetValues(typeof(HumanBodyBones));
            var before = new System.Collections.Generic.Dictionary<HumanBodyBones, (Vector3 p, Quaternion r)>();

            foreach (var id in ids)
            {
                if (id == HumanBodyBones.LastBone)
                    continue;
                var t = animatorComp.GetBoneTransform(id);
                if (t != null)
                    before[id] = (t.localPosition, t.localRotation);
            }

            var rig = vrm.gameObject.AddComponent<ProceduralRig>();
            rig.Capture();
            rig.ResetPose();

            int diffs = 0;
            foreach (var kv in before)
            {
                var t = animatorComp.GetBoneTransform(kv.Key);
                if (t == null) continue;

                float angle = Quaternion.Angle(kv.Value.r, t.localRotation);
                float posDelta = (kv.Value.p - t.localPosition).magnitude;

                if (angle > 0.05f || posDelta > 0.0005f)
                {
                    diffs++;
                    Debug.Log($"[Diff] {kv.Key,-16} angle={angle:F2}deg  pos={posDelta * 1000f:F2}mm");
                }
            }

            Debug.Log($"[Diff] 変化したボーン数 = {diffs} / {before.Count}");

            Object.DestroyImmediate(vrm.gameObject);
            Debug.Log("[Ryona] pose diff done");
        }

        /// <summary>
        /// カメラを完全に固定したまま、素 / 腕だけ / Idle全体 を撮り比べる。
        /// </summary>
        [MenuItem("Ryona/Debug/Pose Compare")]
        public static void PoseCompare()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
            if (!File.Exists(vrmPath))
                return;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLights();

            Vrm10Instance vrm = Vrm10
                .LoadPathAsync(vrmPath, canLoadVrm0X: true, showMeshes: true)
                .GetAwaiter().GetResult();

            // カメラは「素の状態」のサイズで固定する
            var bounds = ComputeBounds(vrm.gameObject);
            float h = Mathf.Max(1f, bounds.size.y);
            float focusY = bounds.center.y;

            var camGo = new GameObject("Cmp Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.30f, 0.31f, 0.34f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 30f;

            Vector3 camPos = new Vector3(0f, focusY + h * 0.05f, h * 2.05f);
            Vector3 lookAt = new Vector3(0f, focusY, 0f);

            Shoot(cam, camPos, lookAt, 30f, 720, 1080, "cmp_a_raw.png");

            foreach (var s in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
                s.forceMatrixRecalculationPerRender = true;
            Shoot(cam, camPos, lookAt, 30f, 720, 1080, "cmp_a2_raw_forced.png");

            var rig = vrm.gameObject.AddComponent<ProceduralRig>();
            rig.Capture();

            // 腕だけ
            rig.ResetPose();
            rig.SetArm(true, new Vector3(0.30f, -1f, 0.05f).normalized, new Vector3(0.10f, -1f, 0.18f).normalized);
            rig.SetArm(false, new Vector3(-0.30f, -1f, 0.05f).normalized, new Vector3(-0.10f, -1f, 0.18f).normalized);
            Shoot(cam, camPos, lookAt, 30f, 720, 1080, "cmp_b_arms.png");

            // 腰を 6mm 動かすだけ
            rig.ResetPose();
            rig.OffsetHips(new Vector3(0f, 0.006f, 0f));
            Shoot(cam, camPos, lookAt, 30f, 720, 1080, "cmp_c_hips.png");

            // Idle 全体
            var anim = vrm.gameObject.AddComponent<CharacterAnimator>();
            anim.Rig = rig;
            anim.ForceState(CharacterAnimator.State.Idle, 0.5f);
            Shoot(cam, camPos, lookAt, 30f, 720, 1080, "cmp_d_idle.png");

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log("[Ryona] pose compare done");
        }

        /// <summary>
        /// 同じ状態を連続で3枚撮る。1枚目だけ壊れるなら「初回レンダリング時の
        /// スキンメッシュの行列が古い」というプレビュー特有の問題。
        /// </summary>
        [MenuItem("Ryona/Debug/Var Test")]
        public static void VarTest()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
            if (!File.Exists(vrmPath))
                return;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLights();

            Vrm10Instance vrm = Vrm10
                .LoadPathAsync(vrmPath, canLoadVrm0X: true, showMeshes: true)
                .GetAwaiter().GetResult();

            var rig = vrm.gameObject.AddComponent<ProceduralRig>();
            rig.Capture();
            var animator = vrm.gameObject.AddComponent<CharacterAnimator>();
            animator.Rig = rig;
            animator.ForceState(CharacterAnimator.State.Idle, 0.5f);

            foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
                smr.forceMatrixRecalculationPerRender = true;

            var bounds = ComputeBounds(vrm.gameObject);
            float h = Mathf.Max(1f, bounds.size.y);
            float focusY = bounds.center.y;

            var camGo = new GameObject("Var Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.30f, 0.31f, 0.34f);
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 50f;

            Vector3 camPos = new Vector3(0f, focusY + h * 0.05f, h * 2.05f);
            Vector3 lookAt = new Vector3(0f, focusY, 0f);

            for (int i = 1; i <= 3; i++)
                Shoot(cam, camPos, lookAt, 30f, 720, 1080, $"var_{i}.png");

            // 同じカメラのまま、ポーズだけ元に戻す（対照実験）
            rig.ResetPose();
            Shoot(cam, camPos, lookAt, 30f, 720, 1080, "var_4_rest.png");

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log("[Ryona] var test done");
        }

        /// <summary>
        /// 変形後（ポーズ適用後）のメッシュで、股間の各点が肌と布のどちらが手前かを調べる。
        /// TポーズとIdleで比較して、ポーズが原因かを確定させる。
        /// </summary>
        [MenuItem("Ryona/Debug/Deform Map")]
        public static void DeformMap()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
            if (!File.Exists(vrmPath))
                return;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Vrm10Instance vrm = Vrm10
                .LoadPathAsync(vrmPath, canLoadVrm0X: true, showMeshes: true)
                .GetAwaiter().GetResult();

            var body = System.Array.Find(vrm.GetComponentsInChildren<SkinnedMeshRenderer>(), r => r.name == "Body");
            if (body == null)
                return;

            int skinSlot = -1, pantsSlot = -1;
            for (int i = 0; i < body.sharedMaterials.Length; i++)
            {
                if (body.sharedMaterials[i] == null) continue;
                string n = body.sharedMaterials[i].name.ToLowerInvariant();
                if (n.Contains("skin")) skinSlot = i;
                if (n.Contains("bottoms")) pantsSlot = i;
            }

            // 肌だけ／布だけのレンダラーを作る（同じボーンを共有するので同じ変形になる）
            SkinnedMeshRenderer MakeOnly(string name, int slot)
            {
                var go = new GameObject(name);
                go.transform.SetParent(body.transform.parent, false);
                go.transform.localPosition = body.transform.localPosition;
                go.transform.localRotation = body.transform.localRotation;
                go.transform.localScale = body.transform.localScale;

                var r = go.AddComponent<SkinnedMeshRenderer>();
                r.sharedMesh = ExtractSubMesh(body.sharedMesh, slot);
                r.bones = body.bones;
                r.rootBone = body.rootBone;
                return r;
            }

            var skin = MakeOnly("Only Skin", skinSlot);
            var pants = MakeOnly("Only Pants", pantsSlot);
            var flat = Shader.Find("Unlit/Color");
            skin.sharedMaterial = new Material(flat) { color = Color.green };
            pants.sharedMaterial = new Material(flat) { color = Color.red };

            var rig = vrm.gameObject.AddComponent<ProceduralRig>();
            rig.Capture();

            Vector3[][] Bake(SkinnedMeshRenderer r)
            {
                var m = new Mesh();
                r.BakeMesh(m, true);
                Vector3[] v = m.vertices;
                Matrix4x4 mat = r.transform.localToWorldMatrix;
                var tris = new Vector3[m.triangles.Length / 3][];
                int[] t = m.triangles;
                for (int i = 0; i < t.Length; i += 3)
                {
                    tris[i / 3] = new[]
                    {
                        mat.MultiplyPoint3x4(v[t[i]]),
                        mat.MultiplyPoint3x4(v[t[i + 1]]),
                        mat.MultiplyPoint3x4(v[t[i + 2]])
                    };
                }
                return tris;
            }

            static float Hit(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 e1 = b - a, e2 = c - a;
                Vector3 p = Vector3.Cross(d, e2);
                float det = Vector3.Dot(e1, p);
                if (Mathf.Abs(det) < 1e-9f) return -1f;
                float inv = 1f / det;
                Vector3 tv = o - a;
                float u = Vector3.Dot(tv, p) * inv;
                if (u < 0f || u > 1f) return -1f;
                Vector3 q = Vector3.Cross(tv, e1);
                float v = Vector3.Dot(d, q) * inv;
                if (v < 0f || u + v > 1f) return -1f;
                float t = Vector3.Dot(e2, q) * inv;
                return t > 0f ? t : -1f;
            }

            void Scan(string label, Vector3[][] s, Vector3[][] p)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"[Deform] --- {label} --- (S=肌が手前 / P=布が手前 / .=無し)");
                int sCount = 0;
                float worst = 0f;

                for (float y = 0.92f; y >= 0.74f; y -= 0.01f)
                {
                    var row = new System.Text.StringBuilder();
                    row.Append($"y={y:F2} ");
                    for (float x = -0.05f; x <= 0.051f; x += 0.01f)
                    {
                        Vector3 o = new Vector3(x, y, 3f);
                        Vector3 d = new Vector3(0f, 0f, -1f);
                        float bestS = float.MaxValue, bestP = float.MaxValue;

                        foreach (var tri in s)
                        {
                            float t = Hit(o, d, tri[0], tri[1], tri[2]);
                            if (t > 0f && t < bestS) bestS = t;
                        }
                        foreach (var tri in p)
                        {
                            float t = Hit(o, d, tri[0], tri[1], tri[2]);
                            if (t > 0f && t < bestP) bestP = t;
                        }

                        if (bestS == float.MaxValue && bestP == float.MaxValue) row.Append('.');
                        else if (bestS <= bestP)
                        {
                            row.Append('S');
                            if (bestP < float.MaxValue)
                            {
                                sCount++;
                                worst = Mathf.Max(worst, bestP - bestS);
                            }
                        }
                        else row.Append('P');
                    }
                    sb.AppendLine(row.ToString());
                }

                sb.AppendLine($"[Deform] {label}: 肌が手前の点 = {sCount}, 最大はみ出し = {worst * 1000f:F1}mm");
                Debug.Log(sb.ToString());
            }

            Scan("Tポーズ(素)", Bake(skin), Bake(pants));

            var anim = vrm.gameObject.AddComponent<CharacterAnimator>();
            anim.Rig = rig;
            anim.ForceState(CharacterAnimator.State.Idle, 0.5f);

            Scan("Idle(腕を下ろした)", Bake(skin), Bake(pants));

            Object.DestroyImmediate(vrm.gameObject);
            Debug.Log("[Ryona] deform map done");
        }

        /// <summary>
        /// 素のVRMを、毎レンダーで骨行列を再計算する設定にして撮る。
        /// これで股間が壊れるなら、原因は私のポーズではなく読み込んだ骨格の姿勢。
        /// </summary>
        [MenuItem("Ryona/Debug/Raw Forced")]
        public static void RawForced()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
            if (!File.Exists(vrmPath))
                return;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLights();

            Vrm10Instance vrm = Vrm10
                .LoadPathAsync(vrmPath, canLoadVrm0X: true, showMeshes: true)
                .GetAwaiter().GetResult();

            var smrs = vrm.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var s in smrs)
                s.forceMatrixRecalculationPerRender = true;

            var bounds = ComputeBounds(vrm.gameObject);
            float h = Mathf.Max(1f, bounds.size.y);
            float focusY = bounds.center.y;

            var camGo = new GameObject("RawF Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.30f, 0.31f, 0.34f);
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 50f;

            Shoot(cam, new Vector3(0f, focusY + h * 0.05f, h * 2.05f),
                new Vector3(0f, focusY, 0f), 30f, 720, 1080, "rawf_1.png");
            Shoot(cam, new Vector3(0f, focusY + h * 0.05f, h * 2.05f),
                new Vector3(0f, focusY, 0f), 30f, 720, 1080, "rawf_2.png");

            // 骨格の姿勢をログに残す
            var hips = System.Array.Find(vrm.GetComponentsInChildren<Transform>(true), t => t.name == "J_Bip_C_Hips");
            var thighL = System.Array.Find(vrm.GetComponentsInChildren<Transform>(true), t => t.name == "J_Bip_L_UpperLeg");
            if (hips != null)
                Debug.Log($"[RawF] hips.pos={hips.position} rot={hips.rotation.eulerAngles}");
            if (thighL != null)
                Debug.Log($"[RawF] thighL.rot={thighL.rotation.eulerAngles}");

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log("[Ryona] raw forced done");
        }

        /// <summary>ポーズ前後で脚のボーンが動いているかを数値で確認する。</summary>
        [MenuItem("Ryona/Debug/Leg Check")]
        public static void LegCheck()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
            if (!File.Exists(vrmPath))
                return;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Vrm10Instance vrm = Vrm10
                .LoadPathAsync(vrmPath, canLoadVrm0X: true, showMeshes: true)
                .GetAwaiter().GetResult();

            var rig = vrm.gameObject.AddComponent<ProceduralRig>();
            rig.Capture();

            void Log(string label)
            {
                Debug.Log($"[Leg] {label} hipsPos={rig.Hips.position} " +
                          $"thighL={rig.LUpperLeg.position} thighR={rig.RUpperLeg.position} " +
                          $"kneeL={rig.LLowerLeg.position} kneeR={rig.RLowerLeg.position} " +
                          $"footL={rig.LFoot.position} footR={rig.RFoot.position}");
                Debug.Log($"[Leg] {label} thighL.rot={rig.LUpperLeg.rotation.eulerAngles} " +
                          $"thighR.rot={rig.RUpperLeg.rotation.eulerAngles} " +
                          $"spinePos={rig.Spine.position} spineRot={rig.Spine.rotation.eulerAngles}");
            }

            Log("rest");

            var anim = vrm.gameObject.AddComponent<CharacterAnimator>();
            anim.Rig = rig;
            anim.ForceState(CharacterAnimator.State.Idle, 0.5f);

            Log("idle");

            // 腕だけ動かした場合
            rig.ResetPose();
            rig.SetArm(true, new Vector3(0.30f, -1f, 0.05f).normalized, new Vector3(0.10f, -1f, 0.18f).normalized);
            rig.SetArm(false, new Vector3(-0.30f, -1f, 0.05f).normalized, new Vector3(-0.10f, -1f, 0.18f).normalized);
            Log("arms only");

            // 腰だけ
            rig.ResetPose();
            rig.OffsetHips(new Vector3(0f, 0.006f, 0f));
            Log("hips only");

            // 体幹だけ
            rig.ResetPose();
            rig.BendTorso(0.8f, 0f, 0f);
            Log("torso only");

            Object.DestroyImmediate(vrm.gameObject);
            Debug.Log("[Ryona] leg check done");
        }

        /// <summary>股間付近の頂点が、どのボーンにどれだけ引っ張られているかを見る。</summary>
        [MenuItem("Ryona/Debug/Weight Check")]
        public static void WeightCheck()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
            if (!File.Exists(vrmPath))
                return;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Vrm10Instance vrm = Vrm10
                .LoadPathAsync(vrmPath, canLoadVrm0X: true, showMeshes: true)
                .GetAwaiter().GetResult();

            var body = System.Array.Find(vrm.GetComponentsInChildren<SkinnedMeshRenderer>(), r => r.name == "Body");
            if (body == null)
                return;

            Mesh mesh = body.sharedMesh;
            Vector3[] verts = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            Transform[] bones = body.bones;

            int skinSlot = -1, pantsSlot = -1;
            for (int i = 0; i < body.sharedMaterials.Length; i++)
            {
                if (body.sharedMaterials[i] == null) continue;
                string n = body.sharedMaterials[i].name.ToLowerInvariant();
                if (n.Contains("skin")) skinSlot = i;
                if (n.Contains("bottoms")) pantsSlot = i;
            }

            void Dump(string label, int slot)
            {
                var seen = new System.Collections.Generic.HashSet<int>();
                int printed = 0;
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"[Weight] --- {label} ---");

                foreach (int vi in mesh.GetTriangles(slot))
                {
                    if (!seen.Add(vi)) continue;

                    Vector3 p = verts[vi];
                    if (Mathf.Abs(p.x) > 0.035f || p.y < 0.74f || p.y > 0.90f)
                        continue;

                    BoneWeight bw = weights[vi];
                    var infl = new (string name, float w)[]
                    {
                        (bw.boneIndex0 < bones.Length ? bones[bw.boneIndex0].name : "?", bw.weight0),
                        (bw.boneIndex1 < bones.Length ? bones[bw.boneIndex1].name : "?", bw.weight1),
                        (bw.boneIndex2 < bones.Length ? bones[bw.boneIndex2].name : "?", bw.weight2),
                        (bw.boneIndex3 < bones.Length ? bones[bw.boneIndex3].name : "?", bw.weight3)
                    };

                    System.Array.Sort(infl, (a, b) => b.w.CompareTo(a.w));

                    var parts = new System.Text.StringBuilder();
                    for (int i = 0; i < 4 && infl[i].w > 0.001f; i++)
                        parts.Append($"{infl[i].name.TrimStart('J','_').Replace("Bip_","")}={infl[i].w:F2} ");

                    sb.AppendLine($"  v{vi} ({p.x:F3},{p.y:F3},{p.z:F3})  {parts}");

                    if (++printed >= 14)
                        break;
                }

                Debug.Log(sb.ToString());
            }

            Dump("肌(SKIN)", skinSlot);
            Dump("布(Bottoms)", pantsSlot);

            Object.DestroyImmediate(vrm.gameObject);
            Debug.Log("[Ryona] weight check done");
        }

        /// <summary>腕を下ろした状態で、股間に見えているのがどのサブメッシュかを色分けで確定する。</summary>
        [MenuItem("Ryona/Debug/Id Arms")]
        public static void IdArms()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
            if (!File.Exists(vrmPath))
                return;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLights();

            Vrm10Instance vrm = Vrm10
                .LoadPathAsync(vrmPath, canLoadVrm0X: true, showMeshes: true)
                .GetAwaiter().GetResult();

            var bounds = ComputeBounds(vrm.gameObject);
            float h = Mathf.Max(1f, bounds.size.y);
            float focusY = bounds.center.y;

            foreach (var s in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
                s.forceMatrixRecalculationPerRender = true;

            var body = System.Array.Find(vrm.GetComponentsInChildren<SkinnedMeshRenderer>(), r => r.name == "Body");

            var flat = Shader.Find("Unlit/Color");
            var mats = new Material[body.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                string n = body.sharedMaterials[i] != null ? body.sharedMaterials[i].name.ToLowerInvariant() : "";
                Color c = Color.white;
                if (n.Contains("skin")) c = Color.green;
                else if (n.Contains("bottoms")) c = Color.red;
                else if (n.Contains("tops")) c = Color.blue;
                else if (n.Contains("shoes")) c = Color.yellow;
                else if (n.Contains("hairback")) c = Color.magenta;
                Debug.Log($"[Id] slot {i} {n} -> {c}");
                mats[i] = new Material(flat) { color = c };
            }

            var camGo = new GameObject("Id Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.30f, 0.31f, 0.34f);
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 50f;

            Vector3 camPos = new Vector3(0f, focusY + h * 0.05f, h * 2.05f);
            Vector3 lookAt = new Vector3(0f, focusY, 0f);

            body.sharedMaterials = mats;
            Shoot(cam, camPos, lookAt, 30f, 720, 1080, "id_a_rest.png");

            var rig = vrm.gameObject.AddComponent<ProceduralRig>();
            rig.Capture();
            rig.SetArm(true, new Vector3(0.30f, -1f, 0.05f).normalized, new Vector3(0.10f, -1f, 0.18f).normalized);
            rig.SetArm(false, new Vector3(-0.30f, -1f, 0.05f).normalized, new Vector3(-0.10f, -1f, 0.18f).normalized);
            Shoot(cam, camPos, lookAt, 30f, 720, 1080, "id_b_arms.png");

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);
            Debug.Log("[Ryona] id arms done");
        }

    }
}


