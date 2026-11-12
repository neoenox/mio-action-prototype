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
    public static partial class CharacterPreview
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

    }
}


