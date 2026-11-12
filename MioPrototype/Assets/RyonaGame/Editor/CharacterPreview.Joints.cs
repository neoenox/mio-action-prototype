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

    }
}


