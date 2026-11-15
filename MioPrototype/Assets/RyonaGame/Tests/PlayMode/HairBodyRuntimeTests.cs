using System.Collections;
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
    /// 髪ジョイントがいくつ内側にあるかと、その最深値を出す。
    /// </summary>
    public sealed class HairBodyRuntimeTests
    {
        [UnityTest]
        public IEnumerator 立ち姿勢で髪のジョイントが体のコライダーに入っていない()
        {
            yield return TestScene.LoadPrototype();

            Vrm10Instance vrm = null;
            float deadline = Time.realtimeSinceStartup + 25f;
            while (Time.realtimeSinceStartup < deadline)
            {
                var found = Object.FindObjectsByType<Vrm10Instance>(FindObjectsSortMode.None);
                foreach (var candidate in found)
                {
                    if (candidate.GetComponent<CharacterAnimator>() != null &&
                        candidate.GetComponent<CharacterAnimator>().Rig != null &&
                        candidate.GetComponent<CharacterAnimator>().Rig.Ready)
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
            for (int i = 0; i < 60; i++)
                yield return null;

            var allJoints = vrm.GetComponentsInChildren<VRM10SpringBoneJoint>(true);
            var colliders = vrm.GetComponentsInChildren<VRM10SpringBoneCollider>(true);
            var joints = new System.Collections.Generic.List<VRM10SpringBoneJoint>();
            foreach (var joint in allJoints)
            {
                if (joint != null && joint.name.IndexOf("HairJoint", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    joints.Add(joint);
            }

            Assert.Greater(joints.Count, 0, "髪のジョイントが無い");
            Assert.Greater(colliders.Length, 0, "体のコライダーが無い");

            int inside = 0;
            int insideMargin = 0;
            float deepest = 0f;
            string worstJoint = "-";
            string worstCollider = "-";
            var marginByCollider = new System.Collections.Generic.Dictionary<string, int>();
            var marginJoints = new System.Collections.Generic.List<string>();

            foreach (var joint in joints)
            {
                Vector3 p = joint.transform.position;
                bool hit = false;
                bool hitMargin = false;

                foreach (var col in colliders)
                {
                    if (col == null || !col.isActiveAndEnabled)
                        continue;

                    if (TryPenetration(col, p, out float depth))
                    {
                        hit = true;
                        if (depth > deepest)
                        {
                            deepest = depth;
                            worstJoint = joint.name;
                            worstCollider = col.name;
                        }
                    }

                    // 毛束の板がめり込む分の余裕（2cm）を足しても内側か。
                    if (TryPenetration(col, p, out _, margin: 0.02f))
                    {
                        hitMargin = true;
                        marginByCollider.TryGetValue(col.name, out int n);
                        marginByCollider[col.name] = n + 1;
                    }
                }

                if (hit) inside++;
                if (hitMargin)
                {
                    insideMargin++;
                    marginJoints.Add(joint.name);
                }
            }

            var byColliderText = string.Join(", ",
                System.Linq.Enumerable.Select(marginByCollider,
                    kv => $"{kv.Key}:{kv.Value}"));

            Debug.Log($"[HairAudit] joints={joints.Count} colliders={colliders.Length} " +
                      $"inside={inside} inside+2cm={insideMargin} deepest={deepest:F3} " +
                      $"joint={worstJoint} collider={worstCollider} " +
                      $"byCollider=[{byColliderText}] " +
                      $"marginJoints=[{string.Join(",", marginJoints)}]");

            // 体のコライダーは VrmHairBodyMargin が広げている。広げたぶんを戻して
            // 「実際の体の表面」に対して髪メッシュが入っているかを見る。
            var marginComponent = vrm.GetComponent<VrmHairBodyMargin>();
            float bodyShrink = marginComponent != null ? marginComponent.Margin : 0f;

            // 毛束のメッシュ（板）自体が体のコライダーに入っていないか。
            // 頭・首は根元なので除き、胴・腕・脚だけを見る。
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
                        mat.name.IndexOf("Hair", System.StringComparison.OrdinalIgnoreCase) < 0)
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
                            if (bone.IndexOf("Head", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                                bone.IndexOf("Neck", System.StringComparison.OrdinalIgnoreCase) >= 0)
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

                Object.Destroy(baked);
            }

            Debug.Log($"[HairMeshAudit] vertices={meshTotal} inside={meshInside} " +
                      $"deepest={meshDeepest:F3} worst={meshWorst}");

            // まずは数値の記録が目的。体のコライダーに 1 本も入っていなければ、
            // 見えている貫通は毛束メッシュ側（ジョイント外側）の問題。
            Assert.AreEqual(0, inside,
                $"髪のジョイントが体のコライダーに入っている（{inside}/{joints.Count}、最深 {deepest:F3}m）");

            // 毛束は完全な剛体ではないので、サブミリのめり込みは揺れの誤差。
            // 見て分かる貫通（数 mm 以上）だけを失敗にする。
            const float AllowedPenetration = 0.003f;
            Assert.LessOrEqual(meshDeepest, AllowedPenetration,
                $"髪メッシュが体に深く入っている（最深 {meshDeepest:F3}m、{meshWorst}、" +
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
