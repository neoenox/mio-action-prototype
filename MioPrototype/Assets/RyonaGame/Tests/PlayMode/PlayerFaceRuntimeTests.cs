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
        public void 腹パンは顔ヒットより口を開けどちらも目を強くつぶる()
        {
            var face = PlayerFace.DefaultFacePain;
            var belly = PlayerFace.DefaultBellyPain;

            Assert.Greater(belly.Mouth, face.Mouth, "腹パンは口を開けて息を漏らす");
            Assert.Less(belly.Grit, face.Grit, "腹パンは食いしばりを緩める（口を開けるため）");

            // 殴られた瞬間はどちらも反射で目を閉じる。顔ヒットは口を結んだままなので、
            // まぶたが閉じても「眠っている」顔にならないよう眉と食いしばりで作る。
            Assert.GreaterOrEqual(face.Blink, 0.85f, "顔ヒットは目を強くつぶる");
            Assert.GreaterOrEqual(belly.Blink, 0.75f, "腹パンも目を強くつぶる");
            Assert.GreaterOrEqual(face.Angry, 0.75f, "目を閉じても眠って見えないよう眉を寄せる");
        }

        [UnityTest]
        public IEnumerator BearHugOpensEyesBetweenSqueezes() => CheckHeldFace(GrappleKind.BearHug);

        [UnityTest]
        public IEnumerator BackHoldOpensEyesBetweenBodyPunches() => CheckHeldFace(GrappleKind.Back);

        [UnityTest]
        public IEnumerator PinOpensEyesBetweenKneeStrikes() => CheckHeldFace(GrappleKind.Pin);

        IEnumerator CheckHeldFace(GrappleKind kind)
        {
            yield return TestScene.LoadPrototype();
            _stats = Object.FindFirstObjectByType<PlayerStats>();
            var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            foreach (var enemy in enemies)
                enemy.enabled = false;

            PlayerFace face = null;
            float deadline = Time.realtimeSinceStartup + 8f;
            while (face == null && Time.realtimeSinceStartup < deadline)
            {
                face = _stats.GetComponentInChildren<PlayerFace>();
                yield return null;
            }
            Assert.IsNotNull(face);

            var mesh = System.Array.Find(face.GetComponentsInChildren<SkinnedMeshRenderer>(),
                renderer => renderer.sharedMesh != null &&
                    renderer.sharedMesh.GetBlendShapeIndex("Fcl_BRW_Sorrow") >= 0);
            Assert.IsNotNull(mesh);
            int brow = mesh.sharedMesh.GetBlendShapeIndex("Fcl_BRW_Sorrow");
            int eyelids = mesh.sharedMesh.GetBlendShapeIndex("Fcl_EYE_Close");
            int mouth = mesh.sharedMesh.GetBlendShapeIndex("Fcl_MTH_A");
            Assert.GreaterOrEqual(eyelids, 0);
            Assert.GreaterOrEqual(mouth, 0);

            var grapple = _stats.GetComponent<PlayerGrapple>();
            grapple.Begin(enemies[0], kind);
            yield return WaitUnscaled(kind == GrappleKind.Back ? 0.1f : 0.24f);
            Assert.Greater(mesh.GetBlendShapeWeight(brow), 30f, "The hold uses a worried brow.");
            if (kind != GrappleKind.Back)
                Assert.Greater(mesh.GetBlendShapeWeight(brow), 74f, "Anticipation strengthens the worried brow.");
            Assert.Less(mesh.GetBlendShapeWeight(eyelids), 5f, "Eyes stay open between hits.");
            float restingMouth = mesh.GetBlendShapeWeight(mouth);

            if (kind == GrappleKind.BearHug)
                grapple.Squeeze();
            float peakClosed = 0f;
            float peakMouth = 0f;
            bool tookDamage = false;
            deadline = Time.realtimeSinceStartup + 0.55f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                if (!tookDamage && (kind != GrappleKind.BearHug || grapple.IsBearHugContact))
                {
                    _stats.LastHitZone = PunchZone.Body;
                    bool tagPunch = kind == GrappleKind.Back;
                    var attacker = tagPunch ? enemies[1] : enemies[0];
                    float bellyDamage = kind switch
                    {
                        GrappleKind.Back => CombatConstants.HeldBellyPunchDamage,
                        GrappleKind.Pin => CombatConstants.HeldKneeBellyDamage,
                        _ => 0f
                    };
                    var attackKind = kind == GrappleKind.BearHug ? AttackKind.Grapple : AttackKind.Light;
                    _stats.ReceiveHit(attacker.State, new AttackData { kind = attackKind, damage = 1f },
                        attacker.transform, bellyDamage, isTagPunch: tagPunch);
                    tookDamage = true;
                }
                peakClosed = Mathf.Max(peakClosed, mesh.GetBlendShapeWeight(eyelids));
                peakMouth = Mathf.Max(peakMouth, mesh.GetBlendShapeWeight(mouth));
            }
            Assert.IsTrue(tookDamage);
            Assert.Greater(peakClosed, 90f, "The hit closes the eyes.");
            Assert.Greater(peakMouth, restingMouth + 8f, "The hit opens the mouth a little more.");
            Assert.IsTrue(grapple.IsGrappled);
            Assert.Less(mesh.GetBlendShapeWeight(eyelids), 5f, "Eyes reopen after the hit.");

            grapple.Release();
            yield return null;
            yield return null;
            Assert.Less(mesh.GetBlendShapeWeight(brow), 5f, "The worried brow clears on release.");
            Assert.Less(mesh.GetBlendShapeWeight(eyelids), 5f, "Held damage does not leave a punch expression.");
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

        [UnityTest]
        public IEnumerator 殴られたら目を強く閉じ少しして開ける()
        {
            yield return LoadFace();

            Hit(PunchZone.Face);
            yield return WaitUnscaled(0.08f);
            Assert.Greater(_face.Squeeze, 0.9f, "殴られた瞬間に目を強く閉じていない");

            yield return WaitUnscaled(1.6f);
            Assert.Less(_face.Squeeze, 0.1f, "いつまでも目を閉じたまま");
        }

        /// <summary>声を 1 回鳴らして、口の母音と開きの推移を集める。</summary>
        IEnumerator Record(SfxId id, System.Collections.Generic.List<(float t, PlayerFace.Vowel v, float open)> log)
        {
            var sfx = ProceduralSfx.Instance;
            Assert.IsNotNull(sfx, "ProceduralSfx が無い");
            sfx.Play(id, 0.01f, 1f);

            float t = 0f;
            while (t < 1.2f)
            {
                yield return null;
                t += Time.unscaledDeltaTime;
                log.Add((t, _face.VoiceVowel, _face.VoiceOpen));
            }
        }

        [UnityTest]
        public IEnumerator 声あに合わせて口をあの形で開け無音の間は開けない()
        {
            yield return LoadFace();
            var log = new System.Collections.Generic.List<(float t, PlayerFace.Vowel v, float open)>();
            yield return Record(SfxId.HurtLarge, log);

            float early = 0f, peak = 0f;
            PlayerFace.Vowel atPeak = PlayerFace.Vowel.None;
            foreach (var s in log)
            {
                if (s.t < 0.10f)
                    early = Mathf.Max(early, s.open);
                if (s.open > peak)
                {
                    peak = s.open;
                    atPeak = s.v;
                }
            }

            Debug.Log($"[VoiceMouth] large early={early:F2} peak={peak:F2} vowel={atPeak}");
            Assert.Less(early, 0.05f, "声の出だし（無音）で口が開いている");
            Assert.Greater(peak, 0.5f, "声が出ているのに口が開かない");
            Assert.AreEqual(PlayerFace.Vowel.A, atPeak, "「あ」の声なのに口の形が違う");
            Assert.Less(log[log.Count - 1].open, 0.05f, "声が終わっても口が開いたまま");
        }

        [UnityTest]
        public IEnumerator 腹の声おふは口をおからうへ変える()
        {
            yield return LoadFace();
            var log = new System.Collections.Generic.List<(float t, PlayerFace.Vowel v, float open)>();
            yield return Record(SfxId.HurtBelly, log);

            float firstO = -1f, firstU = -1f;
            foreach (var s in log)
            {
                if (s.open < 0.05f)
                    continue;
                if (s.v == PlayerFace.Vowel.O && firstO < 0f)
                    firstO = s.t;
                if (s.v == PlayerFace.Vowel.U && firstU < 0f)
                    firstU = s.t;
            }

            Debug.Log($"[VoiceMouth] belly O at {firstO:F2}s, U at {firstU:F2}s");
            Assert.GreaterOrEqual(firstO, 0f, "「お」の口にならない");
            Assert.Greater(firstU, firstO, "「お」のあと「ふ（う）」の口にならない");
        }

        [UnityTest]
        public IEnumerator 声うは口をうの形にする()
        {
            yield return LoadFace();
            var log = new System.Collections.Generic.List<(float t, PlayerFace.Vowel v, float open)>();
            yield return Record(SfxId.HurtSmall, log);

            float peak = 0f;
            PlayerFace.Vowel atPeak = PlayerFace.Vowel.None;
            foreach (var s in log)
            {
                if (s.open > peak)
                {
                    peak = s.open;
                    atPeak = s.v;
                }
            }

            Assert.Greater(peak, 0.5f, "声が出ているのに口が開かない");
            Assert.AreEqual(PlayerFace.Vowel.U, atPeak, "「う」の声なのに口の形が違う");
        }
    }
}
