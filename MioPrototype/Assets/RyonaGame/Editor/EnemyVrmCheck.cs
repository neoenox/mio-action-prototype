using System.IO;
using Ryona.Game;
using UniVRM10;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ryona.EditorTools
{
    /// <summary>
    /// 書き出した敵 VRM（<c>enemy_v1.vrm</c>）を、ゲームに入れる前に 1 分で検品する。
    ///
    /// 「置いたのにゲームで変なことになる」を先に潰すための道具。見るのは 3 つだけ。
    ///   1. 置き場所が正しいか（ゲームと同じ 4 か所を同じ順で探す）
    ///   2. 背丈が Mio より大きいか（当たり判定カプセル 1.8m と合っているか）
    ///   3. 人の骨が揃っているか（揃っていないと腕が伸びない・足が動かない）
    /// </summary>
    public static class EnemyVrmCheck
    {
        const string FileName = "enemy_v1.vrm";

        /// <summary>ゲーム側が動かす骨。1 本でも欠けると、その部位は動かない。</summary>
        static readonly (HumanBodyBones bone, string label)[] RequiredBones =
        {
            (HumanBodyBones.Hips, "腰"),
            (HumanBodyBones.Spine, "背骨"),
            (HumanBodyBones.Chest, "胸"),
            (HumanBodyBones.Neck, "首"),
            (HumanBodyBones.Head, "頭"),
            (HumanBodyBones.LeftUpperArm, "左肩"),
            (HumanBodyBones.LeftLowerArm, "左ひじ"),
            (HumanBodyBones.LeftHand, "左てのひら"),
            (HumanBodyBones.RightUpperArm, "右肩"),
            (HumanBodyBones.RightLowerArm, "右ひじ"),
            (HumanBodyBones.RightHand, "右てのひら"),
            (HumanBodyBones.LeftUpperLeg, "左もも"),
            (HumanBodyBones.LeftLowerLeg, "左ひざ"),
            (HumanBodyBones.LeftFoot, "左足"),
            (HumanBodyBones.RightUpperLeg, "右もも"),
            (HumanBodyBones.RightLowerLeg, "右ひざ"),
            (HumanBodyBones.RightFoot, "右足"),
        };

        [MenuItem("Ryona/Diagnostics/Check Enemy VRM")]
        public static void Check()
        {
            string path = Resolve();
            if (path == null)
            {
                Debug.LogWarning(
                    "[EnemyVRM] enemy_v1.vrm が見つかりません。次のどれか 1 か所に置けば、その回から敵が VRM になります:\n  " +
                    string.Join("\n  ", EnemyVrm.BuildSearchPaths(FileName)));
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var key = new GameObject("Key Light");
            var keyLight = key.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.1f;
            keyLight.color = new Color(1f, 0.96f, 0.92f);
            key.transform.rotation = Quaternion.Euler(42f, -35f, 0f);

            var fill = new GameObject("Fill Light");
            var fillLight = fill.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.intensity = 0.45f;
            fillLight.color = new Color(0.72f, 0.78f, 0.95f);
            fill.transform.rotation = Quaternion.Euler(16f, 140f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.34f, 0.36f, 0.40f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = Vector3.one * 6f;
            var floorMat = new Material(Shader.Find("Standard"));
            floorMat.color = new Color(0.22f, 0.23f, 0.26f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;

            Vrm10Instance vrm = Vrm10
                .LoadPathAsync(path, canLoadVrm0X: true, showMeshes: true)
                .GetAwaiter().GetResult();

            foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
                smr.forceMatrixRecalculationPerRender = true;

            // --- 背丈 ---
            float height = ComputeHeight(vrm.gameObject);

            // --- 骨 ---
            var animator = vrm.GetComponent<Animator>();
            int missing = 0;
            var missingLabels = "";
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                Debug.LogError("[EnemyVRM] Humanoid ではありません。VRoid の書き出しで「VRM」を選んでください（glb ではありません）。");
            }
            else
            {
                foreach (var (bone, label) in RequiredBones)
                {
                    if (animator.GetBoneTransform(bone) != null)
                        continue;

                    missing++;
                    missingLabels += label + " ";
                }
            }

            // --- 撮影（正面 / 横 / 斜め） ---
            var camGo = new GameObject("Enemy Check Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 30f;

            float h = Mathf.Max(1f, height);
            float d = h * 1.9f;

            Shoot(cam, new Vector3(0f, h * 0.55f, d), new Vector3(0f, h * 0.5f, 0f), 34f, 640, 900, "enemy_vrm_front.png");
            Shoot(cam, new Vector3(d, h * 0.55f, 0f), new Vector3(0f, h * 0.5f, 0f), 34f, 640, 900, "enemy_vrm_side.png");
            Shoot(cam, new Vector3(d * 0.7f, h * 0.6f, d * 0.7f), new Vector3(0f, h * 0.5f, 0f), 34f, 640, 900, "enemy_vrm_quarter.png");

            // --- 結果 ---
            Debug.Log(
                $"[EnemyVRM] 見つけた場所: {path}\n" +
                $"[EnemyVRM] 背丈: {height:F2}m（ゲームは 1.72 / 1.79 / 1.85 に自動で合わせます）\n" +
                $"[EnemyVRM] 骨: {(missing == 0 ? "17 本そろっています" : $"欠け {missing} 本 → {missingLabels}")}\n" +
                $"[EnemyVRM] 画像: enemy_vrm_front.png / enemy_vrm_side.png / enemy_vrm_quarter.png");

            if (height < 1.6f)
                Debug.LogWarning("[EnemyVRM] 背丈が低すぎます。VRoid の身長スライダーで 1.80 前後にしてください（当たり判定は 1.8m）。");

            if (height > 2.1f)
                Debug.LogWarning("[EnemyVRM] 背丈が高すぎます。1.80 前後にしてください。");

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(floor);
            Object.DestroyImmediate(key);
            Object.DestroyImmediate(fill);
            Object.DestroyImmediate(vrm.gameObject);
        }

        static string Resolve()
        {
            foreach (string candidate in EnemyVrm.BuildSearchPaths(FileName))
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        static float ComputeHeight(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return 0f;

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds.size.y;
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
            File.WriteAllBytes(Path.Combine(root, fileName), tex.EncodeToPNG());

            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
        }
    }
}
