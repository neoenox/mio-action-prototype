using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Ryona.Tests
{
    /// <summary>
    /// UI（タイトル / ポーズ / HUD / ステージ終了）と波の進行の回帰テスト。
    ///
    /// これまで <c>-titleshot</c> / <c>-pauseshot</c> / <c>-encountertest</c> の
    /// 目視だけで守っていた領域。ここが壊れると「押しても始まらない」
    /// 「ポーズから戻れない」「クリアしても終わらない」という、
    /// ゲームとして一番痛い壊れ方をする。
    ///
    /// タイトルとポーズは <see cref="RuntimeInitializeOnLoadMethodAttribute"/> で
    /// 起動時に作られるため、シーンを読み直すテストでは存在しない。
    /// テストは自前で作って <c>Awake</c>（UI 構築）から通す。
    /// </summary>
    public sealed class UiRuntimeTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        /// <summary>クリアタイムの保存先。<c>EncounterDirector</c> と同じキー。</summary>
        const string BestKey = "Mio.BestClearSeconds.Normal10.v2";

        [TearDown]
        public void TearDown()
        {
            // テストが落ちてもゲーム全体の時計を戻す
            Time.timeScale = 1f;
        }

        // --- タイトル -----------------------------------------------------------

        [UnityTest]
        public IEnumerator HUDはダウンと腹ダメージの蓄積と残り打数を表示する()
        {
            yield return TestScene.LoadPrototype();

            var hud = Object.FindFirstObjectByType<MatchHud>();
            var player = hud.Player;
            var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            foreach (var enemy in enemies)
                enemy.enabled = false;

            var downFill = Field<Image>(hud, "_downFill");
            var bellyFill = Field<Image>(hud, "_bellyFill");
            var downText = Field<Text>(hud, "_downHitsText");
            var bellyText = Field<Text>(hud, "_bellyHitsText");
            Assert.AreEqual(0f, downFill.fillAmount);
            Assert.AreEqual(0f, bellyFill.fillAmount);
            Assert.AreEqual("弱あと9発 / 強あと4発", downText.text);
            Assert.AreEqual("腹パンあと7発 / 膝あと9発", bellyText.text);

            player.State.AddFlinch(48f);
            player.GetComponent<PlayerGrapple>().Begin(enemies[0], GrappleKind.Back);
            player.LastHitZone = PunchZone.Body;
            player.ReceiveHit(enemies[1].State, AttackLibrary.Light(), enemies[1].transform,
                CombatConstants.HeldBellyPunchDamage);
            yield return null;
            yield return null;

            Assert.AreEqual(0.60f, downFill.fillAmount, 0.001f);
            Assert.AreEqual(0.16f, bellyFill.fillAmount, 0.001f);
            Assert.AreEqual("弱あと4発 / 強あと2発", downText.text);
            Assert.AreEqual("腹パンあと6発 / 膝あと7発", bellyText.text);
            Assert.LessOrEqual(downText.preferredWidth, downText.rectTransform.rect.width);
            Assert.LessOrEqual(bellyText.preferredWidth, bellyText.rectTransform.rect.width);
            Assert.IsFalse(System.Array.Exists(hud.GetComponentsInChildren<Text>(),
                label => label.text == "腕" || label.text == "脚" || label.text == "胴"));
            Assert.IsFalse(System.Array.Exists(hud.GetComponentsInChildren<Text>(),
                label => label.text.Contains("スタミナ") || label.text.Contains("体勢")));
        }

        [UnityTest]
        public IEnumerator タイトルは時間を止めて開始で必ず戻る()
        {
            yield return TestScene.LoadPrototype();
            Assert.IsNull(Object.FindFirstObjectByType<TitleGate>(), "タイトルが残っている");

            var go = new GameObject("Title Gate (test)");
            var gate = go.AddComponent<TitleGate>();
            yield return null;

            Assert.AreEqual(0f, Time.timeScale, 1e-4f, "タイトル中は時間が止まっていない");

            var canvas = go.transform.Find("Title Canvas");
            Assert.IsNotNull(canvas, "タイトルの Canvas が無い");

            var labels = canvas.GetComponentsInChildren<Text>();
            Assert.IsTrue(System.Array.Exists(labels, label => label.text == "MIO"),
                "見出し MIO が無い");
            Assert.IsTrue(System.Array.Exists(labels, label => label.text.Contains("何かキーを押して開始")),
                "開始の案内が無い");
            Assert.IsTrue(System.Array.Exists(labels, label => label.text.Contains("J 弱")),
                "操作一覧が無い");

            gate.Skip();
            yield return null;

            Assert.AreEqual(1f, Time.timeScale, 1e-4f, "開始しても時間が戻っていない");
            Assert.IsTrue(gate == null, "タイトルが消えていない");
        }

        [UnityTest]
        public IEnumerator ホイールで通常時と演出中のカメラをズームできる()
        {
            yield return TestScene.LoadPrototype();
            var camera = Camera.main.GetComponent<ThirdPersonCamera>();
            var settings = InputSystem.settings;
            var background = settings.backgroundBehavior;
            var editorInput = settings.editorInputBehaviorInPlayMode;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                float distance = camera.Distance;
                float depth = Mathf.Abs(camera.transform.position.z);
                const float tick = 1f;
                InputSystem.QueueDeltaStateEvent(mouse.scroll, Vector2.up * tick);
                InputSystem.Update();
                yield return null;

                Assert.Less(camera.Distance, distance, "上に回すと寄る");
                Assert.Less(Mathf.Abs(camera.transform.position.z), depth, "描画位置にも反映される");

                camera.HoldFocus(0.4f, 0f, camera.Target.position.x, 2f);
                yield return null;
                yield return null;
                float focusedDepth = Mathf.Abs(camera.transform.position.z);
                InputSystem.QueueDeltaStateEvent(mouse.scroll, Vector2.down * tick);
                InputSystem.Update();
                yield return null;

                Assert.IsTrue(camera.IsFocusing);
                Assert.That(camera.Distance, Is.EqualTo(distance).Within(0.001f), "下に回すと元の距離に戻る");
                Assert.Greater(Mathf.Abs(camera.transform.position.z), focusedDepth, "演出中も引ける");

                camera.HoldFocus(1f, 0f, 0f, 0f);
                Vector3 shotTarget = camera.Target.position + Vector3.up;
                Vector3 shotPosition = shotTarget + Vector3.back * 2f;
                Call(camera, "RequestCinematicShot", shotPosition, shotTarget, true);
                InputSystem.QueueDeltaStateEvent(mouse.scroll, Vector2.up * tick);
                InputSystem.Update();
                yield return null;
                Assert.That(Vector3.Distance(camera.transform.position, shotTarget), Is.EqualTo(1.8f).Within(0.001f),
                    "マウントの固定ショットも狙いを保ったまま寄る");

                InputSystem.QueueDeltaStateEvent(mouse.scroll, Vector2.down * tick * 40f);
                InputSystem.Update();
                yield return null;
                Assert.That(camera.Distance, Is.EqualTo(camera.MaxDistance));

                InputSystem.QueueDeltaStateEvent(mouse.scroll, Vector2.up * tick * 40f);
                InputSystem.Update();
                yield return null;
                Assert.That(camera.Distance, Is.EqualTo(camera.BeltMinZoomDistance));
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                settings.editorInputBehaviorInPlayMode = editorInput;
                settings.backgroundBehavior = background;
            }
        }

        [UnityTest]
        public IEnumerator 拘束の案内は連打を受け付けるときだけ操作を促す()
        {
            yield return TestScene.LoadPrototype();
            var hud = Object.FindFirstObjectByType<MatchHud>();
            var grapple = hud.Player.GetComponent<PlayerGrapple>();
            var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            foreach (var enemy in enemies)
                enemy.enabled = false;
            grapple.Begin(enemies[0], GrappleKind.Back);
            var label = Field<Text>(hud, "_grappleText");

            grapple.SuppressStruggle(1f);
            yield return null;
            yield return null;
            Assert.That(label.text, Does.Contain("連打は一時停止中"));
            int before = grapple.InputCount;
            grapple.Struggle();
            Assert.AreEqual(before, grapple.InputCount);

            grapple.ClearStruggleSuppression();
            yield return null;
            yield return null;
            Assert.That(label.text, Does.Contain("J / K / L を連打して脱出"));
            grapple.Struggle();
            Assert.Greater(grapple.InputCount, before);
            Assert.LessOrEqual(label.preferredWidth, label.rectTransform.rect.width);
            Assert.LessOrEqual(label.preferredHeight, label.rectTransform.rect.height);
        }

        // --- ポーズ -------------------------------------------------------------

        [UnityTest]
        public IEnumerator ポーズは時間を止めて音量と感度を変える()
        {
            yield return TestScene.LoadPrototype();

            var sfx = ProceduralSfx.Instance;
            Assert.IsNotNull(sfx, "ProceduralSfx が無い");

            var cam = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
            Assert.IsNotNull(cam, "カメラに ThirdPersonCamera が無い");

            // ProceduralSfx は DontDestroyOnLoad なので、テストの変更を持ち越さない
            float volume0 = sfx.MasterVolume;
            float sens0 = cam.MouseSensitivity;

            var go = new GameObject("Pause Menu (test)");
            var pause = go.AddComponent<PauseMenu>();
            yield return null;

            var canvas = go.transform.Find("Pause Canvas");
            Assert.IsNotNull(canvas, "ポーズの Canvas が無い");
            Assert.IsFalse(canvas.gameObject.activeSelf, "ポーズが最初から開いている");

            try
            {
                Call(pause, "Pause");

                Assert.AreEqual(0f, Time.timeScale, 1e-4f, "ポーズで時間が止まっていない");
                Assert.IsTrue(canvas.gameObject.activeSelf, "ポーズ画面が出ていない");

                var volumeLabel = Field<Text>(pause, "_volumeValue");
                var sensLabel = Field<Text>(pause, "_sensValue");
                Assert.IsNotNull(volumeLabel, "音量の表示が無い");
                Assert.IsNotNull(sensLabel, "感度の表示が無い");

                Assert.AreEqual(Percent(volume0), volumeLabel.text, "音量の初期表示が違う");
                Assert.AreEqual($"{sens0:F1}", sensLabel.text, "感度の初期表示が違う");

                Call(pause, "StepVolume", 0.1f);
                float volume1 = Mathf.Clamp01(volume0 + 0.1f);
                Assert.AreEqual(volume1, sfx.MasterVolume, 1e-3f, "音量が変わっていない");
                Assert.AreEqual(Percent(volume1), volumeLabel.text, "音量の表示が古い");

                Call(pause, "StepSensitivity", 0.5f);
                Assert.AreEqual(Mathf.Clamp(sens0 + 0.5f, 0.5f, 8f), cam.MouseSensitivity, 1e-3f,
                    "カメラ感度が変わっていない");

                Call(pause, "Resume");
                Assert.AreEqual(1f, Time.timeScale, 1e-4f, "再開しても時間が戻っていない");
                Assert.IsFalse(canvas.gameObject.activeSelf, "ポーズ画面が閉じていない");
            }
            finally
            {
                sfx.MasterVolume = volume0;
                cam.MouseSensitivity = sens0;
                Time.timeScale = 1f;
            }
        }

        // --- 波の進行 -----------------------------------------------------------

        [UnityTest]
        public IEnumerator 敵が残っている間は次の一団を出さない()
        {
            yield return TestScene.LoadPrototype();

            var director = Object.FindFirstObjectByType<EncounterDirector>();
            var player = Object.FindFirstObjectByType<PlayerStats>();
            var brains = Enemies();

            Assert.IsNotNull(director, "EncounterDirector が無い");
            Assert.IsNotNull(player, "PlayerStats が無い");
            Assert.AreEqual(3, brains.Length, "敵が 3 体ではない");

            Assert.AreEqual(1, director.Wave, "最初の一団が出ていない");
            Assert.AreEqual(3, director.Alive, "最初の一団が 3 体そろっていない");

            // 目の前に湧かない（画面の右端の外から歩いて入る）
            float minX = float.MaxValue;
            foreach (var brain in brains)
                minX = Mathf.Min(minX, brain.transform.position.x);

            Assert.Greater(minX, player.transform.position.x + 1.9f,
                $"敵が目の前に湧いた x={minX:F2}");

            var lethal = Lethal();

            // 1 体だけ倒しても、残りが生きている間は次の一団を出さない
            brains[0].TakeHit(player.State, lethal);
            yield return WaitRealtime(1.2f);

            Assert.AreEqual(1, director.Wave, "敵が残っているのに次の一団が出た");
            Assert.AreEqual(2, director.Alive, "撃破数が反映されていない");

            // 全滅させると、歩いて進めなくても停滞タイマーで次が出る
            director.StallSeconds = 0.3f;
            KillAll(brains, player, lethal);
            Assert.AreEqual(0, director.Alive, "全滅していない");

            float deadline = Time.realtimeSinceStartup + 6f;
            while (director.Wave < 2 && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.AreEqual(2, director.Wave, "全滅後も次の一団が出ない");
            Assert.AreEqual(3, director.Alive, "次の一団が 3 体そろっていない");
        }

        // --- ステージ終了 -------------------------------------------------------

        [UnityTest]
        public IEnumerator 最終波を片付けるとクリア表示とタイムが出る()
        {
            bool hadBest = PlayerPrefs.HasKey(BestKey);
            float oldBest = PlayerPrefs.GetFloat(BestKey, 0f);

            try
            {
                yield return TestScene.LoadPrototype();

                var director = Object.FindFirstObjectByType<EncounterDirector>();
                var hud = Object.FindFirstObjectByType<MatchHud>();
                var player = Object.FindFirstObjectByType<PlayerStats>();

                Assert.IsNotNull(director, "EncounterDirector が無い");
                Assert.IsNotNull(hud, "MatchHud が無い");
                Assert.IsNotNull(player, "PlayerStats が無い");

                var encounterText = Field<Text>(hud, "_encounterText");
                var bannerRoot = Field<RectTransform>(hud, "_bannerRoot");
                var bannerText = Field<Text>(hud, "_bannerText");

                Assert.IsNotNull(encounterText, "撃破数の表示が無い");
                Assert.IsNotNull(bannerRoot, "クリア表示が無い");
                Assert.IsNotNull(bannerText, "クリアの文字が無い");

                yield return null;
                StringAssert.Contains("残り 3", encounterText.text, "撃破数が出ていない");
                Assert.IsFalse(bannerRoot.gameObject.activeSelf, "まだ終わっていないのにクリア表示が出ている");

                // 1 波で終わる条件にして、その一団を片付ける
                director.MaxWaves = 1;
                KillAll(Enemies(), player, Lethal());

                float deadline = Time.realtimeSinceStartup + 4f;
                while (!director.StageCleared && Time.realtimeSinceStartup < deadline)
                    yield return null;

                Assert.IsTrue(director.StageCleared, "1 波片付けたのに終わらない");
                Assert.Greater(director.ClearTimeSeconds, 0f, "クリアタイムが 0 のまま");

                yield return null;   // HUD に反映させる

                Assert.IsTrue(bannerRoot.gameObject.activeSelf, "STAGE CLEAR が出ていない");
                StringAssert.Contains("STAGE CLEAR", bannerText.text, "見出しが違う");
                StringAssert.Contains("タイム", bannerText.text, "タイムが出ていない");
                StringAssert.Contains("全 1 波", encounterText.text, "波の数が出ていない");
            }
            finally
            {
                // テストで本物のベストタイムを書き換えない
                if (hadBest)
                    PlayerPrefs.SetFloat(BestKey, oldBest);
                else
                    PlayerPrefs.DeleteKey(BestKey);

                PlayerPrefs.Save();
            }
        }

        // --- 道具 ---------------------------------------------------------------

        static EnemyBrain[] Enemies() =>
            Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);

        static AttackData Lethal() => new AttackData
        {
            id = "test_lethal",
            kind = AttackKind.Heavy,
            damage = 9999f,
            partDamage = 0f,
            targetPart = BodyPart.Torso
        };

        static void KillAll(EnemyBrain[] brains, PlayerStats player, AttackData lethal)
        {
            foreach (var brain in brains)
            {
                if (brain != null && brain.IsAlive)
                    brain.TakeHit(player.State, lethal);
            }
        }

        static string Percent(float volume) => $"{Mathf.RoundToInt(volume * 100f)} %";

        /// <summary>プライベートメソッドを叩く（既存テストと同じ流儀）。</summary>
        static object Call(object target, string method, params object[] args)
        {
            var info = target.GetType().GetMethod(method, Private);
            Assert.IsNotNull(info, $"{target.GetType().Name}.{method} が見つからない");
            return info.Invoke(target, args);
        }

        /// <summary>プライベートフィールドを読む（UI の実体は実行時に組み立てられる）。</summary>
        static T Field<T>(object target, string name) where T : class
        {
            var info = target.GetType().GetField(name, Private);
            Assert.IsNotNull(info, $"{target.GetType().Name}.{name} が見つからない");
            return info.GetValue(target) as T;
        }

        /// <summary>ヒットストップで時間が止まっても進む待ち。</summary>
        static IEnumerator WaitRealtime(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
                yield return null;
        }
    }
}
