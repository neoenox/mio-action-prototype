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

    }
}


