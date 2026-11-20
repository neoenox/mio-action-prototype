using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Ryona.Game;
using UniVRM10;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>
    /// プレイヤーの髪が体の内側へ入っていないかを測る。
    ///
    /// 見た目の「髪が体を貫通している」は、ジョイントが体のコライダーに入っているのか、
    /// ジョイントは外でも毛束のメッシュ（板）がめり込んでいるのかで直し方が変わる。
    /// ここでは VRM のスプリングボーン用コライダー（胴・腕・脚）に対して、
    /// 髪ジョイントと髪メッシュ頂点がどこまで入っているかを出す。
    /// </summary>
    public sealed class HairBodyRuntimeTests
    {
        const int SettleFrames = 60;

        /// <summary>毛束は完全な剛体ではないので、サブミリのめり込みは揺れの誤差として許す。</summary>
        const float AllowedPenetration = 0.003f;

        [UnityTest]
        public IEnumerator 立ち姿勢で髪のジョイントが体のコライダーに入っていない()
        {
            Vrm10Instance vrm = null;
            yield return LoadVrm(v => vrm = v);
            Audit(vrm, "idle");
        }

        [UnityTest]
        public IEnumerator ダウン中も髪が体に深く入らない()
        {
            Vrm10Instance vrm = null;
            yield return LoadVrm(v => vrm = v);

            var stats = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
            var anim = vrm.GetComponent<CharacterAnimator>();
            Assert.IsNotNull(stats, "PlayerStats が無い");
            Assert.IsNotNull(anim, "CharacterAnimator が無い");

            // うつ伏せに倒す。髪の重力は VrmHairSpring が弱めるが、
            // 毛束が体の上に落ちるので、立ち姿勢とは別に測る。
            stats.ForceDown(6f, "hair-down", launchDistance: 0f, forward: true);

            float deadline = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < deadline && anim.Current != CharacterAnimator.State.Down)
                yield return null;

            Assert.AreEqual(CharacterAnimator.State.Down, anim.Current, "ダウン姿勢に入らない");

            for (int i = 0; i < SettleFrames; i++)
                yield return null;

            Audit(vrm, "down");
        }

        static IEnumerator LoadVrm(Action<Vrm10Instance> onLoaded)
        {
            yield return TestScene.LoadPrototype();

            Vrm10Instance vrm = null;
            float deadline = Time.realtimeSinceStartup + 25f;
            while (Time.realtimeSinceStartup < deadline)
            {
                var found = UnityEngine.Object.FindObjectsByType<Vrm10Instance>(FindObjectsSortMode.None);
                foreach (var candidate in found)
                {
                    var anim = candidate.GetComponent<CharacterAnimator>();
                    if (anim != null && anim.Rig != null && anim.Rig.Ready)
                    {
                        vrm = candidate;
                        break;
                    }
                }
                if (vrm != null)
                    break;
                yield return null;
            }

            Assert.IsNotNull(vrm, "プレイヤーの VRM が読み込まれない");

            // 髪の物理が落ち着くまで待つ。
            for (int i = 0; i < SettleFrames; i++)
                yield return null;

            onLoaded(vrm);
        }

        static void Audit(Vrm10Instance vrm, string label)
        {
            var allJoints = vrm.GetComponentsInChildren<VRM10SpringBoneJoint>(true);
            var colliders = vrm.GetComponentsInChildren<VRM10SpringBoneCollider>(true);
            var joints = new List<VRM10SpringBoneJoint>();
            foreach (var joint in allJoints)
            {
                if (joint != null && joint.name.IndexOf("HairJoint", StringComparison.OrdinalIgnoreCase) >= 0)
                    joints.Add(joint);
            }

            Assert.Greater(joints.Count, 0, "髪のジョイントが無い");
            Assert.Greater(colliders.Length, 0, "体のコライダーが無い");

            // 体のコライダーは VrmHairBodyMargin が広げている。広げたぶんを戻して
            // 「実際の体の表面」に対して髪が入っているかを見る。
            var marginComponent = vrm.GetComponent<VrmHairBodyMargin>();
            float bodyShrink = marginComponent != null ? marginComponent.Margin : 0f;

            int jointInside = 0;
            float jointDeepest = 0f;
            string jointWorst = "-";

            foreach (var joint in joints)
            {
                Vector3 p = joint.transform.position;
                foreach (var col in colliders)
                {
                    if (col == null || !col.isActiveAndEnabled)
                        continue;
                    if (!TryPenetration(col, p, out float depth, margin: -bodyShrink))
                        continue;

                    jointInside++;
                    if (depth > jointDeepest)
                    {
                        jointDeepest = depth;
                        jointWorst = $"{joint.name}/{col.name}";
                    }
                }
            }

            int meshInside = 0;
            int meshTotal = 0;
            float meshDeepest = 0f;
            string meshWorst = "-";

            foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr == null || smr.sharedMesh == null)
                    continue;

                var baked = new Mesh();
                smr.BakeMesh(baked, true);
                Vector3[] vertices = baked.vertices;
                Matrix4x4 toWorld = smr.localToWorldMatrix;

                // 1 つの SkinnedMeshRenderer に体・服・髪が同居している（Body メッシュ）。
                // 髪のサブメッシュだけを見る。体の頂点を「髪」と数えない。
                for (int sub = 0; sub < baked.subMeshCount && sub < smr.sharedMaterials.Length; sub++)
                {
                    var mat = smr.sharedMaterials[sub];
                    if (mat == null ||
                        mat.name.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    int[] indices = baked.GetIndices(sub);
                    for (int k = 0; k < indices.Length; k++)
                    {
                        int vi = indices[k];
                        if (vi < 0 || vi >= vertices.Length)
                            continue;

                        Vector3 p = toWorld.MultiplyPoint3x4(vertices[vi]);
                        meshTotal++;

                        foreach (var col in colliders)
                        {
                            if (col == null || !col.isActiveAndEnabled)
                                continue;

                            string bone = col.transform.name;
                            if (bone.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                bone.IndexOf("Neck", StringComparison.OrdinalIgnoreCase) >= 0)
                                continue;

                            if (!TryPenetration(col, p, out float depth, margin: -bodyShrink))
                                continue;

                            meshInside++;
                            if (depth > meshDeepest)
                            {
                                meshDeepest = depth;
                                meshWorst = $"{smr.name}[{sub}]/{col.name}";
                            }
                        }
                    }
                }

                UnityEngine.Object.Destroy(baked);
            }

            Debug.Log($"[HairAudit:{label}] joints={joints.Count} colliders={colliders.Length} " +
                      $"inside={jointInside} deepest={jointDeepest:F3} joint={jointWorst}");
            Debug.Log($"[HairMeshAudit:{label}] vertices={meshTotal} inside={meshInside} " +
                      $"deepest={meshDeepest:F3} worst={meshWorst}");

            Assert.AreEqual(0, jointInside,
                $"[{label}] 髪のジョイントが体のコライダーに入っている" +
                $"（{jointInside}/{joints.Count}、最深 {jointDeepest:F3}m、{jointWorst}）");

            Assert.LessOrEqual(meshDeepest, AllowedPenetration,
                $"[{label}] 髪メッシュが体に深く入っている（最深 {meshDeepest:F3}m、{meshWorst}、" +
                $"inside={meshInside}/{meshTotal}）");
        }

        /// <summary>コライダーの内側なら、表面までの深さ（正）を返す。</summary>
        static bool TryPenetration(VRM10SpringBoneCollider col, Vector3 point, out float depth,
            float margin = 0f)
        {
            depth = 0f;
            float scale = col.transform.lossyScale.x;
            float radius = Mathf.Max(0f, col.Radius * scale) + margin;

            switch (col.ColliderType)
            {
                case VRM10SpringBoneColliderTypes.Sphere:
                case VRM10SpringBoneColliderTypes.SphereInside:
                {
                    Vector3 center = col.transform.TransformPoint(col.Offset);
                    float d = Vector3.Distance(point, center);
                    depth = radius - d;
                    return depth > 0f;
                }

                case VRM10SpringBoneColliderTypes.Capsule:
                case VRM10SpringBoneColliderTypes.CapsuleInside:
                {
                    Vector3 a = col.transform.TransformPoint(col.Offset);
                    Vector3 b = col.transform.TransformPoint(col.Tail);
                    Vector3 ab = b - a;
                    float t = ab.sqrMagnitude > 1e-8f
                        ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude)
                        : 0f;
                    Vector3 closest = a + ab * t;
                    float d = Vector3.Distance(point, closest);
                    depth = radius - d;
                    return depth > 0f;
                }

                default:
                    return false;
            }
        }
    }
}
