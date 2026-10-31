using System.IO;
using Ryona.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ryona.EditorTools
{
    /// <summary>
    /// 検証用シーンの自動生成。VRM がまだ無いので、主人公はカプセルで代用する。
    /// VRM が届いたら Mio/Visual の子を差し替えるだけでよい構造にしてある。
    /// </summary>
    public static class PrototypeSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Prototype.unity";

        /// <summary>
        /// ステージ（ベルト）の中心 X。横スクロールなので、帯は X 方向に伸びる。
        /// 画面の上下（Z）は狭い帯に閉じ込め、左右（X）だけ長く歩ける。
        /// </summary>
        const float StageCenterX = 9f;

        /// <summary>帯の幅（Z の半幅）。これより外へは見えない壁で出られない。</summary>
        const float StageHalfDepth = 2.0f;

        [MenuItem("Ryona/Build Prototype Scene")]
        public static void BuildMenu() => Build();

        /// <summary>組み上がったシーンの見た目を確認するためのプレビュー出力。</summary>
        [MenuItem("Ryona/Capture Preview")]
        public static void CapturePreview()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("[Ryona] Main Camera not found");
                return;
            }

            cam.transform.position = new Vector3(2.9f, 1.9f, -5.4f);
            cam.transform.LookAt(new Vector3(0f, 1.0f, -0.4f));

            const int width = 1280;
            const int height = 720;

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "preview.png");
            File.WriteAllBytes(outPath, tex.EncodeToPNG());

            Object.DestroyImmediate(tex);
            rt.Release();

            Debug.Log($"[Ryona] Preview written: {outPath}");
        }

        /// <summary>Unity -batchmode -executeMethod Ryona.EditorTools.PrototypeSceneBuilder.Build</summary>
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var standard = Shader.Find("Standard");
            var groundMat = MakeMaterial(standard, new Color(0.28f, 0.28f, 0.30f));
            var bodyMat = MakeMaterial(standard, new Color(0.16f, 0.16f, 0.18f));
            // 敵は3体が同じ服・同じ背丈だと「複製」に見えるので、少しずつ変える。
            // 背丈は EnemyFigure.Height。VRM に差し替えたときも FitHeight がこの値に合わせる。
            var enemyHeights = new[] { 1.72f, 1.79f, 1.85f };
            var enemyClothColors = new[]
            {
                new Color(0.40f, 0.35f, 0.32f),   // 茶グレー
                new Color(0.30f, 0.33f, 0.38f),   // 青グレー
                new Color(0.38f, 0.30f, 0.31f),   // 赤グレー
            };
            var enemySkinColors = new[]
            {
                new Color(0.62f, 0.50f, 0.44f),
                new Color(0.58f, 0.46f, 0.40f),
                new Color(0.66f, 0.53f, 0.47f),
            };

            // --- 地面 -------------------------------------------------
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(7.2f, 1f, 0.7f);
            ground.transform.position = new Vector3(StageCenterX, 0f, 0f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;

            // --- ステージの縁（ベルトの帯） -----------------------------
            // 見た目は低い縁石、当たり判定は見えない壁。
            // プレイヤーも敵も Z の帯から出られないので、画面の上下に逃げられない。
            var curbMat = MakeMaterial(standard, new Color(0.20f, 0.20f, 0.23f));
            BuildBox("Curb", curbMat, new Vector3(StageCenterX, 0.06f, -StageHalfDepth),
                new Vector3(72f, 0.12f, 0.22f));
            BuildBox("Curb", curbMat, new Vector3(StageCenterX, 0.06f, StageHalfDepth),
                new Vector3(72f, 0.12f, 0.22f));

            BuildBound(-StageHalfDepth - 0.2f);
            BuildBound(StageHalfDepth + 0.2f);

            // ステージの端（行き止まり）
            BuildBoundX(-21f);
            BuildBoundX(39f);

            // 歩いた距離が分かるように、床に横線を入れる（ベルトの目盛り）
            for (float x = -18f; x <= 36f; x += 3f)
                BuildBox("Depth Mark", curbMat, new Vector3(x, 0.012f, 0f),
                    new Vector3(0.07f, 0.01f, 2f * StageHalfDepth - 0.3f));

            // --- 奥の街並み -------------------------------------------
            // 上半分が空（スカイボックス）だとベルトに見えないので、奥を建物で埋める。
            var backMat = MakeMaterial(standard, new Color(0.26f, 0.27f, 0.32f));
            var buildingMat = MakeMaterial(standard, new Color(0.19f, 0.20f, 0.24f));
            BuildBackdrop(backMat, buildingMat);

            // --- 光源 -------------------------------------------------
            var lightGo = new GameObject("Key Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.96f, 0.92f);
            lightGo.transform.rotation = Quaternion.Euler(RenderSetup.KeyElevation, RenderSetup.KeyYaw, 0f);

            // 影。無いとキャラクターが地面から浮いて見える
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.85f;
            light.shadowBias = 0.03f;
            light.shadowNormalBias = 0.35f;

            var rimGo = new GameObject("Rim Light");
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.intensity = 0.55f;
            rim.color = new Color(0.72f, 0.80f, 1f);
            rimGo.transform.rotation = Quaternion.Euler(20f, 150f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.34f, 0.35f, 0.38f);

            // --- プレイヤー -------------------------------------------
            var player = new GameObject("Mio");
            player.transform.position = new Vector3(-6f, 0f, 0f);

            // ベルトは右（+X）へ進む。最初から横（+X）を向かせて、横顔で立たせる。
            player.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            var controller = player.AddComponent<CharacterController>();
            controller.height = 1.75f;
            controller.radius = 0.30f;
            controller.center = new Vector3(0f, 0.875f, 0f);
            controller.skinWidth = 0.02f;
            controller.slopeLimit = 50f;

            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Visual";
            visual.transform.SetParent(player.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.875f, 0f);
            visual.transform.localScale = new Vector3(0.58f, 0.875f, 0.58f);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.GetComponent<Renderer>().sharedMaterial = bodyMat;

            var arm = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            arm.name = "AttackOrigin";
            arm.transform.SetParent(player.transform, false);
            arm.transform.localPosition = new Vector3(0f, 1.15f, 0.35f);
            arm.transform.localScale = Vector3.one * 0.12f;
            Object.DestroyImmediate(arm.GetComponent<Collider>());

            // 位置の目印としてだけ使う。見た目（赤い球）は出さない。
            Object.DestroyImmediate(arm.GetComponent<Renderer>());

            var stats = player.AddComponent<PlayerStats>();
            var motor = player.AddComponent<PlayerMotor>();
            motor.Stats = stats;
            var combat = player.AddComponent<PlayerCombat>();
            combat.Stats = stats;
            combat.AttackOrigin = arm.transform;

            // 掴まれ・もがき。シーンに保存してあるので、既存シーンでも自動で付く
            player.AddComponent<PlayerGrapple>();

            // VRM の受け口。ファイルを置くだけでカプセルが差し替わる。
            var slot = player.AddComponent<VrmCharacterSlot>();
            slot.Placeholder = visual.transform;
            slot.Mount = player.transform;
            slot.FileName = "mio_v1.vrm";

            // --- カメラ -----------------------------------------------
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 42f;
            cam.nearClipPlane = 0.05f;
            camGo.AddComponent<AudioListener>();

            var camRig = camGo.AddComponent<ThirdPersonCamera>();
            camRig.Target = player.transform;
            motor.CameraTransform = camGo.transform;

            // 固定カメラ（ベルト）。マウスでは回さず、ステージ軸（X）に沿ってだけ追う。
            cam.fieldOfView = 40f;
            camRig.BeltMode = true;
            camRig.BeltPitch = 14f;
            camRig.BeltYaw = 0f;
            camRig.BeltPivotHeight = 0.86f;
            camRig.BeltLookAhead = 1.8f;
            camRig.BeltFollowSharpness = 8f;
            camRig.BeltMinX = -16f;
            camRig.BeltMaxX = 34f;
            camRig.Distance = 4.4f;

            camGo.transform.position = player.transform.position + new Vector3(0f, 3.6f, -7.8f);

            // --- 敵（複数） -------------------------------------------
            var groupGo = new GameObject("Enemy Group");
            var group = groupGo.AddComponent<EnemyGroup>();
            group.MaxAttackers = 2;   // SPEC §5「同時攻撃は最大 2 体まで」

            // 敵はステージの先（+X）から来る。
            var spawnSpots = new[]
            {
                new Vector3(2.5f, 0f,  0.0f),
                new Vector3(5.5f, 0f, -1.1f),
                new Vector3(8.5f, 0f,  1.1f)
            };

            EnemyBrain firstBrain = null;

            for (int i = 0; i < spawnSpots.Length; i++)
            {
                // 見た目は EnemyFigure がプリミティブで組むので、ここは入れ物だけ作る。
                // 座標は足元基準（y=0）に統一。
                var enemy = new GameObject($"Enemy {i + 1}");
                enemy.transform.position = spawnSpots[i];

                float enemyHeight = enemyHeights[i % enemyHeights.Length];
                var enemyClothMat = MakeMaterial(standard, enemyClothColors[i % enemyClothColors.Length]);
                var enemySkinMat = MakeMaterial(standard, enemySkinColors[i % enemySkinColors.Length]);

                var capsule = enemy.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(0f, enemyHeight * 0.5f, 0f);
                capsule.height = enemyHeight;
                capsule.radius = 0.40f;

                var brain = enemy.AddComponent<EnemyBrain>();
                brain.Target = player.transform;
                brain.Player = stats;
                brain.Group = group;

                var figure = enemy.AddComponent<EnemyFigure>();
                figure.Brain = brain;
                figure.Height = enemyHeight;
                figure.BodyMaterial = enemySkinMat;
                figure.ClothMaterial = enemyClothMat;

                var reaction = enemy.AddComponent<EnemyReaction>();
                reaction.Brain = brain;

                if (firstBrain == null)
                    firstBrain = brain;
            }

            // --- 手触り -------------------------------------------------
            var feelGo = new GameObject("Combat Feel");
            var feel = feelGo.AddComponent<CombatFeel>();
            feel.Combat = combat;
            feel.Stats = stats;
            feel.CameraRig = camRig;

            // --- カメラワーク -------------------------------------------
            var director = camGo.AddComponent<CameraDirector>();
            director.Rig = camRig;
            director.Stats = stats;
            director.Combat = combat;

            // --- HUD --------------------------------------------------
            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<MatchHud>();
            hud.Player = stats;
            hud.Enemy = firstBrain;

            // --- 決着（撃破数・次の波） --------------------------------
            var encounterGo = new GameObject("Encounter Director");
            var encounter = encounterGo.AddComponent<EncounterDirector>();
            encounter.Player = player.transform;
            encounter.Camera = camRig;
            // 生きている敵より先へは進めない（走って置き去りにできない）
            var gateGo = new GameObject("Belt Gate");
            var gate = gateGo.AddComponent<BeltScreenLock>();
            gate.Camera = camRig;
            gate.Motor = motor;
            gate.Group = group;
            gate.HalfWidth = 5.0f;
            gate.EdgeMargin = 1.6f;
            gate.GateRange = 3.0f;
            // --- 保存 -------------------------------------------------
            EnsureFolder("Assets/Scenes");
            EnsureFolder("Assets/StreamingAssets");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Ryona] Prototype scene built: {ScenePath}");
        }

        /// <summary>奥の壁と建物。画面の上半分を埋める。</summary>
        static void BuildBackdrop(Material wallMat, Material buildingMat)
        {
            BuildBox("Backdrop Wall", wallMat,
                new Vector3(StageCenterX, 7f, 8.5f), new Vector3(90f, 14f, 0.4f));

            // 高さと幅を変えて、奥行きのある街並みに見せる
            float[] xs = { -16f, -8f, -1f, 6f, 13f, 21f, 29f };
            float[] hs = { 5.0f, 7.5f, 4.2f, 9.0f, 5.5f, 8.0f, 6.5f };
            float[] ws = { 6.0f, 5.0f, 4.0f, 7.0f, 5.0f, 6.0f, 5.0f };

            for (int i = 0; i < xs.Length; i++)
                BuildBox("Building", buildingMat,
                    new Vector3(xs[i], hs[i] * 0.5f, 6.6f),
                    new Vector3(ws[i], hs[i], 1.4f));
        }

        /// <summary>見える箱（縁石・目盛り）。当たり判定は残さない。</summary>
        static void BuildBox(string name, Material mat, Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;

            // 床の目盛りに当たると歩きが引っかかるので、見た目だけにする
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        /// <summary>奥行き（Z）の見えない壁。帯から出られないようにする。</summary>
        static void BuildBound(float z)
        {
            var go = new GameObject("Stage Bound Z");
            go.transform.position = new Vector3(StageCenterX, 0.9f, z);
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(72f, 1.8f, 0.2f);
        }

        /// <summary>ステージ端（X）の見えない壁。行き止まり。</summary>
        static void BuildBoundX(float x)
        {
            var go = new GameObject("Stage Bound X");
            go.transform.position = new Vector3(x, 0.9f, 0f);
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(0.2f, 1.8f, 2f * StageHalfDepth + 1f);
        }

        static Material MakeMaterial(Shader shader, Color color)
        {
            var mat = new Material(shader) { name = "Runtime_" + color.ToString("F2") };
            mat.color = color;
            return mat;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            Directory.CreateDirectory(path);
            AssetDatabase.Refresh();
        }
    }
}
