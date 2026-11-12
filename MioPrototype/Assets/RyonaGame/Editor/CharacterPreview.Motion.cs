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

    }
}


