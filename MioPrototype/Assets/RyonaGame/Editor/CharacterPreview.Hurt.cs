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

            // 5 行目: 疲労（撮り比べ用。スタミナ廃止後はゲーム中に自動では出ない）
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
        /// タッグ技（片手打ち）の「打たない腕」の構えを撮り比べる。
        /// 腰の横へ下ろすと上体を前へ倒す腹パンの姿勢と重なって腕がだらりと見えるため、
        /// 胸の前に構える角度を 3x3 で並べて選ぶ。
        /// </summary>
        [MenuItem("Ryona/Diagnostics/Capture Tag Guard Sweep")]
        public static void CaptureTagGuardSweep()
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
            float[] pitches = { -6f, -12f, -20f };
            float[] elbows = { 115f, 128f, 140f };

            const int cellW = 360;
            const int cellH = 520;

            var sheet = new Texture2D(cellW * pitches.Length, cellH * elbows.Length,
                TextureFormat.RGB24, false);

            for (int r = 0; r < elbows.Length; r++)
            {
                for (int c = 0; c < pitches.Length; c++)
                {
                    figure.ForceLeftArm = new Vector3(pitches[c], elbows[r], 2f);
                    // 打つ直前（拳を伸ばし切る手前）で、打たない腕の形を見る
                    figure.Preview(EnemyPhase.Windup, 0.78f, AttackId.Jab, 0f);

                    // 斜め前から。正面だと前腕が胴に隠れて形が読めない。
                    cam.transform.position = new Vector3(h * 1.15f, h * 0.52f, h * 1.45f);
                    cam.transform.LookAt(new Vector3(0f, h * 0.48f, 0f));

                    var cell = RenderCell(cam, cellW, cellH);
                    sheet.SetPixels(c * cellW, (elbows.Length - 1 - r) * cellH, cellW, cellH, cell.GetPixels());
                    Object.DestroyImmediate(cell);

                    Vector3 fist = figure.Fist(true).position;
                    Vector3 local = figureGo.transform.InverseTransformPoint(fist);
                    Debug.Log($"[TagGuard] pitch={pitches[c]:F0} elbow={elbows[r]:F0} " +
                        $"local=({local.x:F2},{local.y:F2},{local.z:F2}) " +
                        $"hipY={figure.HipBone(false).position.y:F2}");
                }
            }

            figure.ForceLeftArm = null;
            sheet.Apply();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(root, "tag_guard_sweep.png");
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(figureGo);
            Object.DestroyImmediate(floor);

            Debug.Log($"[Ryona] tag guard sweep: {outPath}");
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

    }
}


