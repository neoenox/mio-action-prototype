using System.Collections;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>
    /// 被弾の見た目の回帰テスト。
    ///
    /// 「顔を殴られたら首が振れる」「腹なら上体が折れる」「横からなら上体がねじれる」
    /// 「場所が分からないとき（投げ）はどちらにも倒れない」の 4 点を固定する。
    ///
    /// 同じ穴を 3 回踏んだ（顔の上体ねじれ / 腹のねじれ / 投げの持ち越し）ので、
    /// 数値で押さえておく。敵を待たずに <see cref="CharacterAnimator.PreviewHitZone"/> で
    /// 場所を指定し、<see cref="CharacterAnimator.ForceState"/> で 1 フレーム分の姿勢を作る。
    /// </summary>
    public sealed class HurtPoseRuntimeTests
    {
        CharacterAnimator _anim;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
        }

        IEnumerator LoadCharacter()
        {
            yield return TestScene.LoadPrototype();

            var stats = Object.FindFirstObjectByType<PlayerStats>();
            Assert.IsNotNull(stats, "PlayerStats がシーンに無い");

            // VRM の読み込み後に CharacterAnimator が足されるので、組み上がるまで待つ
            float deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline)
            {
                _anim = stats.GetComponentInChildren<CharacterAnimator>();
                if (_anim != null && _anim.Rig != null && _anim.Rig.Ready)
                    break;
                yield return null;
            }

            Assert.IsTrue(_anim != null && _anim.Rig != null && _anim.Rig.Ready,
                "キャラクターのリグが組み上がらない（VRM の読み込み待ちで時間切れ）");
        }

        /// <summary>被弾の姿勢を 1 回作って、そのときの角度を返す。</summary>
        (float torsoPitch, float torsoYaw, float headYaw) Pose(
            PunchZone? zone, int hurtSide, StaggerTier tier, float time, float duration)
        {
            _anim.PreviewHitZone = zone;
            _anim.PreviewHurtSide(hurtSide);
            _anim.ForceState(CharacterAnimator.State.Stagger, time, duration, tier, log: false);
            return (_anim.DebugTorsoPitch, _anim.DebugTorsoYaw, _anim.DebugHeadYaw);
        }

        [UnityTest]
        public IEnumerator 顔を殴られたら首がカメラ側へ振れ上体は折れない()
        {
            yield return LoadCharacter();

            var face = Pose(PunchZone.Face, 0, StaggerTier.Medium, 0.12f, 0.30f);

            Assert.Greater(Mathf.Abs(face.headYaw), 40f,
                $"顔なのに首が振れていない（headYaw={face.headYaw:F1}）");
            Assert.Less(Mathf.Abs(face.torsoPitch), 32f,
                $"顔なのに上体が折れている（torsoPitch={face.torsoPitch:F1}）＝腹の分岐に入っている");
        }

        [UnityTest]
        public IEnumerator 腹を殴られたら上体が折れて首は振れない()
        {
            yield return LoadCharacter();

            var face = Pose(PunchZone.Face, 0, StaggerTier.Large, 0.08f, 0.60f);
            var body = Pose(PunchZone.Body, 0, StaggerTier.Large, 0.08f, 0.60f);

            Assert.Greater(body.torsoPitch, 50f,
                $"腹なのに上体が折れていない（torsoPitch={body.torsoPitch:F1}）");
            Assert.Less(Mathf.Abs(body.headYaw), Mathf.Abs(face.headYaw) * 0.5f,
                $"腹なのに首が振れている（headYaw={body.headYaw:F1} / 顔={face.headYaw:F1}）");
        }

        [UnityTest]
        public IEnumerator 横から殴られた腹ヒットは上体がねじれる()
        {
            yield return LoadCharacter();

            var front = Pose(PunchZone.Body, 0, StaggerTier.Large, 0.08f, 0.60f);
            var side = Pose(PunchZone.Body, 1, StaggerTier.Large, 0.08f, 0.60f);

            Assert.Greater(Mathf.Abs(side.torsoYaw - front.torsoYaw), 4f,
                $"横からなのに上体がねじれていない（正面={front.torsoYaw:F1} / 横={side.torsoYaw:F1}）");
        }

        [UnityTest]
        public IEnumerator 場所が分からない被弾は腹の折れ方にならない()
        {
            yield return LoadCharacter();

            // 掴み失敗の投げなど、殴られた場所が無いダメージ。
            // 直前のパンチの場所が残っていると、ここが腹の折れ方（上体 70°）になってしまう。
            var body = Pose(PunchZone.Body, 0, StaggerTier.Large, 0.12f, 0.60f);
            var unknown = Pose(null, 0, StaggerTier.Large, 0.12f, 0.60f);

            Assert.Less(unknown.torsoPitch, body.torsoPitch - 15f,
                $"場所が無いのに腹の折れ方になっている（場所なし={unknown.torsoPitch:F1} / 腹={body.torsoPitch:F1}）");
        }
    }
}
