using System.Collections;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>
    /// 被弾の表情の回帰テスト。
    ///
    /// 腹パン（ボディーブロー）は顔ヒットと別の配合にする。
    /// 顔は「口を結んで耐える」、腹は「口を開けて息を漏らし、目を強くつぶる」。
    /// 場所を取り違えると、腹パンが軽く小突かれた顔になる（5 連続の腹パンが効かない）。
    ///
    /// VRM を待たずに <see cref="PlayerFace"/> だけを PlayerStats に足して測る。
    /// 配合の計算は Vrm に依存しないので、これで場所の分岐だけを固定できる。
    /// </summary>
    public sealed class PlayerFaceRuntimeTests
    {
        PlayerStats _stats;
        PlayerFace _face;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            if (_face != null)
                Object.Destroy(_face);
        }

        IEnumerator LoadFace()
        {
            yield return TestScene.LoadPrototype();

            _stats = Object.FindFirstObjectByType<PlayerStats>();
            Assert.IsNotNull(_stats, "PlayerStats がシーンに無い");

            _face = _stats.gameObject.AddComponent<PlayerFace>();
            _face.Stats = _stats;

            // AddComponent の時点では Stats が未設定で購読していない。
            // 有効化し直して OnEnable を Stats 付きで通す。
            _face.enabled = false;
            _face.enabled = true;

            yield return null;
        }

        /// <summary>1 発当てる。実機と同じく、先に場所を立ててから ReceiveHit を呼ぶ。</summary>
        void Hit(PunchZone zone)
        {
            var attack = new AttackData
            {
                id = "face_test",
                kind = AttackKind.Light,
                damage = 1f,
                postureDamage = 0f,
                partDamage = 1f,
                targetPart = BodyPart.Torso,
                startupSeconds = 0f,
                activeSeconds = 0f,
                recoverySeconds = 0f
            };

            _stats.LastHitZone = zone;
            _stats.ReceiveHit(new CombatState(), attack);
        }

        IEnumerator WaitUnscaled(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        [Test]
        public void 腹パンの配合は顔ヒットより口を開け目を強くつぶる()
        {
            var face = PlayerFace.DefaultFacePain;
            var belly = PlayerFace.DefaultBellyPain;

            Assert.Greater(belly.Mouth, face.Mouth, "腹パンは口を開けて息を漏らす");
            Assert.Greater(belly.Blink, face.Blink, "腹パンは目を強くつぶる");
            Assert.Less(belly.Grit, face.Grit, "腹パンは食いしばりを緩める（口を開けるため）");
        }

        [UnityTest]
        public IEnumerator 腹パンで表情が腹パン用の配合に切り替わる()
        {
            yield return LoadFace();

            Assert.AreEqual(0f, _face.BellyBlend, 0.01f, "最初は顔ヒット用の配合");

            Hit(PunchZone.Body);
            yield return WaitUnscaled(0.2f);

            Assert.AreEqual(1f, _face.BellyBlend, 0.05f, "腹パンで腹パン用の配合へ寄っていない");
            Assert.AreEqual(_face.BellyPain.Mouth, _face.CurrentShape.Mouth, 0.02f,
                "口の開きが腹パン用になっていない");
        }

        [UnityTest]
        public IEnumerator 腹パンのあと顔を殴られたら顔ヒット用の配合に戻る()
        {
            yield return LoadFace();

            Hit(PunchZone.Body);
            yield return WaitUnscaled(0.2f);
            Assert.AreEqual(1f, _face.BellyBlend, 0.05f, "腹パンで腹パン用の配合へ寄っていない");

            Hit(PunchZone.Face);
            yield return WaitUnscaled(0.5f);

            Assert.Less(_face.BellyBlend, 0.05f, "顔ヒットのあと腹パン用の配合が残っている");
            Assert.AreEqual(_face.FacePain.Mouth, _face.CurrentShape.Mouth, 0.02f,
                "口の開きが顔ヒット用に戻っていない");
        }
    }
}
