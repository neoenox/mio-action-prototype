using System.IO;
using Ryona.Game;
using UniVRM10;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using AttackId = Ryona.Game.EnemyBrain.AttackId;

namespace Ryona.EditorTools
{
    /// <summary>
    /// VRM を実際に読み込んで、正面・斜め・顔の3枚を書き出す。
    /// 向き・スケール・ライティングの確認用。編集モードで動く。
    /// </summary>
    public static class CharacterPreview
    {
        const string VrmName = "mio_v1.vrm";

        [MenuItem("Ryona/Capture Character Preview")]
        public static void Capture()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);

            if (!File.Exists(vrmPath))
            {
                Debug.LogError($"[Ryona] VRM not found: {vrmPath}");
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            SetupLights();

            Vrm10Instance vrm;
            try
            {
                // 編集モードでは ImmediateCaller が使われるので同期的に完了する
                vrm = Vrm10.LoadPathAsync(vrmPath, canLoadVrm0X: true, showMeshes: true)
                    .GetAwaiter().GetResult();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Ryona] VRM load failed: {e}");
                return;
            }

            if (vrm == null)
            {
                Debug.LogError("[Ryona] VRM load returned null");
                return;
            }

            vrm.transform.position = Vector3.zero;
            vrm.transform.rotation = Quaternion.identity;

            // リグを組む（T ポーズ解消 + 手続きモーション）
            var rig = vrm.gameObject.AddComponent<ProceduralRig>();
            rig.Capture();
            var animator = vrm.gameObject.AddComponent<CharacterAnimator>();
            animator.Rig = rig;
            animator.ForceState(CharacterAnimator.State.Idle, 0.5f);

            foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
                smr.forceMatrixRecalculationPerRender = true;

            var bounds = ComputeBounds(vrm.gameObject);
            Debug.Log($"[Ryona] height={bounds.size.y:F3}m  center={bounds.center}  " +
                      $"footprint=({bounds.size.x:F3}, {bounds.size.z:F3})");

            var camGo = new GameObject("Preview Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.30f, 0.31f, 0.34f);
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 50f;

            float h = Mathf.Max(1f, bounds.size.y);
            float focusY = bounds.center.y;

            // 1) 正面(キャラの正面は +Z を向いている)
            Shoot(cam, new Vector3(0f, focusY + h * 0.05f, h * 2.05f),
                new Vector3(0f, focusY, 0f), 30f, 720, 1080, "preview_body.png");

            // 2) 斜め45度
            Shoot(cam, new Vector3(h * 0.95f, focusY + h * 0.12f, h * 1.75f),
                new Vector3(0f, focusY, 0f), 30f, 720, 1080, "preview_angle.png");

            // 3) 顔
            Shoot(cam, new Vector3(0f, h * 0.935f, h * 0.46f),
                new Vector3(0f, h * 0.925f, 0f), 26f, 900, 900, "preview_face.png");

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);
        }

        /// <summary>
        /// 「股間の破綻」の切り分け。
        /// カメラの near 値（z-fighting か）と、体メッシュ／服メッシュのどちらが
        /// 表に出ているかを、4枚の比較画像で判定する。
        /// </summary>
        [MenuItem("Ryona/Debug/Clip Test")]
        public static void ClipTest()
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

            var rig = vrm.gameObject.AddComponent<ProceduralRig>();
            rig.Capture();
            var animator = vrm.gameObject.AddComponent<CharacterAnimator>();
            animator.Rig = rig;
            animator.ForceState(CharacterAnimator.State.Idle, 0.5f);

            var renderers = vrm.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var smr in renderers)
                smr.forceMatrixRecalculationPerRender = true;

            // どのメッシュが何なのかを記録する
            foreach (var smr in renderers)
            {
                string mats = string.Join(", ",
                    System.Array.ConvertAll(smr.sharedMaterials,
                        m => m == null ? "null" : $"{m.name}({m.shader.name},q={m.renderQueue})"));
                Debug.Log($"[Mesh] {smr.name}  verts={smr.sharedMesh.vertexCount}  mats=[{mats}]");
            }

            var bounds = ComputeBounds(vrm.gameObject);
            float h = Mathf.Max(1f, bounds.size.y);
            float focusY = bounds.center.y;

            var camGo = new GameObject("Clip Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.30f, 0.31f, 0.34f);
            cam.farClipPlane = 50f;

            Vector3 camPos = new Vector3(0f, focusY + h * 0.05f, h * 2.05f);
            Vector3 lookAt = new Vector3(0f, focusY, 0f);

            // 1) 現状の near
            cam.nearClipPlane = 0.02f;
            Shoot(cam, camPos, lookAt, 30f, 720, 1080, "clip_a_near002.png");

            // 2) near を大きくする（z-fighting 説の検証）
            cam.nearClipPlane = 0.30f;
            Shoot(cam, camPos, lookAt, 30f, 720, 1080, "clip_b_near030.png");

            // 3) 体メッシュ（最大のメッシュ）を隠す
            cam.nearClipPlane = 0.02f;
            var body = renderers[0];
            int maxVerts = -1;
            foreach (var smr in renderers)
            {
                if (smr.sharedMesh != null && smr.sharedMesh.vertexCount > maxVerts)
                {
                    maxVerts = smr.sharedMesh.vertexCount;
                    body = smr;
                }
            }
            Debug.Log($"[Mesh] 最大メッシュ = {body.name} ({maxVerts} verts)");

            body.enabled = false;
            Shoot(cam, camPos, lookAt, 30f, 720, 1080, "clip_c_nobody.png");
            body.enabled = true;

            // 4) 最大メッシュ以外（服）を隠す
            foreach (var smr in renderers)
                smr.enabled = smr == body;
            Shoot(cam, camPos, lookAt, 30f, 720, 1080, "clip_d_bodyonly.png");
            foreach (var smr in renderers)
                smr.enabled = true;

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log("[Ryona] clip test done");
        }

        /// <summary>
        /// どのサブメッシュ（肌／パンツ／上衣／靴）が表に出ているかを色で特定する。
        /// マテリアルを Unlit の単色に置き換えて、股間を接写する。
        /// </summary>
        [MenuItem("Ryona/Debug/Material Id Test")]
        public static void MaterialIdTest()
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

            var rig = vrm.gameObject.AddComponent<ProceduralRig>();
            rig.Capture();
            var animator = vrm.gameObject.AddComponent<CharacterAnimator>();
            animator.Rig = rig;
            animator.ForceState(CharacterAnimator.State.Idle, 0.5f);

            var unlit = Shader.Find("Unlit/Color");
            if (unlit == null)
                unlit = Shader.Find("Sprites/Default");

            foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.forceMatrixRecalculationPerRender = true;

                if (smr.name != "Body")
                    continue;

                var mats = smr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i] != null ? mats[i].name : "";
                    Color c;
                    string label;

                    if (n.Contains("Bottoms")) { c = Color.green; label = "パンツ"; }
                    else if (n.Contains("Tops")) { c = Color.blue; label = "上衣"; }
                    else if (n.Contains("Shoes")) { c = Color.yellow; label = "靴"; }
                    else if (n.Contains("HairBack")) { c = new Color(0.4f, 0.25f, 0.1f); label = "後ろ髪"; }
                    else { c = Color.magenta; label = "肌"; }

                    var m = new Material(unlit) { color = c };
                    mats[i] = m;
                    Debug.Log($"[Id] slot{i} {n} -> {label}");
                }

                smr.sharedMaterials = mats;
            }

            var camGo = new GameObject("Id Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.10f, 0.12f);
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 20f;
            cam.fieldOfView = 26f;

            // 腰まわりを接写
            cam.transform.position = new Vector3(0f, 0.90f, 1.35f);
            cam.transform.LookAt(new Vector3(0f, 0.86f, 0f));

            Shoot(cam, cam.transform.position, new Vector3(0f, 0.86f, 0f), 26f, 900, 900, "id_hips.png");

            cam.transform.position = new Vector3(0f, 0.90f, 0.65f);
            cam.transform.LookAt(new Vector3(0f, 0.87f, 0f));
            Shoot(cam, cam.transform.position, new Vector3(0f, 0.87f, 0f), 26f, 900, 900, "id_hips_close.png");

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log("[Ryona] material id test done");
        }

        /// <summary>
        /// 股間で肌がパンツより前に出ていないかを数値で判定する。
        /// 再エクスポートのたびに実行して、破綻が直ったかを目視なしで確認できる。
        /// </summary>
        [MenuItem("Ryona/Debug/Crotch Check")]
        public static void CrotchCheck()
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
            Matrix4x4 toWorld = body.transform.localToWorldMatrix;

            int skinSlot = -1, pantsSlot = -1;
            for (int i = 0; i < body.sharedMaterials.Length; i++)
            {
                if (body.sharedMaterials[i] == null)
                    continue;
                string n = body.sharedMaterials[i].name.ToLowerInvariant();
                if (n.Contains("skin")) skinSlot = i;
                if (n.Contains("bottoms")) pantsSlot = i;
            }

            if (skinSlot < 0 || pantsSlot < 0)
            {
                Debug.LogError("[Ryona] skin / bottoms slot not found");
                return;
            }

            Vector3[] World(int sub)
            {
                int[] t = mesh.GetTriangles(sub);
                var used = new System.Collections.Generic.HashSet<int>();
                foreach (int i in t)
                    used.Add(i);

                var list = new System.Collections.Generic.List<Vector3>(used.Count);
                foreach (int i in used)
                    list.Add(toWorld.MultiplyPoint3x4(verts[i]));
                return list.ToArray();
            }

            Vector3[] skinV = World(skinSlot);
            Vector3[] pantsV = World(pantsSlot);

            int total = 0, inFront = 0;
            float worst = 0f;

            foreach (var s in skinV)
            {
                if (Mathf.Abs(s.x) > 0.06f || s.y < 0.72f || s.y > 0.95f)
                    continue;

                float best = float.MaxValue;
                Vector3 bp = default;
                foreach (var p in pantsV)
                {
                    float d = (p.x - s.x) * (p.x - s.x) + (p.y - s.y) * (p.y - s.y);
                    if (d < best) { best = d; bp = p; }
                }

                float dz = s.z - bp.z;
                total++;
                if (dz > 0.0005f)
                {
                    inFront++;
                    if (dz > worst) worst = dz;
                }
            }

            Debug.Log($"[Crotch] 肌頂点={total}  パンツより手前={inFront}  最大はみ出し={worst * 1000f:F1}mm");

            Object.DestroyImmediate(vrm.gameObject);
            Debug.Log("[Ryona] crotch check done");
        }

        /// <summary>
        /// パンツのサブメッシュだけを抜き出して描画する。
        /// 股間が「布で塞がっていない」のかを、透過や深度に頼らずに見る。
        /// </summary>
        [MenuItem("Ryona/Debug/Pants Only")]
        public static void PantsOnly()
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

            int pantsSlot = -1;
            for (int i = 0; i < body.sharedMaterials.Length; i++)
            {
                if (body.sharedMaterials[i] != null && body.sharedMaterials[i].name.Contains("Bottoms"))
                    pantsSlot = i;
            }

            if (pantsSlot < 0)
            {
                Debug.LogError("[Ryona] Bottoms slot not found");
                return;
            }

            Mesh pants = ExtractSubMesh(body.sharedMesh, pantsSlot);

            var go = new GameObject("Pants Only");
            go.transform.SetParent(body.transform.parent, false);
            go.transform.localPosition = body.transform.localPosition;
            go.transform.localRotation = body.transform.localRotation;
            go.transform.localScale = body.transform.localScale;

            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = pants;
            smr.bones = body.bones;
            smr.rootBone = body.rootBone;
            smr.forceMatrixRecalculationPerRender = true;

            var mat = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.15f, 0.75f, 0.25f) };
            smr.sharedMaterial = mat;

            body.enabled = false;

            var camGo = new GameObject("Pants Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.10f, 0.12f);
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 26f;

            Vector3 camPos = new Vector3(0f, 0.90f, 0.62f);
            Vector3 lookAt = new Vector3(0f, 0.87f, 0f);
            Shoot(cam, camPos, lookAt, 26f, 900, 900, "pants_only.png");

            // 横からも
            Vector3 camPos2 = new Vector3(0.55f, 0.88f, 0.35f);
            Shoot(cam, camPos2, lookAt, 30f, 900, 900, "pants_only_side.png");

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log("[Ryona] pants only done");
        }

        static Mesh ExtractSubMesh(Mesh src, int sub)
        {
            int[] tris = src.GetTriangles(sub);
            Vector3[] vs = src.vertices;
            Vector3[] ns = src.normals;
            Vector2[] uvs = src.uv;
            BoneWeight[] ws = src.boneWeights;

            var map = new System.Collections.Generic.Dictionary<int, int>();
            var nv = new System.Collections.Generic.List<Vector3>();
            var nn = new System.Collections.Generic.List<Vector3>();
            var nu = new System.Collections.Generic.List<Vector2>();
            var nw = new System.Collections.Generic.List<BoneWeight>();
            var nt = new int[tris.Length];

            for (int i = 0; i < tris.Length; i++)
            {
                int idx = tris[i];
                if (!map.TryGetValue(idx, out int ni))
                {
                    ni = nv.Count;
                    map[idx] = ni;
                    nv.Add(vs[idx]);
                    nn.Add(ns.Length > idx ? ns[idx] : Vector3.up);
                    nu.Add(uvs.Length > idx ? uvs[idx] : Vector2.zero);
                    nw.Add(ws.Length > idx ? ws[idx] : default);
                }
                nt[i] = ni;
            }

            var m = new Mesh();
            m.SetVertices(nv);
            m.SetNormals(nn);
            m.SetUVs(0, nu);
            m.boneWeights = nw.ToArray();
            m.bindposes = src.bindposes;
            m.triangles = nt;
            m.RecalculateBounds();
            return m;
        }

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

        /// <summary>
        /// 攻撃1種の「振りかぶり→振り抜き」をコマ送りで並べる。
        /// 拳が頭や体を貫いていないか、振りの軌道が読めるかを見る。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Heavy Swing")]
        public static void CaptureHeavySwing()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLights();

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = Vector3.one * 6f;

            var figureGo = new GameObject("Enemy Figure");
            var figure = figureGo.AddComponent<EnemyFigure>();

            var camGo = new GameObject("Enemy Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.fieldOfView = 38f;
            cam.nearClipPlane = 0.05f;

            const float h = 1.75f;
            cam.transform.position = new Vector3(h * 2.1f, h * 0.55f, 0f);
            cam.transform.LookAt(new Vector3(0f, h * 0.55f, 0f));

            // 溜め 4 コマ + 振り抜き 5 コマ
            var shots = new (EnemyPhase phase, float windup, float swing, string label)[]
            {
                (EnemyPhase.Windup, 0f, 0f, "0 構え"),
                (EnemyPhase.Windup, 0.30f, 0f, "1 引き中"),
                (EnemyPhase.Windup, 0.55f, 0f, "2 引き切り"),
                (EnemyPhase.Windup, 1.00f, 0f, "3 溜め終わり"),
                (EnemyPhase.Recover, 0f, 0.00f, "4 振り始め"),
                (EnemyPhase.Recover, 0f, 0.018f, "5 振り中"),
                (EnemyPhase.Recover, 0f, 0.035f, "6 半ば"),
                (EnemyPhase.Recover, 0f, 0.052f, "7 ほぼ伸び"),
                (EnemyPhase.Recover, 0f, 0.070f, "8 伸び切り"),
            };

            const int cellW = 320;
            const int cellH = 560;
            var sheet = new Texture2D(cellW * shots.Length, cellH, TextureFormat.RGB24, false);

            for (int i = 0; i < shots.Length; i++)
            {
                var s = shots[i];
                figure.Preview(s.phase, s.windup, AttackId.Heavy, 0f, s.swing);

                var cell = RenderCell(cam, cellW, cellH);
                sheet.SetPixels(i * cellW, 0, cellW, cellH, cell.GetPixels());
                Object.DestroyImmediate(cell);
            }
            sheet.Apply();

            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../heavy_swing.png"));
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Debug.Log($"[Ryona] heavy swing: {path}");
        }

        /// <summary>
        /// 効果音が無音になっていないか、狙った音色かを数値で確かめる。
        /// 明るさ＝ゼロ交差率。値が大きいほど高い音。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Scan Sfx")]
        public static void ScanSfx()
        {
            var go = new GameObject("SfxTest");
            var sfx = go.AddComponent<ProceduralSfx>();

            foreach (SfxId id in System.Enum.GetValues(typeof(SfxId)))
            {
                if (!sfx.TryMeasure(id, out float seconds, out float peak, out float brightness, out float rms))
                {
                    Debug.LogError($"[Sfx] {id} を測れない");
                    continue;
                }

                // 実際に鳴る大きさ＝RMS × 基準音量 × MasterVolume
                float level = rms * ProceduralSfx.Gain(id) * sfx.MasterVolume;
                float dbfs = level > 1e-6f ? 20f * Mathf.Log10(level) : -120f;

                Debug.Log($"[Sfx] {id} 長さ={seconds:F3}s ピーク={peak:F3} 明るさ={brightness:F3} " +
                    $"RMS={rms:F3} 基準={ProceduralSfx.Gain(id):F2} 実効={level:F3} ({dbfs:F1} dBFS)");
            }

            // 重ね合わせ。同時に鳴ったときに 1.0 を超えると音割れするので、そこだけ確かめる
            var combos = new (string Name, SfxId A, float VolA, SfxId B, float VolB)[]
            {
                ("被弾 Pipe+Stagger", SfxId.Pipe, 1.00f, SfxId.Stagger, 0.70f),
                ("ガード Guard+Stagger", SfxId.Guard, 0.90f, SfxId.Stagger, 0.50f),
                ("決着 Break+BodyDrop", SfxId.Break, 1.00f, SfxId.BodyDrop, 1.00f),
            };

            foreach (var c in combos)
            {
                if (!sfx.TryMeasureMix(c.A, c.VolA, c.B, c.VolB, out float peak, out float rms))
                {
                    Debug.LogError($"[Sfx] 重ね {c.Name} を測れない");
                    continue;
                }

                float dbfs = peak > 1e-6f ? 20f * Mathf.Log10(peak) : -120f;
                string clip = peak >= 1f ? "あり" : "なし";
                Debug.Log($"[Sfx] 重ね {c.Name} ピーク={peak:F3} ({dbfs:F1} dBFS) " +
                    $"RMS={rms:F3} 音割れ={clip}");
            }

            Object.DestroyImmediate(go);
        }

        static float StanceSlip(Vector3 now, Vector3 prev, float dt, ref int count, ref float max)
        {
            // 接地している（足が低い位置にある）ときだけ測る
            if (now.y > 0.05f)
                return 0f;

            float slip = new Vector2(now.x - prev.x, now.z - prev.z).magnitude / dt;
            count++;
            max = Mathf.Max(max, slip);
            return slip;
        }

        /// <summary>
        /// 攻撃の予備動作が「いつ見え始めるか」を拳の移動で測る。
        /// 25% 到達が遅い＝予備動作が見え始めるのが遅い、ということ。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Scan Attack Tell")]
        public static void ScanAttackTell()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("TellTest");
            var fig = go.AddComponent<EnemyFigure>();

            // まず1回組ませて拳を取る
            fig.Preview(EnemyPhase.Windup, 0f, AttackId.Jab, 0f, 0f);

            Transform fist = fig.Fist(false);
            if (fist == null)
            {
                Debug.LogError("[Tell] 拳の Transform が取れない");
                Object.DestroyImmediate(go);
                return;
            }

            AttackId[] ids = { AttackId.Jab, AttackId.Heavy, AttackId.Break };
            string[] names = { "Jab", "Heavy", "Break" };
            float[] durations = { 0.35f, 0.75f, 0.55f };

            const int steps = 100;

            for (int a = 0; a < ids.Length; a++)
            {
                var pos = new Vector3[steps + 1];
                for (int i = 0; i <= steps; i++)
                {
                    fig.Preview(EnemyPhase.Windup, i / (float)steps, ids[a], 0f, 0f);
                    pos[i] = fist.position - go.transform.position;
                }

                float total = 0f;
                for (int i = 1; i <= steps; i++)
                    total += Vector3.Distance(pos[i - 1], pos[i]);

                float t25 = -1f, t50 = -1f, t90 = -1f, acc = 0f;
                for (int i = 1; i <= steps; i++)
                {
                    acc += Vector3.Distance(pos[i - 1], pos[i]);
                    float f = total > 1e-6f ? acc / total : 0f;
                    float t = i / (float)steps;
                    if (t25 < 0f && f >= 0.25f) t25 = t;
                    if (t50 < 0f && f >= 0.5f) t50 = t;
                    if (t90 < 0f && f >= 0.9f) t90 = t;
                }

                // 引き切った姿勢（最終姿勢から 2cm 以内）に入った時刻
                float hold = 1f;
                for (int i = steps; i >= 0; i--)
                {
                    if (Vector3.Distance(pos[i], pos[steps]) > 0.02f)
                        break;
                    hold = i / (float)steps;
                }

                float dur = durations[a];
                Debug.Log($"[Tell] {names[a]} 溜め={dur:F2}s 拳の移動={total:F3}m " +
                          $"25%={t25 * dur:F2}s({t25:P0}) 50%={t50 * dur:F2}s 90%={t90 * dur:F2}s " +
                          $"引き切り保持={(1f - hold) * dur:F2}s");
            }

            // 拳が体の前後どちらにあるか（z が + で前）。振りかぶりで後ろ、振り抜きで前に出ているか
            for (int a = 0; a < ids.Length; a++)
            {
                fig.Preview(EnemyPhase.Windup, 0f, ids[a], 0f, 0f);
                Vector3 rest = go.transform.InverseTransformPoint(fist.position);

                fig.Preview(EnemyPhase.Windup, 1f, ids[a], 0f, 0f);
                Vector3 cock = go.transform.InverseTransformPoint(fist.position);

                fig.Preview(EnemyPhase.Recover, 0f, ids[a], 0f, 0.10f);
                Vector3 hit = go.transform.InverseTransformPoint(fist.position);

                Debug.Log($"[Punch] {names[a]} 構え z={rest.z:F2} 振りかぶり z={cock.z:F2} 振り抜き z={hit.z:F2} " +
                          $"前後の振り={hit.z - cock.z:F2}m 打点の高さ={hit.y:F2}m 打点の横={hit.x:F2}m");
            }

            Object.DestroyImmediate(go);
        }

        /// <summary>
        /// 歩き1周期を横から並べる。足が地面に着いているか、大股になっていないかを見る。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Walk Cycle")]
        public static void CaptureWalkCycle()
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

            var camGo = new GameObject("Walk Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.05f;

            float h = 1.75f;
            cam.transform.position = new Vector3(h * 1.9f, h * 0.50f, 0f);
            cam.transform.LookAt(new Vector3(0f, h * 0.50f, 0f));

            float[] phases = { 0f, 0.15f, 0.31f, 0.46f, 0.62f, 0.77f, 0.92f };
            const int cellW = 300;
            const int cellH = 520;
            var sheet = new Texture2D(cellW * phases.Length, cellH * 2, TextureFormat.RGB24, false);

            for (int row = 0; row < 2; row++)
            {
                // 0 = 前進（真横から見る）、1 = 真横歩き（正面から見る）
                Vector2 dir = row == 0 ? new Vector2(0f, 1f) : new Vector2(1f, 0f);

                if (row == 0)
                {
                    cam.transform.position = new Vector3(h * 1.9f, h * 0.50f, 0f);
                    cam.transform.LookAt(new Vector3(0f, h * 0.50f, 0f));
                }
                else
                {
                    cam.transform.position = new Vector3(0f, h * 0.50f, h * 1.9f);
                    cam.transform.LookAt(new Vector3(0f, h * 0.50f, 0f));
                }

                for (int i = 0; i < phases.Length; i++)
                {
                    figure.PreviewWalkAt(phases[i], 1.6f, dir);
                    var cell = RenderCell(cam, cellW, cellH);

                    int y = row == 0 ? cellH : 0;
                    sheet.SetPixels(i * cellW, y, cellW, cellH, cell.GetPixels());
                    Object.DestroyImmediate(cell);
                }
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "walk_cycle.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(figureGo);
            Object.DestroyImmediate(floor);

            Debug.Log($"[Ryona] walk cycle: {outPath}");
        }

        /// <summary>
        /// 敵の立ち上がり（ダウン → 構え）を 7 コマで並べる。
        /// 倒れた姿勢から構えまで、腰の高さと上体が連続して上がるかを見る。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Enemy GetUp")]
        public static void CaptureEnemyGetUp()
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

            var camGo = new GameObject("GetUp Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.fieldOfView = 38f;
            cam.nearClipPlane = 0.05f;

            // 倒れた体は奥行き 1.7m になるので、少し引いて真横から見る
            float h = 1.75f;
            cam.transform.position = new Vector3(h * 2.3f, h * 0.42f, 0f);
            cam.transform.LookAt(new Vector3(0f, h * 0.34f, 0f));

            float[] rises = { 0f, 0.16f, 0.32f, 0.50f, 0.70f, 0.85f, 1.00f };
            const int cellW = 300;
            const int cellH = 520;
            var sheet = new Texture2D(cellW * rises.Length, cellH, TextureFormat.RGB24, false);

            for (int i = 0; i < rises.Length; i++)
            {
                figure.PreviewRise(rises[i]);
                var cell = RenderCell(cam, cellW, cellH);
                sheet.SetPixels(i * cellW, 0, cellW, cellH, cell.GetPixels());
                Object.DestroyImmediate(cell);

                Transform hips = figure.HipsBone;
                Debug.Log($"[GetUp] rise={rises[i]:F2} hipsY={hips.localPosition.y:F3} " +
                          $"fall={figure.FallAngle:F1} chest={figure.ChestBone.localEulerAngles.x:F1}");
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "enemy_getup.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(figureGo);
            Object.DestroyImmediate(floor);

            Debug.Log($"[Ryona] enemy getup: {outPath}");
        }

        /// <summary>
        /// 敵が倒れる瞬間（立っている → Dead）を 8 コマで並べる。
        /// 板のように倒れていないか、膝が抜けてから崩れているかを見る。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Enemy Fall")]
        public static void CaptureEnemyFall()
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

            var camGo = new GameObject("Fall Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.fieldOfView = 38f;
            cam.nearClipPlane = 0.05f;

            float h = 1.75f;
            cam.transform.position = new Vector3(h * 2.3f, h * 0.42f, 0f);
            cam.transform.LookAt(new Vector3(0f, h * 0.34f, 0f));

            float[] times = { 0f, 0.08f, 0.16f, 0.24f, 0.36f, 0.50f, 0.70f, 1.00f };
            const int cellW = 300;
            const int cellH = 520;
            var sheet = new Texture2D(cellW * times.Length, cellH, TextureFormat.RGB24, false);

            for (int i = 0; i < times.Length; i++)
            {
                figure.PreviewDead(times[i]);
                var cell = RenderCell(cam, cellW, cellH);
                sheet.SetPixels(i * cellW, 0, cellW, cellH, cell.GetPixels());
                Object.DestroyImmediate(cell);

                Transform hips = figure.HipsBone;
                Debug.Log($"[Fall] t={times[i]:F2} fall={figure.FallAngle:F1} " +
                          $"hipsY={hips.localPosition.y:F3} chestX={figure.ChestBone.localEulerAngles.x:F1} " +
                          $"headWY={figure.HeadBone.position.y:F2} chestWY={figure.ChestBone.position.y:F2} " +
                          $"ankleWY={figure.AnkleBone(false).position.y:F2} kneeWY={figure.KneeBone(false).position.y:F2}");
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "enemy_fall.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(figureGo);
            Object.DestroyImmediate(floor);

            Debug.Log($"[Ryona] enemy fall: {outPath}");
        }

        /// <summary>
        /// 彼女の歩き 1 周期を 3 方向（前進・右への横歩き・後退り）で並べる。
        /// 足が進行方向へ運ばれているか、横歩きで足が交差していないかを見る。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Player Walk Cycle")]
        public static void CapturePlayerWalkCycle()
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

            var camGo = new GameObject("Walk Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.05f;

            float[] phases = { 0f, 0.15f, 0.31f, 0.46f, 0.62f, 0.77f, 0.92f };
            const int cellW = 300;
            const int cellH = 560;

            // 0 = 前進（真横から）、1 = 右への横歩き（正面から）、2 = 後退り（真横から）
            var dirs = new Vector2[]
            {
                new Vector2(0f, 1f),
                new Vector2(1f, 0f),
                new Vector2(0f, -1f),
            };
            var names = new[] { "前進", "横歩き", "後退り" };

            var sheet = new Texture2D(cellW * phases.Length, cellH * dirs.Length, TextureFormat.RGB24, false);

            for (int row = 0; row < dirs.Length; row++)
            {
                CharacterAnimator.WalkDirectionOverride = dirs[row];

                // 横歩きだけ正面から見る（足が横へ出るのが分かる）
                if (row == 1)
                {
                    cam.transform.position = new Vector3(0f, h * 0.52f, h * 2.0f);
                    cam.transform.LookAt(new Vector3(0f, h * 0.50f, 0f));
                }
                else
                {
                    cam.transform.position = new Vector3(h * 2.0f, h * 0.52f, 0f);
                    cam.transform.LookAt(new Vector3(0f, h * 0.50f, 0f));
                }

                for (int i = 0; i < phases.Length; i++)
                {
                    anim.ForceState(CharacterAnimator.State.Walk, phases[i], 1f, StaggerTier.None, false);
                    var cell = RenderCell(cam, cellW, cellH);

                    int y = (dirs.Length - 1 - row) * cellH;
                    sheet.SetPixels(i * cellW, y, cellW, cellH, cell.GetPixels());
                    Object.DestroyImmediate(cell);
                }
            }

            CharacterAnimator.WalkDirectionOverride = null;
            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "player_walk_cycle.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(floor);

            Debug.Log($"[Ryona] player walk cycle: {outPath}（{names[0]}/{names[1]}/{names[2]} × {phases.Length} コマ）");
        }

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

        /// <summary>
        /// 彼女の「どこから殴られたか」別の崩れ方と、疲労・振りほどき後の突き放しを 1 枚で見る。
        /// 上から 4 行 = 正面 / 右 / 左 / 背中から。5 行目 = 疲労、6 行目 = 突き放し。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Hurt Sheet")]
        public static void CaptureHurtSheet()
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

            var rig = vrm.gameObject.AddComponent<ProceduralRig>();
            rig.Capture();

            var anim = vrm.gameObject.AddComponent<CharacterAnimator>();
            anim.Rig = rig;

            foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
                smr.forceMatrixRecalculationPerRender = true;

            var bounds = ComputeBounds(vrm.gameObject);
            float h = Mathf.Max(1f, bounds.size.y);

            var camGo = new GameObject("Hurt Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.fieldOfView = 38f;
            cam.nearClipPlane = 0.05f;

            cam.transform.position = new Vector3(h * 0.30f, h * 0.52f, h * 2.10f);
            cam.transform.LookAt(new Vector3(0f, h * 0.48f, 0f));

            int[] sides = { 0, 1, -1, 2 };
            string[] sideNames = { "正面", "右から", "左から", "背中から" };
            float[] times = { 0.02f, 0.08f, 0.16f, 0.26f };

            const int cellW = 300;
            const int cellH = 620;
            const int rows = 7;

            var sheet = new Texture2D(cellW * times.Length, cellH * rows, TextureFormat.RGB24, false);

            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < times.Length; col++)
                {
                    anim.PreviewTired(0f);
                    anim.PreviewShove(0f);
                    anim.PreviewHurtSide(sides[row]);
                    anim.ForceState(CharacterAnimator.State.Stagger, times[col], 0.30f, StaggerTier.Large);

                    var cell = RenderCell(cam, cellW, cellH);
                    sheet.SetPixels(col * cellW, (rows - 1 - row) * cellH, cellW, cellH, cell.GetPixels());
                    Object.DestroyImmediate(cell);
                }

                Debug.Log($"[Hurt] {sideNames[row]} を撮影");
            }

            // 5 行目: 疲労（スタミナが減ったときの構え）
            float[] tiredLevels = { 0f, 0.4f, 0.7f, 1f };
            for (int col = 0; col < tiredLevels.Length; col++)
            {
                anim.PreviewShove(0f);
                anim.PreviewTired(tiredLevels[col]);
                anim.ForceState(CharacterAnimator.State.Idle, 0.5f, 2f, StaggerTier.None);

                var cell = RenderCell(cam, cellW, cellH);
                sheet.SetPixels(col * cellW, (rows - 1 - 4) * cellH, cellW, cellH, cell.GetPixels());
                Object.DestroyImmediate(cell);
            }

            // 6 行目: 振りほどいた直後の突き放し（0.28s から減衰）
            float[] shoveTimes = { 0.28f, 0.18f, 0.09f, 0f };
            for (int col = 0; col < shoveTimes.Length; col++)
            {
                anim.PreviewTired(0f);
                anim.PreviewShove(shoveTimes[col]);
                anim.ForceState(CharacterAnimator.State.Idle, 0.5f, 2f, StaggerTier.None);

                var cell = RenderCell(cam, cellW, cellH);
                sheet.SetPixels(col * cellW, (rows - 1 - 5) * cellH, cellW, cellH, cell.GetPixels());
                Object.DestroyImmediate(cell);
            }

            anim.PreviewShove(0f);
            anim.PreviewTired(0f);
            anim.PreviewHurtSide(0);

            // 7 行目: 正面からの一撃を真横から。のけぞりが首の限界を超えていないか、
            // 上体が「反り橋」になっていないかを見る（正面からだと分からない）。
            cam.transform.position = new Vector3(h * 2.10f, h * 0.52f, 0f);
            cam.transform.LookAt(new Vector3(0f, h * 0.48f, 0f));
            for (int col = 0; col < times.Length; col++)
            {
                anim.PreviewTired(0f);
                anim.PreviewShove(0f);
                anim.PreviewHurtSide(0);
                anim.ForceState(CharacterAnimator.State.Stagger, times[col], 0.30f, StaggerTier.Large);

                var cell = RenderCell(cam, cellW, cellH);
                sheet.SetPixels(col * cellW, 0, cellW, cellH, cell.GetPixels());
                Object.DestroyImmediate(cell);

                Debug.Log($"[Hurt] side t={times[col]:F2} torso={anim.DebugTorsoPitch:F1} " +
                          $"head={anim.DebugHeadPitch:F1}");
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "hurt_sheet.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(floor);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] hurt sheet: {outPath}");
        }

        /// <summary>
        /// 食らいの 3 段階（弱・中・強）を真横から並べる。
        /// 「弱い一撃の方が激しく揺れる」ような逆転が無いか、
        /// 段ごとに崩れ方が違って見えるかを見る。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Hurt Tiers")]
        public static void CaptureHurtTiers()
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

            var rig = vrm.gameObject.AddComponent<ProceduralRig>();
            rig.Capture();

            var anim = vrm.gameObject.AddComponent<CharacterAnimator>();
            anim.Rig = rig;

            foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
                smr.forceMatrixRecalculationPerRender = true;

            var bounds = ComputeBounds(vrm.gameObject);
            float h = Mathf.Max(1f, bounds.size.y);

            var camGo = new GameObject("Tier Camera");
            // 顔ヒットの分岐は Camera.main を「弾かれる先」として使う。タグを付けないと
            // Camera.main が null になり、首が横を向かない（実機と別物になる）。
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.fieldOfView = 38f;
            cam.nearClipPlane = 0.05f;
            cam.transform.position = new Vector3(h * 2.10f, h * 0.52f, 0f);
            cam.transform.LookAt(new Vector3(0f, h * 0.48f, 0f));

            // 段（弱・中・強）× 殴られた場所（顔・腹）。最後の 2 行は横から殴られた場合で、
            // 「殴られた方向への 20° のねじれ」が顔ヒットでも残ることを見る。
            var rows = new (string Label, StaggerTier Tier, PunchZone Zone, int Side, float Duration)[]
            {
                ("弱・顔", StaggerTier.Small, PunchZone.Face, 0, 0.22f),
                ("弱・腹", StaggerTier.Small, PunchZone.Body, 0, 0.22f),
                ("中・顔", StaggerTier.Medium, PunchZone.Face, 0, 0.45f),
                ("中・腹", StaggerTier.Medium, PunchZone.Body, 0, 0.45f),
                ("強・顔", StaggerTier.Large, PunchZone.Face, 0, 0.80f),
                ("強・腹", StaggerTier.Large, PunchZone.Body, 0, 0.80f),
                ("中・顔・横", StaggerTier.Medium, PunchZone.Face, 1, 0.45f),
                ("中・腹・横", StaggerTier.Medium, PunchZone.Body, 1, 0.45f)
            };

            float[] times = { 0.02f, 0.08f, 0.16f, 0.30f };

            const int cellW = 300;
            const int cellH = 620;

            var sheet = new Texture2D(cellW * times.Length, cellH * rows.Length,
                TextureFormat.RGB24, false);

            for (int row = 0; row < rows.Length; row++)
            {
                for (int col = 0; col < times.Length; col++)
                {
                    anim.PreviewTired(0f);
                    anim.PreviewShove(0f);
                    anim.PreviewHurtSide(rows[row].Side);
                    anim.PreviewHitZone = rows[row].Zone;
                    anim.ForceState(CharacterAnimator.State.Stagger, times[col],
                        rows[row].Duration, rows[row].Tier);

                    var cell = RenderCell(cam, cellW, cellH);
                    sheet.SetPixels(col * cellW, (rows.Length - 1 - row) * cellH,
                        cellW, cellH, cell.GetPixels());
                    Object.DestroyImmediate(cell);

                    Debug.Log($"[Tier] {rows[row].Label} t={times[col]:F2} " +
                              $"torsoP={anim.DebugTorsoPitch:F1} torsoY={anim.DebugTorsoYaw:F1} " +
                              $"headP={anim.DebugHeadPitch:F1} headY={anim.DebugHeadYaw:F1}");
                }
            }

            anim.PreviewHitZone = null;

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "hurt_tiers.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(floor);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] hurt tiers: {outPath}");
        }

        /// <summary>
        /// 掴まれて もがいている姿勢を、正面と真横から 5 コマずつ並べる。
        /// 引かれて上体が反るか、両手が相手の腕に届くか、踏ん張りが効いているかを見る。
        /// 2 列目・4 列目はレバガチャの脈を 1（肘が開いて震える）にした瞬間。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Grabbed")]
        public static void CaptureGrabbed()
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

            var rig = vrm.gameObject.AddComponent<ProceduralRig>();
            rig.Capture();

            var anim = vrm.gameObject.AddComponent<CharacterAnimator>();
            anim.Rig = rig;

            foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
                smr.forceMatrixRecalculationPerRender = true;

            var bounds = ComputeBounds(vrm.gameObject);
            float h = Mathf.Max(1f, bounds.size.y);

            var camGo = new GameObject("Grabbed Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.fieldOfView = 38f;
            cam.nearClipPlane = 0.05f;

            float[] times = { 0.15f, 0.50f, 0.90f, 1.30f, 1.70f };
            float[] pulses = { 0f, 1f, 0.6f, 1f, 0f };
            var viewNames = new[] { "正面", "真横" };

            const int cellW = 320;
            const int cellH = 620;

            var sheet = new Texture2D(cellW * times.Length, cellH * 2, TextureFormat.RGB24, false);

            for (int row = 0; row < 2; row++)
            {
                if (row == 0)
                {
                    cam.transform.position = new Vector3(h * 0.30f, h * 0.55f, h * 2.20f);
                    cam.transform.LookAt(new Vector3(0f, h * 0.50f, 0f));
                }
                else
                {
                    cam.transform.position = new Vector3(h * 2.20f, h * 0.55f, 0f);
                    cam.transform.LookAt(new Vector3(0f, h * 0.50f, 0f));
                }

                for (int col = 0; col < times.Length; col++)
                {
                    anim.StrugglePulseOverride = pulses[col];
                    anim.ForceState(CharacterAnimator.State.Grabbed, times[col], 3f,
                        StaggerTier.None, false);

                    var cell = RenderCell(cam, cellW, cellH);
                    sheet.SetPixels(col * cellW, (1 - row) * cellH, cellW, cellH, cell.GetPixels());
                    Object.DestroyImmediate(cell);

                    Debug.Log($"[Grab] {viewNames[row]} t={times[col]:F2} pulse={pulses[col]:F1} " +
                              $"hipsY={rig.Hips.position.y:F3} torso={anim.DebugTorsoPitch:F1} " +
                              $"head={anim.DebugHeadPitch:F1} " +
                              $"handL=({rig.LHand.position.x:F2},{rig.LHand.position.y:F2},{rig.LHand.position.z:F2}) " +
                              $"handR=({rig.RHand.position.x:F2},{rig.RHand.position.y:F2},{rig.RHand.position.z:F2}) " +
                              $"footL={rig.LFoot.position.y:F2} footR={rig.RFoot.position.y:F2}");
                }
            }

            anim.StrugglePulseOverride = -1f;

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "grabbed_sheet.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(floor);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] grabbed sheet: {outPath}");
        }

        /// <summary>
        /// 敵の攻撃 3 種（横薙ぎ・振り下ろし・突き）を、同じ瞬間どうしで見比べる。
        /// 予備動作／当たり／振り抜きの 3 段 × 3 種。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Enemy Attacks")]
        public static void CaptureEnemyAttacks()
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

            var attacks = new[] { AttackId.Jab, AttackId.Heavy, AttackId.Break, AttackId.Grab };
            var rows = new (EnemyPhase phase, float windup, float swing, string label)[]
            {
                (EnemyPhase.Windup, 0.78f, 0f, "cock"),
                (EnemyPhase.Attack, 0f, 0f, "hit"),
                (EnemyPhase.Recover, 0f, 0.10f, "swing"),
            };

            const int cellW = 380;
            const int cellH = 620;

            var sheet = new Texture2D(cellW * attacks.Length, cellH * rows.Length,
                TextureFormat.RGB24, false);

            for (int r = 0; r < rows.Length; r++)
            {
                for (int c = 0; c < attacks.Length; c++)
                {
                    figure.Preview(rows[r].phase, rows[r].windup, attacks[c], 0f, rows[r].swing);

                    cam.transform.position = new Vector3(h * 1.95f, h * 0.55f, h * 0.30f);
                    cam.transform.LookAt(new Vector3(0f, h * 0.50f, 0f));

                    var cell = RenderCell(cam, cellW, cellH);
                    // Texture の原点は左下なので、1 行目が上に来るように反転して置く
                    sheet.SetPixels(c * cellW, (rows.Length - 1 - r) * cellH, cellW, cellH, cell.GetPixels());
                    Object.DestroyImmediate(cell);

                    Vector3 hand = figure.WristBone(false).position;
                    Vector3 handL = figure.WristBone(true).position;
                    Debug.Log($"[EA] {attacks[c]} {rows[r].label} " +
                        $"shoulder={Mathf.DeltaAngle(0f, figure.ShoulderBone(false).localEulerAngles.x):F0} " +
                        $"elbow={Mathf.DeltaAngle(0f, figure.ElbowBone(false).localEulerAngles.x):F0} " +
                        $"hand=({hand.x:F2},{hand.y:F2},{hand.z:F2}) " +
                        $"handL=({handL.x:F2},{handL.y:F2},{handL.z:F2})");
                }
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "enemy_attacks.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(figureGo);
            Object.DestroyImmediate(floor);

            Debug.Log($"[Ryona] enemy attacks: {outPath}");
        }

        /// <summary>
        /// 肩の X 角を振って、どの角度が「前」「上」「後ろ」かを実測する。
        /// EnemyFigure の armRPitch は 0=真下 を基準にした回転なので、
        /// ポーズを組む前に一度きちんと対応表を作るためのシート。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Arm Sweep")]
        public static void CaptureArmSweep()
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
            var angles = new[] { -170f, -140f, -110f, -80f, -50f, -20f, 10f, 40f, 70f, 100f, 130f, 160f };

            const int cellW = 300;
            const int cellH = 620;
            var sheet = new Texture2D(cellW * angles.Length, cellH, TextureFormat.RGB24, false);

            // 一度だけ姿勢を作り、以後は肩の向きだけ差し替える（LateUpdate は回さない）
            figure.Preview(EnemyPhase.Chase, 0f, AttackId.Jab, 0f, 0f);

            Transform shoulder = figure.ShoulderBone(false);
            Quaternion rest = shoulder.localRotation;

            for (int i = 0; i < angles.Length; i++)
            {
                shoulder.localRotation = Quaternion.Euler(angles[i], 0f, -8f);

                cam.transform.position = new Vector3(h * 1.95f, h * 0.55f, h * 0.30f);
                cam.transform.LookAt(new Vector3(0f, h * 0.50f, 0f));

                var cell = RenderCell(cam, cellW, cellH);
                sheet.SetPixels(i * cellW, 0, cellW, cellH, cell.GetPixels());
                Object.DestroyImmediate(cell);

                Debug.Log($"[ArmSweep] cell {i} angle={angles[i]}");
            }

            shoulder.localRotation = rest;
            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "arm_sweep.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(figureGo);
            Object.DestroyImmediate(floor);

            Debug.Log($"[Ryona] arm sweep: {outPath}");
        }

        /// <summary>
        /// 敵の1モーションを通しで確認する。予備動作 → 振り抜き → 戻りが読めるか見る。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Enemy Strip")]
        public static void CaptureEnemyStrip()
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

            var shots = new (EnemyPhase phase, float windup, AttackId attack, float speed,
                float swing, int view, string label)[]
            {
                (EnemyPhase.Chase, 0f, AttackId.Jab, 1.1f, 0f, 0, "walk 横"),
                (EnemyPhase.Chase, 0f, AttackId.Jab, 1.1f, 0f, 1, "walk 正面"),
                (EnemyPhase.Windup, 0.95f, AttackId.Jab, 0f, 0f, 0, "jab 予備動作"),
                (EnemyPhase.Recover, 0f, AttackId.Jab, 0f, 0.10f, 0, "jab 振り抜き"),
                (EnemyPhase.Windup, 0.95f, AttackId.Heavy, 0f, 0f, 0, "heavy 予備動作"),
                (EnemyPhase.Recover, 0f, AttackId.Heavy, 0f, 0.10f, 0, "heavy 振り抜き"),
                (EnemyPhase.Windup, 0.95f, AttackId.Break, 0f, 0f, 1, "break 予備動作"),
                (EnemyPhase.Recover, 0f, AttackId.Break, 0f, 0.10f, 1, "break 振り抜き"),
                (EnemyPhase.Stagger, 0f, AttackId.Jab, 0f, 0f, 0, "stagger"),
                (EnemyPhase.Dead, 0f, AttackId.Jab, 0f, 0f, 0, "dead"),
            };

            const int cellW = 360;
            const int cellH = 620;

            var sheet = new Texture2D(cellW * shots.Length, cellH, TextureFormat.RGB24, false);

            for (int i = 0; i < shots.Length; i++)
            {
                var s = shots[i];
                figure.Preview(s.phase, s.windup, s.attack, s.speed, s.swing);

                if (s.view == 0)
                {
                    cam.transform.position = new Vector3(h * 1.95f, h * 0.55f, h * 0.30f);
                    cam.transform.LookAt(new Vector3(0f, h * 0.50f, 0f));
                }
                else
                {
                    cam.transform.position = new Vector3(h * 0.15f, h * 0.55f, h * 1.95f);
                    cam.transform.LookAt(new Vector3(0f, h * 0.50f, 0f));
                }

                var cell = RenderCell(cam, cellW, cellH);
                sheet.SetPixels(i * cellW, 0, cellW, cellH, cell.GetPixels());
                Object.DestroyImmediate(cell);

                Debug.Log($"[EnemyStrip] {s.label} handY={figure.name}");
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "enemy_strip.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(figureGo);
            Object.DestroyImmediate(floor);

            Debug.Log($"[Ryona] enemy strip: {outPath}");
        }

        /// <summary>
        /// 敵の右手首を寄りで撮る。前腕と拳が一直線のままだと「棒の先に塊」に見えるので、
        /// 構え・振りかぶり・当てる瞬間・振り抜きで手首が折れているかを目と数値で確認する。
        /// 数値は「前腕の向き」と「手首 → 拳の向き」の差（0 なら手首が無いのと同じ）。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Enemy Wrist")]
        public static void CaptureEnemyWrist()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLights();

            var skin = new Material(Shader.Find("Standard"));
            skin.color = new Color(0.63f, 0.51f, 0.45f);
            var cloth = new Material(Shader.Find("Standard"));
            cloth.color = new Color(0.35f, 0.30f, 0.28f);

            var figureGo = new GameObject("Enemy Figure");
            var figure = figureGo.AddComponent<EnemyFigure>();
            figure.BodyMaterial = skin;
            figure.ClothMaterial = cloth;

            var camGo = new GameObject("Wrist Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.02f;

            var shots = new (EnemyPhase phase, float windup, AttackId attack, float swing, string label)[]
            {
                (EnemyPhase.Chase, 0f, AttackId.Jab, 0f, "構え"),
                (EnemyPhase.Windup, 0.95f, AttackId.Heavy, 0f, "振りかぶり"),
                (EnemyPhase.Attack, 1f, AttackId.Jab, 0f, "当てる"),
                (EnemyPhase.Recover, 0f, AttackId.Jab, 0.10f, "振り抜き"),
            };

            const int cellW = 460;
            const int cellH = 460;
            var sheet = new Texture2D(cellW * shots.Length, cellH, TextureFormat.RGB24, false);

            for (int i = 0; i < shots.Length; i++)
            {
                var s = shots[i];
                figure.Preview(s.phase, s.windup, s.attack, 0f, s.swing);

                Transform wrist = figure.WristBone(false);
                Transform fist = figure.Fist(false);
                Vector3 elbow = figure.ElbowBone(false).position;

                float bend = Vector3.Angle(wrist.position - elbow, fist.position - wrist.position);

                Transform t = figure.transform;
                Vector3 look = wrist.position;
                cam.transform.position = look + t.right * 0.50f + t.forward * 0.18f + Vector3.up * 0.05f;
                cam.transform.LookAt(look);

                var cell = RenderCell(cam, cellW, cellH);
                sheet.SetPixels(i * cellW, 0, cellW, cellH, cell.GetPixels());
                Object.DestroyImmediate(cell);

                Debug.Log($"[EnemyWrist] {s.label} bend={bend:F1}deg wrist={wrist.position} fist={fist.position} cam={cam.transform.position}");
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "enemy_wrist.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(figureGo);

            Debug.Log($"[Ryona] enemy wrist: {outPath}");
        }

        /// <summary>
        /// 手のひらのひねり角を比べる。攻撃時に手のひらが上を向くと「手を差し出す」に見えるため、
        /// 前腕軸まわりのロールを何通りか描いて一番マシな角度を選ぶ。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Hand Roll")]
        public static void CaptureHandRollGrid()
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
            anim.AttackOverride = AttackLibrary.Light();

            foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
                smr.forceMatrixRecalculationPerRender = true;

            var camGo = new GameObject("Hand Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.30f, 0.32f, 0.36f);
            cam.fieldOfView = 26f;
            cam.nearClipPlane = 0.02f;

            float[] rolls = { 0f, -45f, -90f, -135f, -180f, 45f, 90f };

            const int cellW = 420;
            const int cellH = 560;

            var sheet = new Texture2D(cellW * rolls.Length, cellH, TextureFormat.RGB24, false);

            var upper = new Vector3(0.15f, 0.08f, 0.98f).normalized;
            var fore = new Vector3(0.03f, 0.02f, 1f).normalized;

            for (int i = 0; i < rolls.Length; i++)
            {
                anim.ForceState(CharacterAnimator.State.Attack, 0.050f, 0.6f);

                // 構えたあとに手首だけを回す
                rig.SetArm(false, upper, fore, rolls[i]);

                Vector3 hand = rig.RHand.position;
                cam.transform.position = hand + new Vector3(0.62f, 0.30f, 0.66f);
                cam.transform.LookAt(hand);

                var cell = RenderCell(cam, cellW, cellH);
                sheet.SetPixels(i * cellW, 0, cellW, cellH, cell.GetPixels());
                Object.DestroyImmediate(cell);

                Debug.Log($"[HandRoll] {rolls[i]:F0}deg hand=({hand.x:F3},{hand.y:F3},{hand.z:F3})");
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "hand_roll.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] hand roll: {outPath}");
        }

        /// <summary>
        /// 指を握らせる軸を特定する。VRM の指ボーンは曲がる軸がモデル依存なので、
        /// 候補を描いて「実際に拳になる」ものを選ぶ。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Finger Test")]
        public static void CaptureFingerTest()
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
            anim.AttackOverride = AttackLibrary.Light();

            foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
                smr.forceMatrixRecalculationPerRender = true;

            var camGo = new GameObject("Finger Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.30f, 0.31f, 0.35f);
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.01f;

            var tests = new (string label, Vector3 axis, float amount)[]
            {
                ("open", Vector3.zero, 0f),
                ("X+70", Vector3.right, 1f),
                ("X-70", Vector3.left, 1f),
                ("Y+70", Vector3.up, 1f),
                ("Z+70", Vector3.forward, 1f),
                ("Z-70", Vector3.back, 1f),
            };

            const int cellW = 420;
            const int cellH = 420;
            var sheet = new Texture2D(cellW * tests.Length, cellH, TextureFormat.RGB24, false);

            for (int i = 0; i < tests.Length; i++)
            {
                anim.ForceState(CharacterAnimator.State.Attack, 0.100f, 0.6f);

                if (tests[i].amount > 0f)
                    rig.CurlFingers(false, tests[i].amount, tests[i].axis);

                Vector3 hand = rig.RHand.position;
                cam.transform.position = hand + new Vector3(0.26f, 0.20f, 0.34f);
                cam.transform.LookAt(hand);

                var cell = RenderCell(cam, cellW, cellH);
                sheet.SetPixels(i * cellW, 0, cellW, cellH, cell.GetPixels());
                Object.DestroyImmediate(cell);

                Debug.Log($"[Finger] {tests[i].label} amount={tests[i].amount}");
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "finger_test.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] finger test: {outPath}");
        }

        /// <summary>
        /// 手と腕の動きを判定するための連番。歩き8コマ＋攻撃6コマを並べる。
        /// 肘が曲がっているか、手が体の前で止まっているかを見る。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Motion Strip")]
        public static void CaptureMotionStrip()
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

            var camGo = new GameObject("Motion Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.30f, 0.31f, 0.35f);
            cam.fieldOfView = 32f;
            cam.nearClipPlane = 0.05f;
            cam.transform.position = new Vector3(h * 0.78f, h * 0.58f, h * 1.55f);
            cam.transform.LookAt(new Vector3(0f, h * 0.50f, 0f));

            const int cellW = 340;
            const int cellH = 620;

            var sheet = new Texture2D(cellW * 8, cellH * 2, TextureFormat.RGB24, false);

            var shots = new (CharacterAnimator.State state, float time, string label)[]
            {
                (CharacterAnimator.State.Walk, 0.000f, "walk 0"),
                (CharacterAnimator.State.Walk, 0.125f, "walk 1"),
                (CharacterAnimator.State.Walk, 0.250f, "walk 2"),
                (CharacterAnimator.State.Walk, 0.375f, "walk 3"),
                (CharacterAnimator.State.Walk, 0.500f, "walk 4"),
                (CharacterAnimator.State.Walk, 0.625f, "walk 5"),
                (CharacterAnimator.State.Walk, 0.750f, "walk 6"),
                (CharacterAnimator.State.Walk, 0.875f, "walk 7"),
                (CharacterAnimator.State.Attack, 0.000f, "atk 0"),
                (CharacterAnimator.State.Attack, 0.040f, "atk 1"),
                (CharacterAnimator.State.Attack, 0.070f, "atk 2"),
                (CharacterAnimator.State.Attack, 0.100f, "atk 3"),
                (CharacterAnimator.State.Attack, 0.160f, "atk 4"),
                (CharacterAnimator.State.Attack, 0.250f, "atk 5"),
            };

            anim.AttackOverride = AttackLibrary.Light();

            for (int i = 0; i < shots.Length; i++)
            {
                var s = shots[i];
                anim.ForceState(s.state, s.time, s.state == CharacterAnimator.State.Attack ? 0.6f : 1f);

                var cell = RenderCell(cam, cellW, cellH);
                int col = i % 8;
                int row = i / 8;
                sheet.SetPixels(col * cellW, (1 - row) * cellH, cellW, cellH, cell.GetPixels());
                Object.DestroyImmediate(cell);

                Debug.Log($"[Motion] {s.label} handR=({rig.RHand.position.x:F2},{rig.RHand.position.y:F2},{rig.RHand.position.z:F2}) " +
                          $"elbowR=({rig.RLowerArm.position.x:F2},{rig.RLowerArm.position.y:F2},{rig.RLowerArm.position.z:F2})");
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "motion_strip.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] motion strip: {outPath}");
        }

        /// <summary>
        /// 左手の握りを 2 案（+z / -y）で、正面・左横・真上から撮って決める。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Left Fist Compare")]
        public static void CaptureLeftFistCompare()
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

            var camGo = new GameObject("Fist Camera 2");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.fieldOfView = 20f;
            cam.nearClipPlane = 0.02f;

            var axes = new[]
            {
                new Vector3(0f, 0f, 1f),
                new Vector3(0f, -1f, 0f),
            };

            const int cell = 520;
            var sheet = new Texture2D(cell * 3, cell * 2, TextureFormat.RGB24, false);

            for (int i = 0; i < axes.Length; i++)
            {
                rig.LeftFingerAxis = axes[i];

                anim.ForceState(CharacterAnimator.State.Idle, 0.5f, 2f, StaggerTier.None);
                anim.ApplyPose();

                Vector3 hand = rig.LHand.position;
                var views = new[]
                {
                    new Vector3(hand.x + 0.02f, hand.y + 0.02f, hand.z + 0.46f),
                    new Vector3(hand.x - 0.44f, hand.y + 0.04f, hand.z + 0.10f),
                    new Vector3(hand.x + 0.02f, hand.y + 0.46f, hand.z + 0.10f),
                };

                for (int v = 0; v < views.Length; v++)
                {
                    cam.transform.position = views[v];
                    cam.transform.LookAt(hand);

                    var tex = RenderCell(cam, cell, cell);
                    sheet.SetPixels(v * cell, (1 - i) * cell, cell, cell, tex.GetPixels());
                    Object.DestroyImmediate(tex);
                }
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "left_fist_compare.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] left fist compare: {outPath}");
        }

        /// <summary>
        /// 左手の指の曲げ軸を振って、拳になる向きを探す。
        /// 右手は拳なのに左手だけ「開いた手のまま」になるため、軸を実測で選び直す。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Left Finger Axis Sweep")]
        public static void CaptureLeftFingerAxisSweep()
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

            var camGo = new GameObject("Finger Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.fieldOfView = 22f;
            cam.nearClipPlane = 0.02f;

            var axes = new[]
            {
                new Vector3(0f, 0f, 1f),
                new Vector3(0f, 0f, -1f),
                new Vector3(1f, 0f, 0f),
                new Vector3(-1f, 0f, 0f),
                new Vector3(0f, 1f, 0f),
                new Vector3(0f, -1f, 0f),
            };

            const int cell = 400;
            var sheet = new Texture2D(cell * axes.Length, cell * 2, TextureFormat.RGB24, false);

            for (int i = 0; i < axes.Length; i++)
            {
                rig.LeftFingerAxis = axes[i];

                anim.ForceState(CharacterAnimator.State.Idle, 0.5f, 2f, StaggerTier.None);
                anim.ApplyPose();

                Vector3 hand = rig.LHand.position;
                var views = new[]
                {
                    new Vector3(hand.x + 0.08f, hand.y + 0.02f, hand.z + 0.48f),
                    new Vector3(hand.x - 0.42f, hand.y + 0.06f, hand.z + 0.22f),
                };

                for (int v = 0; v < views.Length; v++)
                {
                    cam.transform.position = views[v];
                    cam.transform.LookAt(hand);

                    var tex = RenderCell(cam, cell, cell);
                    sheet.SetPixels(i * cell, (1 - v) * cell, cell, cell, tex.GetPixels());
                    Object.DestroyImmediate(tex);
                }

                Debug.Log($"[Axis] {i} {axes[i]}");
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "left_finger_axis.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] left finger axis: {outPath}");
        }

        /// <summary>
        /// ダウン〜立ち上がりの全フレームを数値で総当たりする（描画なし）。
        /// 膝が逆・足が床下、の 2 つを 1 フレームも許さないための検査。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Check Down Joints")]
        public static void CheckDownJoints()
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

            if (vrm == null)
            {
                Debug.LogError("[Ryona] VRM load returned null");
                return;
            }

            var rig = vrm.gameObject.AddComponent<ProceduralRig>();
            rig.Capture();
            var anim = vrm.gameObject.AddComponent<CharacterAnimator>();
            anim.Rig = rig;

            int bad = 0;

            void Check(string label, CharacterAnimator.State state, float time, float duration)
            {
                anim.ForceState(state, time, duration, StaggerTier.None);
                anim.ApplyPose();

                float kneeL = Vector3.Angle(rig.LLowerLeg.position - rig.LUpperLeg.position,
                    rig.LFoot.position - rig.LLowerLeg.position);
                float kneeR = Vector3.Angle(rig.RLowerLeg.position - rig.RUpperLeg.position,
                    rig.RFoot.position - rig.RLowerLeg.position);

                float signL = Vector3.Dot(
                    Vector3.Cross(rig.LLowerLeg.position - rig.LUpperLeg.position,
                        rig.LFoot.position - rig.LLowerLeg.position).normalized, rig.transform.right);
                float signR = Vector3.Dot(
                    Vector3.Cross(rig.RLowerLeg.position - rig.RUpperLeg.position,
                        rig.RFoot.position - rig.RLowerLeg.position).normalized, rig.transform.right);

                float footL = rig.LFoot.position.y;
                float footR = rig.RFoot.position.y;

                bool badKnee = kneeL > 2f && signL < 0f || kneeR > 2f && signR < 0f;
                bool badKneeAngle = kneeL > 150f || kneeR > 150f;
                bool badFoot = footL < -0.03f || footR < -0.03f;
                bool badHips = rig.Hips.position.y < 0.05f;

                if (badKnee || badKneeAngle || badFoot || badHips)
                    bad++;

                Debug.Log($"[Chk] {(badKnee || badKneeAngle || badFoot || badHips ? "NG" : "ok")} {label}" +
                    $" kneeL={kneeL:F0}({signL:F2}) kneeR={kneeR:F0}({signR:F2})" +
                    $" footL.y={footL:F2} footR.y={footR:F2} hips.y={rig.Hips.position.y:F2}");

                if (state == CharacterAnimator.State.Down && time > 1.15f && time < 1.35f)
                {
                    Vector3 hipL = rig.LUpperLeg.position;
                    Vector3 knee = rig.LLowerLeg.position;
                    Vector3 ankle = rig.LFoot.position;
                    Vector3 pole = rig.transform.position + rig.transform.forward * 2f + Vector3.up * 0.55f;
                    Vector3 kneeLocal = Quaternion.Inverse(rig.Hips.rotation) * (knee - rig.Hips.position);
                    Vector3 ankleLocal = Quaternion.Inverse(rig.Hips.rotation) * (ankle - rig.Hips.position);
                    Debug.Log($"[Dbg] {label}" +
                        $" hip=({hipL.x:F2},{hipL.y:F2},{hipL.z:F2})" +
                        $" knee=({knee.x:F2},{knee.y:F2},{knee.z:F2})" +
                        $" ankle=({ankle.x:F2},{ankle.y:F2},{ankle.z:F2})" +
                        $" pole=({pole.x:F2},{pole.y:F2},{pole.z:F2})" +
                        $" hipToAnkle={Vector3.Distance(hipL, ankle):F2}" +
                        $" hipsEuler={rig.Hips.rotation.eulerAngles}" +
                        $" kneeInHips=({kneeLocal.x:F2},{kneeLocal.y:F2},{kneeLocal.z:F2})" +
                        $" ankleInHips=({ankleLocal.x:F2},{ankleLocal.y:F2},{ankleLocal.z:F2})");
                }
            }

            for (float t = 0.30f; t <= 2.001f; t += 0.10f)
                Check($"Down t={t:F2}", CharacterAnimator.State.Down, t, 2f);

            for (float u = 0.05f; u <= 1.001f; u += 0.05f)
                Check($"GetUp u={u:F2}", CharacterAnimator.State.GetUp, u * 1.2f, 1.2f);

            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] down joint check: bad={bad}");
        }

        /// <summary>
        /// 全ステート（待機・歩行・構え・攻撃・スタッガー・ダウン・立ち上がり）を総当たりで数値検査する。
        /// 肘・膝が逆に折れていないか、関節角が伸び切っていないか、手首が折れていないか、
        /// 足と腰が床を突き抜けていないかを見る（描画なし・数分で終わる）。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Check All Joints")]
        public static void CheckAllJoints()
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

            // 肘の蝶番軸を、リスト姿勢の上腕ローカル座標で捕まえる。
            // 肘は前腕が体の前へ曲がるのが正常。軸は「上腕の向き × 体の前」。
            // 胸の右方向を基準にすると、肩を回旋した攻撃モーションで判定が裏返る。
            Vector3 restArmL = (rig.LLowerArm.position - rig.LUpperArm.position).normalized;
            Vector3 restArmR = (rig.RLowerArm.position - rig.RUpperArm.position).normalized;
            Vector3 restHingeL = Vector3.Cross(restArmL, Vector3.forward).normalized;
            Vector3 restHingeR = Vector3.Cross(restArmR, Vector3.forward).normalized;

            Vector3 hingeLocalL = Quaternion.Inverse(rig.LUpperArm.rotation) * restHingeL;
            Vector3 hingeLocalR = Quaternion.Inverse(rig.RUpperArm.rotation) * restHingeR;

            Debug.Log($"[Chk2] hinge rest L=({restHingeL.x:F2},{restHingeL.y:F2},{restHingeL.z:F2})" +
                $" R=({restHingeR.x:F2},{restHingeR.y:F2},{restHingeR.z:F2})");

            int bad = 0;
            int total = 0;

            void Check(string label, CharacterAnimator.State state, float time, float duration,
                StaggerTier tier = StaggerTier.None, bool standing = false)
            {
                anim.ForceState(state, time, duration, tier, log: false);
                anim.ApplyPose();

                // --- 膝（向きは腰のローカル右で見る。寝ていても意味が変わらない） ---
                Vector3 thighL = rig.LLowerLeg.position - rig.LUpperLeg.position;
                Vector3 shinL = rig.LFoot.position - rig.LLowerLeg.position;
                Vector3 thighR = rig.RLowerLeg.position - rig.RUpperLeg.position;
                Vector3 shinR = rig.RFoot.position - rig.RLowerLeg.position;

                float kneeL = Vector3.Angle(thighL, shinL);
                float kneeR = Vector3.Angle(thighR, shinR);

                float kneeSignL = Vector3.Dot(Vector3.Cross(thighL, shinL).normalized, rig.Hips.right);
                float kneeSignR = Vector3.Dot(Vector3.Cross(thighR, shinR).normalized, rig.Hips.right);

                // --- 肘（向きは胸のローカル右で見る） ---
                Vector3 armL = rig.LLowerArm.position - rig.LUpperArm.position;
                Vector3 foreL = rig.LHand.position - rig.LLowerArm.position;
                Vector3 armR = rig.RLowerArm.position - rig.RUpperArm.position;
                Vector3 foreR = rig.RHand.position - rig.RLowerArm.position;

                float elbowL = Vector3.Angle(armL, foreL);
                float elbowR = Vector3.Angle(armR, foreR);

                float elbowSignL = Vector3.Dot(Vector3.Cross(armL, foreL).normalized,
                    rig.LUpperArm.rotation * hingeLocalL);
                float elbowSignR = Vector3.Dot(Vector3.Cross(armR, foreR).normalized,
                    rig.RUpperArm.rotation * hingeLocalR);

                // --- 手首（前腕の向きと手の向きの差） ---
                var midL = at != null ? at.GetBoneTransform(HumanBodyBones.LeftMiddleProximal) : null;
                var midR = at != null ? at.GetBoneTransform(HumanBodyBones.RightMiddleProximal) : null;

                float wristL = midL != null ? Vector3.Angle(foreL, midL.position - rig.LHand.position) : 0f;
                float wristR = midR != null ? Vector3.Angle(foreR, midR.position - rig.RHand.position) : 0f;

                float footL = rig.LFoot.position.y;
                float footR = rig.RFoot.position.y;
                float hipsY = rig.Hips.position.y;

                // つま先（ボール）。かかとを上げてもここは床に残るはず。
                float toeL = rig.LToe != null ? rig.LToe.position.y : 0f;
                float toeR = rig.RToe != null ? rig.RToe.position.y : 0f;

                var why = new System.Collections.Generic.List<string>();
                var warn = new System.Collections.Generic.List<string>();

                // ほぼ伸び切った関節は外積が小さく、向きの符号が数値誤差で決まる。
                // 「曲がっている時だけ」符号を見る（曲がり 20 度未満は警告どまり）。
                bool kneeBentL = kneeL >= 8f;
                bool kneeBentR = kneeR >= 8f;
                bool elbowBentL = elbowL >= 25f;
                bool elbowBentR = elbowR >= 25f;

                if (kneeBentL && kneeSignL < 0f) why.Add("kneeL");
                else if (!kneeBentL && kneeSignL < 0f) warn.Add("kneeL(straight)");

                if (kneeBentR && kneeSignR < 0f) why.Add("kneeR");
                else if (!kneeBentR && kneeSignR < 0f) warn.Add("kneeR(straight)");

                // 肘の蝶番軸は「曲がる向きが正」なので、負なら逆に折れている。
                if (elbowBentL && elbowSignL < 0f) why.Add("elbowL");
                else if (!elbowBentL && elbowSignL < 0f) warn.Add("elbowL(straight)");

                if (elbowBentR && elbowSignR < 0f) why.Add("elbowR");
                else if (!elbowBentR && elbowSignR < 0f) warn.Add("elbowR(straight)");

                // 曲がりすぎ（人が取れない角度）
                if (kneeL > 160f) why.Add("kneeFoldL");
                if (kneeR > 160f) why.Add("kneeFoldR");
                if (elbowL > 165f) why.Add("elbowFoldL");
                if (elbowR > 165f) why.Add("elbowFoldR");

                if (wristL > 75f) why.Add("wristL");
                if (wristR > 75f) why.Add("wristR");
                if (footL < -0.03f) why.Add("footL");
                if (footR < -0.03f) why.Add("footR");
                if (toeL < -0.02f) why.Add("toeL");
                if (toeR < -0.02f) why.Add("toeR");
                if (hipsY < 0.05f) why.Add("hips");
                if (standing && hipsY < 0.55f) why.Add("hipsLow");

                total++;
                if (why.Count > 0) bad++;

                Debug.Log($"[Chk2] {(why.Count > 0 ? "NG" : "ok")} {label}" +
                    $" knee={kneeL:F0}/{kneeR:F0}({kneeSignL:F2}/{kneeSignR:F2})" +
                    $" elbow={elbowL:F0}/{elbowR:F0}({elbowSignL:F2}/{elbowSignR:F2})" +
                    $" wrist={wristL:F0}/{wristR:F0}" +
                    $" foot={footL:F2}/{footR:F2} hips={hipsY:F2}" +
                    $" toe={toeL:F2}/{toeR:F2}" +
                    $" armL={armL.magnitude:F3} foreL={foreL.magnitude:F3}" +
                    $" spanL={Vector3.Distance(rig.LUpperArm.position, rig.LHand.position):F3}" +
                    (why.Count > 0 ? $" why={string.Join(",", why)}" : "") +
                    (warn.Count > 0 ? $" warn={string.Join(",", warn)}" : ""));
            }

            for (float t = 0.2f; t <= 2.001f; t += 0.2f)
                Check($"Idle t={t:F1}", CharacterAnimator.State.Idle, t, 2f, standing: true);

            for (float t = 0f; t <= 1.001f; t += 0.1f)
                Check($"Walk t={t:F1}", CharacterAnimator.State.Walk, t, 1f, standing: true);

            for (float t = 0f; t <= 1.001f; t += 0.1f)
                Check($"Guard t={t:F1}", CharacterAnimator.State.Guard, t, 1f, standing: true);

            // 掴まれてもがいている姿勢（相手に引かれて上体が反り、両手で相手の腕を掴む）
            for (float t = 0f; t <= 1.501f; t += 0.1f)
                Check($"Grabbed t={t:F1}", CharacterAnimator.State.Grabbed, t, 3f, standing: true);

            var attacks = new[]
            {
                ("Light", AttackLibrary.Light()),
                ("Heavy", AttackLibrary.Heavy()),
                ("Break", AttackLibrary.BreakAttack()),
            };

            foreach (var (name, data) in attacks)
            {
                anim.AttackOverride = data;
                float dur = data.TotalSeconds;

                for (float t = 0f; t <= dur + 0.0001f; t += 0.01f)
                    Check($"Attack {name} t={t:F2}/{dur:F2}", CharacterAnimator.State.Attack, t, dur, standing: true);
            }

            anim.AttackOverride = null;

            var tiers = new[]
            {
                (StaggerTier.Small, 0.35f),
                (StaggerTier.Medium, 0.50f),
                (StaggerTier.Large, 0.70f),
                (StaggerTier.GuardBreak, 1.20f),
            };

            foreach (var (tier, dur) in tiers)
            {
                for (float u = 0f; u <= 1.001f; u += 0.125f)
                    Check($"Stagger {tier} u={u:F2}", CharacterAnimator.State.Stagger, u * dur, dur, tier);
            }

            for (float t = 0.3f; t <= 2.001f; t += 0.1f)
                Check($"Down t={t:F2}", CharacterAnimator.State.Down, t, 2f);

            for (float u = 0.05f; u <= 1.001f; u += 0.05f)
                Check($"GetUp u={u:F2}", CharacterAnimator.State.GetUp, u * 1.2f, 1.2f);

            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] all joint check: bad={bad} total={total}");
        }

        /// <summary>
        /// ダウン〜立ち上がりの骨格だけを描く（関節＝球、骨＝カプセル）。
        /// メッシュを隠すので、人体として有り得ない曲がりが角度ではっきり見える。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Down Rig")]
        public static void CaptureDownRig()
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

            // メッシュは隠す（骨格だけ見る）
            foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
                smr.enabled = false;

            var camGo = new GameObject("Rig Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.17f, 0.20f);
            cam.fieldOfView = 32f;
            cam.nearClipPlane = 0.02f;

            var boneMat = new Material(Shader.Find("Unlit/Color"));
            boneMat.color = new Color(0.55f, 0.80f, 1f);
            var jointMat = new Material(Shader.Find("Unlit/Color"));
            jointMat.color = new Color(1f, 0.55f, 0.35f);

            var skeleton = new GameObject("Skeleton");

            var poses = new[]
            {
                (name: "Down end", state: CharacterAnimator.State.Down, time: 2.0f, duration: 2.0f),
                (name: "Down mid", state: CharacterAnimator.State.Down, time: 1.1f, duration: 2.0f),
                (name: "GetUp .35", state: CharacterAnimator.State.GetUp, time: 0.35f, duration: 1.0f),
                (name: "GetUp .60", state: CharacterAnimator.State.GetUp, time: 0.60f, duration: 1.0f),
                (name: "GetUp .85", state: CharacterAnimator.State.GetUp, time: 0.85f, duration: 1.0f),
            };

            const int cell = 520;
            var sheet = new Texture2D(cell * 3, cell * poses.Length, TextureFormat.RGB24, false);

            for (int i = 0; i < poses.Length; i++)
            {
                var p = poses[i];
                anim.ForceState(p.state, p.time, p.duration, StaggerTier.None);
                anim.ApplyPose();

                BuildSkeleton(skeleton.transform, rig, boneMat, jointMat);

                Vector3 hips = rig.Hips.position;
                var views = new[]
                {
                    new Vector3(hips.x + 2.4f, hips.y + 0.55f, hips.z + 0.15f),
                    new Vector3(hips.x + 0.25f, hips.y + 1.05f, hips.z + 2.3f),
                    new Vector3(hips.x - 0.05f, hips.y + 2.5f, hips.z + 0.05f),
                };

                for (int v = 0; v < views.Length; v++)
                {
                    cam.transform.position = views[v];
                    cam.transform.LookAt(new Vector3(hips.x, hips.y, hips.z + 0.25f));

                    var tex = RenderCell(cam, cell, cell);
                    sheet.SetPixels(v * cell, (poses.Length - 1 - i) * cell, cell, cell, tex.GetPixels());
                    Object.DestroyImmediate(tex);
                }

                LogJointAngles(p.name, rig);
            }

            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "down_rig.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            DestroyChildren(skeleton);
            Object.DestroyImmediate(skeleton);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);

            Debug.Log($"[Ryona] down rig: {outPath}");
        }

        static void DestroyChildren(GameObject go)
        {
            for (int i = go.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
        }

        static void AddJoint(Transform parent, Vector3 position, float size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static void AddBone(Transform parent, Vector3 a, Vector3 b, float radius, Material mat)
        {
            Vector3 dir = b - a;
            float length = dir.magnitude;
            if (length < 1e-4f)
                return;

            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.transform.SetParent(parent, false);
            go.transform.position = (a + b) * 0.5f;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
            go.transform.localScale = new Vector3(radius, length * 0.5f, radius);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static void BuildSkeleton(Transform parent, ProceduralRig rig, Material boneMat, Material jointMat)
        {
            DestroyChildren(parent.gameObject);

            void Chain(Transform a, Transform b, Transform c, float r)
            {
                if (a == null || b == null)
                    return;

                AddBone(parent, a.position, b.position, r, boneMat);
                if (c != null)
                    AddBone(parent, b.position, c.position, r, boneMat);
            }

            Chain(rig.Hips, rig.Spine, rig.Chest, 0.045f);
            Chain(rig.Chest, rig.Neck, rig.Head, 0.035f);
            Chain(rig.LUpperArm, rig.LLowerArm, rig.LHand, 0.026f);
            Chain(rig.RUpperArm, rig.RLowerArm, rig.RHand, 0.026f);
            Chain(rig.LUpperLeg, rig.LLowerLeg, rig.LFoot, 0.032f);
            Chain(rig.RUpperLeg, rig.RLowerLeg, rig.RFoot, 0.032f);

            void Joint(Transform t, float s)
            {
                if (t != null)
                    AddJoint(parent, t.position, s, jointMat);
            }

            Joint(rig.Hips, 0.075f);
            Joint(rig.Neck, 0.05f);
            Joint(rig.LLowerArm, 0.045f);
            Joint(rig.RLowerArm, 0.045f);
            Joint(rig.LLowerLeg, 0.05f);
            Joint(rig.RLowerLeg, 0.05f);
        }

        /// <summary>
        /// 関節が人体の向きに曲がっているかを数値で出す。
        /// 膝は「ふくらはぎが後ろへ折れる」、肘は「前腕が前へ折れる」が正。
        /// 逆符号なら、その関節は有り得ない方向へ曲がっている。
        /// </summary>
        static void LogJointAngles(string label, ProceduralRig rig)
        {
            Vector3 right = rig.transform.right;
            Vector3 forward = rig.transform.forward;

            float KneeSign(Transform hip, Transform knee, Transform foot)
            {
                Vector3 u = knee.position - hip.position;
                Vector3 s = foot.position - knee.position;
                Vector3 n = Vector3.Cross(u, s);
                if (n.sqrMagnitude < 1e-8f)
                    return 0f;
                return Vector3.Dot(n.normalized, right);
            }

            float KneeAngle(Transform hip, Transform knee, Transform foot)
                => Vector3.Angle(knee.position - hip.position, foot.position - knee.position);

            float ElbowSign(Transform shoulder, Transform elbow, Transform hand)
            {
                Vector3 u = elbow.position - shoulder.position;
                Vector3 s = hand.position - elbow.position;
                Vector3 n = Vector3.Cross(u, s);
                if (n.sqrMagnitude < 1e-8f)
                    return 0f;
                return Vector3.Dot(n.normalized, right);
            }

            float ElbowAngle(Transform shoulder, Transform elbow, Transform hand)
                => Vector3.Angle(elbow.position - shoulder.position, hand.position - elbow.position);

            // 骨の原点: 大腿=股関節 / 下腿=膝 / 足=足首
            float thighL = Vector3.Angle(rig.LLowerLeg.position - rig.LUpperLeg.position, Vector3.up);
            float thighR = Vector3.Angle(rig.RLowerLeg.position - rig.RUpperLeg.position, Vector3.up);

            Debug.Log($"[Rig] {label}" +
                $" kneeL={KneeAngle(rig.LUpperLeg, rig.LLowerLeg, rig.LFoot):F0}deg sign={KneeSign(rig.LUpperLeg, rig.LLowerLeg, rig.LFoot):F2}" +
                $" kneeR={KneeAngle(rig.RUpperLeg, rig.RLowerLeg, rig.RFoot):F0}deg sign={KneeSign(rig.RUpperLeg, rig.RLowerLeg, rig.RFoot):F2}" +
                $" elbowL={ElbowAngle(rig.LUpperArm, rig.LLowerArm, rig.LHand):F0}deg sign={ElbowSign(rig.LUpperArm, rig.LLowerArm, rig.LHand):F2}" +
                $" elbowR={ElbowAngle(rig.RUpperArm, rig.RLowerArm, rig.RHand):F0}deg sign={ElbowSign(rig.RUpperArm, rig.RLowerArm, rig.RHand):F2}" +
                $" thighL-fromUp={thighL:F0} thighR-fromUp={thighR:F0}" +
                $" hips=({rig.Hips.position.x:F2},{rig.Hips.position.y:F2},{rig.Hips.position.z:F2})" +
                $" footL.y={rig.LFoot.position.y:F2} footR.y={rig.RFoot.position.y:F2}");
        }

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
