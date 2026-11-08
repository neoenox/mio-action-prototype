using System.Collections.Generic;
using System.IO;
using Ryona.Game;
using UniVRM10;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ryona.EditorTools
{
    /// <summary>
    /// 被弾ボイスに合わせた口と、強く閉じた目の見え方を並べて書き出す（<c>voice_faces.png</c>）。
    /// <see cref="PlayerFace.HurtFace"/> をそのまま使うので、コードの配合を変えたらこのシートで見る。
    /// 左上から: 目だけ（閉じ・強く閉じ）、う / あ / お / ふ（口の開き 最大）、拘束中の困り眉＋お。
    /// </summary>
    public static class VoiceFacePreview
    {
        const string VrmName = "mio_v1.vrm";

        [MenuItem("Ryona/Diagnostics/Capture Voice Faces")]
        public static void Capture()
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
            if (!File.Exists(vrmPath))
            {
                Debug.LogError($"[Ryona] VRM not found: {vrmPath}");
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var lightGo = new GameObject("Key Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.55f;
            lightGo.transform.rotation = Quaternion.Euler(25f, 200f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.42f, 0.45f);

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

            var renderers = vrm.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var s in renderers)
                s.forceMatrixRecalculationPerRender = true;

            var camGo = new GameObject("Face Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.27f, 0.30f);
            cam.fieldOfView = 20f;
            cam.nearClipPlane = 0.02f;
            Vector3 face = rig.Head.position + rig.Head.up * 0.045f + rig.Head.forward * 0.05f;
            cam.transform.position = face + new Vector3(0.04f, 0.0f, 0.46f);
            cam.transform.LookAt(face);

            var faces = PlayerFace.HurtFaceSheet();

            const int cellW = 400;
            const int cellH = 300;
            int cols = 4;
            int rows = (faces.Count + cols - 1) / cols;
            var sheet = new Texture2D(cellW * cols, cellH * rows, TextureFormat.RGB24, false);

            for (int i = 0; i < faces.Count; i++)
            {
                var (name, shapes) = faces[i];
                // 実機と同じ分担: 目と母音は Expression（関連するシェイプもまとめて動く）、
                // 眉と目の力みは割り当ての無いシェイプを直接書く。
                float Get(string key) => shapes.TryGetValue(key, out float w) ? w : 0f;
                var e = vrm.Runtime.Expression;
                e.SetWeight(ExpressionKey.Blink, Get("Fcl_EYE_Close"));
                e.SetWeight(ExpressionKey.Aa, Get("Fcl_MTH_A"));
                e.SetWeight(ExpressionKey.Ou, Get("Fcl_MTH_U"));
                e.SetWeight(ExpressionKey.Oh, Get("Fcl_MTH_O"));
                vrm.Runtime.Process();
                foreach (var s in renderers)
                {
                    var mesh = s.sharedMesh;
                    if (mesh == null)
                        continue;
                    foreach (var key in PlayerFace.DirectHurtShapes)
                    {
                        int idx = mesh.GetBlendShapeIndex(key);
                        if (idx >= 0)
                            s.SetBlendShapeWeight(idx, Get(key) * 100f);
                    }
                }

                var cell = Render(cam, cellW, cellH);
                int cx = i % cols;
                int cy = rows - 1 - i / cols;
                sheet.SetPixels(cx * cellW, cy * cellH, cellW, cellH, cell.GetPixels());
                Object.DestroyImmediate(cell);
                Debug.Log($"[VoiceFace] {i} {name}");
            }

            sheet.Apply();
            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "voice_faces.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(vrm.gameObject);
            Debug.Log($"[Ryona] voice faces: {outPath}");
        }

        static Texture2D Render(Camera cam, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            cam.targetTexture = null;
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }
    }
}
