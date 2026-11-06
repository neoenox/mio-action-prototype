using System.Collections;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>
    /// 実シーンを読み込んで、VRM が差し込まれ、モーションが動いているかを確認する。
    /// </summary>
    public sealed class VrmRuntimeTests
    {
        [UnityTest]
        public IEnumerator Vrmが読み込まれリグが組み上がる()
        {
            yield return TestScene.LoadPrototype();

            var slot = Object.FindFirstObjectByType<VrmCharacterSlot>();
            Assert.IsNotNull(slot, "VrmCharacterSlot がシーンに無い");

            float timeout = 20f;
            while (!slot.Loaded && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            Assert.IsTrue(slot.Loaded, $"VRM が読み込まれなかった: {slot.FullPath}");

            var rig = slot.Instance.GetComponent<ProceduralRig>();
            Assert.IsNotNull(rig, "ProceduralRig が付いていない");
            Assert.IsTrue(rig.Ready, "ヒューマノイドボーンが解決できていない");

            var animator = slot.Instance.GetComponent<CharacterAnimator>();
            Assert.IsNotNull(animator, "CharacterAnimator が付いていない");

            // 立ち姿勢は「構え」（拳は顎の前、肘は下がっている）。T ポーズのままではないことを見る。
            // 拳が肩より下にあることを条件にすると構えと矛盾するので、
            // 肘の高さと「拳が体の前にある」で判定する。
            yield return null;
            yield return null;

            float shoulderToHand = Vector3.Distance(rig.LUpperArm.position, rig.LHand.position);
            float shoulderHeight = rig.LUpperArm.position.y;
            Assert.Less(rig.LLowerArm.position.y, shoulderHeight - 0.12f,
                $"肘が下りていない（T ポーズのまま）: elbow={rig.LLowerArm.position.y:F3} shoulder={shoulderHeight:F3}");
            Assert.Greater(rig.LHand.position.z, 0.04f,
                $"拳が体の前（構え）に無い: handZ={rig.LHand.position.z:F3}");
            Assert.Greater(shoulderToHand, 0.05f, "腕の長さが不正");
        }

        [UnityTest]
        public IEnumerator 被弾すると怯み姿勢になりダウンで腰が落ちる()
        {
            yield return TestScene.LoadPrototype();

            var slot = Object.FindFirstObjectByType<VrmCharacterSlot>();
            Assert.IsNotNull(slot);

            float timeout = 20f;
            while (!slot.Loaded && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            Assert.IsTrue(slot.Loaded);

            var stats = Object.FindFirstObjectByType<PlayerStats>();
            var animator = slot.Instance.GetComponent<CharacterAnimator>();
            var rig = slot.Instance.GetComponent<ProceduralRig>();

            Assert.IsNotNull(stats);
            Assert.IsNotNull(animator);
            Assert.IsNotNull(rig);

            yield return new WaitForSeconds(0.2f);
            float hipsIdle = rig.Hips.position.y;

            // ひるみ（ダウンゲージ）が上限に届くと倒れる。
            var hit = AttackLibrary.Heavy();
            hit.damage = 1f;
            for (int i = 0; i < 4 && !stats.IsDown; i++)
                stats.ReceiveHit(new CombatState(), hit);

            yield return null;
            yield return new WaitForSeconds(0.55f);

            Assert.AreEqual(CharacterAnimator.State.Down, animator.Current,
                "ひるみが蓄積してもダウン状態になっていない");
            Assert.Less(rig.Hips.position.y, hipsIdle - 0.1f,
                $"腰が落ちていない: idle={hipsIdle:F3} now={rig.Hips.position.y:F3}");
        }
    }
}
