using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 拘束の「引き渡し（たらい回し）」。
    ///
    /// 3 体で戦っているとき、拘束している敵が別の敵に彼女を引き渡す。
    /// 掴まれたまま相手だけが入れ替わるので、もがきの進捗はやり直しになり
    /// 「抜けても次の敵に捕まる」圧力になる。
    ///
    /// タッグ腹パンのコンボ中は邪魔をしない（演出が終わってから引き渡す）。
    /// </summary>
    [AddComponentMenu("Ryona/Grapple Hand Off")]
    public sealed class GrappleHandOff : MonoBehaviour
    {
        /// <summary>
        /// 引き渡せる距離（これより遠い敵には渡さない）。
        /// 遠すぎると掴み位置までの移動が長くなり、押し出しではなく滑って見える。
        /// </summary>
        public float Range = 2.5f;

        /// <summary>
        /// 腹ダメージが上限未満でコンボを食らい終えたときに、次の敵へ引き渡す確率。
        /// 時間で渡していたころは、腹パンも膝蹴りも出ないうちに渡って「たらい回し」になった。
        /// </summary>
        [Range(0f, 1f)] public float Chance = 0.7f;

        PlayerGrapple _grapple;

        /// <summary>引き渡しの動きを撮る（<c>-handoffburst=&lt;prefix&gt;</c>）。1 回だけ。</summary>
        string _burstPrefix;
        bool _burstDone;

        /// <summary>抽選を固定する（<c>-handoffkind=pin</c> / <c>-handoffkind=back</c>）。検証用。</summary>
        static string _forceKind;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InstallSceneHook()
        {
            // AfterSceneLoad は最初のシーンだけ。"R ではじめから" の再読み込みでも
            // 引き渡しが戻るように、毎回確認する。
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
            UnityEngine.SceneManagement.LoadSceneMode mode) => Boot();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<GrappleHandOff>() != null)
                return;

            string burst = null;
            string kind = null;
            foreach (string a in System.Environment.GetCommandLineArgs())
            {
                if (a.StartsWith("-handoffburst=", System.StringComparison.OrdinalIgnoreCase))
                    burst = a.Substring("-handoffburst=".Length);
                else if (a.StartsWith("-handoffkind=", System.StringComparison.OrdinalIgnoreCase))
                    kind = a.Substring("-handoffkind=".Length).Trim().ToLowerInvariant();
            }

            _forceKind = kind;
            var go = new GameObject("Grapple Hand Off");
            var comp = go.AddComponent<GrappleHandOff>();
            comp._burstPrefix = burst;
        }

        System.Collections.IEnumerator BurstShots()
        {
            const int count = 40;      // 2.0 秒
            const float interval = 0.05f;
            string dir = System.IO.Path.GetDirectoryName(_burstPrefix);
            string name = System.IO.Path.GetFileNameWithoutExtension(_burstPrefix);

            float next = Time.unscaledTime;
            for (int i = 0; i < count; i++)
            {
                while (Time.unscaledTime < next)
                    yield return null;

                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"{name}_{i:00}.png"));
                next += interval;
            }
            Debug.Log($"[HandOff] burst {count} 枚 {_burstPrefix}_XX.png");
        }

        /// <summary>
        /// コンボ後に通常の構図へ戻ってから、次の敵へ渡すか抽選する。
        /// 渡せた場合は true。渡せない場合は元の拘束が続く。
        /// </summary>
        public bool TryChain()
        {
            if (_grapple == null)
                _grapple = FindFirstObjectByType<PlayerGrapple>();
            if (_grapple == null || !_grapple.IsGrappled)
                return false;

            bool roll = Random.value < Mathf.Clamp01(Chance);
            bool handed = roll && TryHandOff();
            Debug.Log($"[HandOff] roll chance={Chance:F2} → " +
                      (handed ? "渡した" : roll ? "渡し先なし（拘束継続）" : "渡さない（拘束継続）"));
            return handed;
        }

        bool TryHandOff()
        {
            var captor = _grapple.Captor;
            if (captor == null)
                return false;

            EnemyBrain best = null;
            float bestDistance = Range;

            foreach (var enemy in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (enemy == null || enemy == captor || !enemy.IsAlive || enemy.IsRising)
                    continue;
                if (enemy.Phase == EnemyPhase.Windup || enemy.Phase == EnemyPhase.Attack ||
                    enemy.Phase == EnemyPhase.Stagger || enemy.IsHolding)
                    continue;

                float d = Vector3.Distance(enemy.transform.position, _grapple.transform.position);
                if (d >= bestDistance)
                    continue;

                best = enemy;
                bestDistance = d;
            }

            if (best == null)
                return false;

            // 抽選: 正面拘束（膝蹴り派生）か背面拘束（羽交い締め→腹パン）か。
            // 検証時は -handoffkind で固定できる（撮り比べ用）。
            GrappleKind kind;
            if (_forceKind == "pin")
                kind = GrappleKind.Pin;
            else if (_forceKind == "back")
                kind = GrappleKind.Back;
            else
                kind = Random.value < 0.5f ? GrappleKind.Pin : GrappleKind.Back;

            // 押し出す側（いま掴んでいる敵）に腕を突き出させる
            captor.PushAway();

            if (_burstPrefix != null && !_burstDone)
            {
                _burstDone = true;
                StartCoroutine(BurstShots());
            }

            _grapple.HandOff(best, kind, shove: true);
            best.TakeOverHold(CombatConstants.GrappleLimitSeconds + 4f);
            Debug.Log($"[HandOff] {captor.name} → {best.name} kind={kind} dist={bestDistance:F2}");
            return true;
        }
    }
}
