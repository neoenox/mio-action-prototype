using Ryona;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ryona.Game
{
    /// <summary>
    /// 手首ロック連動の引き寄せ（道具役 × 打撃役・掴み役の連携）。
    ///
    /// 道具役が背中で両手首に輪を付けると、正面の別の敵が輪を掴みに来て、
    /// 離れた位置（〜<see cref="EnemyBrain.WristPullReach"/>）から手前へ引き込み、
    /// そのまま正面拘束（猶予のあと膝蹴り 5 発）に入る。
    ///
    /// 彼女の選択は二つ:
    /// <list type="bullet">
    /// <item>連打で輪を割る（<see cref="PlayerWristLock.BreakInput"/>）。割れれば引き寄せの前に逃げられる。</item>
    /// <item>引き寄せられる瞬間（掴む前後 <see cref="CounterWindow"/> 秒）に攻撃ボタンを 1 回だけ押して、
    /// 頭突きで押し返す。早すぎる入力はその一回を使い切る（連打していると狙えない）。</item>
    /// </list>
    /// 引き込まれたあとの正面拘束は、輪の分だけ振りほどきに必要な回数が増える
    /// （<see cref="PlayerWristLock.EscapeNeedScale"/>、既存の掴みと同じ仕組み）。
    /// </summary>
    [AddComponentMenu("Ryona/Wrist Pull Director")]
    public sealed class WristPullDirector : MonoBehaviour
    {
        /// <summary>
        /// 引き寄せの構え（輪が付いてから掴む瞬間まで）の秒数。これが輪を割るための猶予になる。
        /// 以前は「0.6 秒待ってから構える」にしていたが、待つ間に正面の敵が自分の押さえつけを
        /// 始めてしまい、引き寄せが一度も出なかった（実測）。輪が付いた直後に構えさせ、構えを長くする。
        /// </summary>
        internal const float PullWindupSeconds = 0.9f;

        /// <summary>頭突きで押し返せる、掴む瞬間の前の幅（秒）。</summary>
        internal const float CounterWindow = 0.16f;

        /// <summary>押し返された敵がよろける秒数。</summary>
        const float CounterStaggerSeconds = 0.9f;

        /// <summary>候補が見つからないときの再試行の間隔（秒）。</summary>
        const float RetrySeconds = 0.4f;

        PlayerStats _stats;
        PlayerGrapple _grapple;
        PlayerWristLock _wrist;
        EnemyBrain _puller;
        float _nextTryAt;
        bool _inputUsed;
        bool _test;
        bool _testCounter;
        bool _testArranged;

        /// <summary>テスト用: いま引き寄せに来ている敵。</summary>
        internal EnemyBrain Puller => _puller;

        /// <summary>テスト用: 引き込まれて正面拘束になった回数。</summary>
        internal int PullsLanded { get; private set; }

        /// <summary>テスト用: 頭突きで押し返した回数。</summary>
        internal int Counters { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Boot();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<WristPullDirector>() != null)
                return;
            var go = new GameObject("Wrist Pull Director");
            var director = go.AddComponent<WristPullDirector>();
            director._test = LaunchOptions.Has("-wristpulltest");
            director._testCounter = LaunchOptions.Has("-wristpullcounter");
        }

        void Update()
        {
            if (!Resolve())
                return;

            if (_puller != null)
            {
                TickPull();
                return;
            }

            if (!_wrist.IsLocked)
                return;

            // 検証用の配置は輪が付いた瞬間に行う（待つ間に別の敵が掴むと引き寄せを撮れない）。
            if (_test && !_testArranged)
            {
                _testArranged = true;
                ArrangeTestPuller();
            }

            if (_grapple.IsGrappled || _grapple.IsHandingOff || _stats.IsDown || _stats.IsDead)
                return;
            if (Time.time < _nextTryAt)
                return;

            TryStartPull();
        }

        bool Resolve()
        {
            if (_stats == null)
            {
                _stats = FindFirstObjectByType<PlayerStats>();
                if (_stats == null)
                    return false;
                _grapple = _stats.GetComponent<PlayerGrapple>();
            }
            if (_wrist == null)
                _wrist = _stats.GetComponent<PlayerWristLock>();
            return _grapple != null && _wrist != null;
        }

        void TickPull()
        {
            if (!_puller.IsPreparingWristPull)
            {
                // 掴む瞬間を過ぎた（掴めた／外れた）か、押し返されて潰れた。
                if (_grapple.IsGrappled && _grapple.Captor == _puller)
                {
                    PullsLanded++;
                    GameLog.Log($"[WristPull] pulled in by {_puller.name} → 正面拘束");
                }
                _puller = null;
                return;
            }

            float left = _puller.WristPullSecondsToStrike;
            bool pressed = InputReader.LightPressed() || InputReader.HeavyPressed() || InputReader.BreakPressed();
            if (_testCounter && !float.IsNaN(left) && left <= CounterWindow * 0.5f)
                pressed = true;
            if (pressed)
                TryCounter();
        }

        /// <summary>
        /// 頭突きで押し返す入力。掴む瞬間の手前 <see cref="CounterWindow"/> 秒以内なら成功。
        /// 一回の引き寄せにつき一度だけ（早すぎた入力でも使い切る）。
        /// </summary>
        internal bool TryCounter()
        {
            if (_puller == null || _inputUsed)
                return false;
            _inputUsed = true;

            float left = _puller.WristPullSecondsToStrike;
            if (float.IsNaN(left) || left > CounterWindow)
            {
                GameLog.Log($"[WristPull] counter too early left={left:F2}");
                return false;
            }

            Counters++;
            GameLog.Log($"[WristPull] headbutt counter left={left:F2} → {_puller.name} よろける");
            _puller.CounterWristPull(CounterStaggerSeconds);
            _puller = null;
            // 押し返した直後にすぐ次が来ないよう、少し間を置く。
            _nextTryAt = Time.time + 1.2f;
            return true;
        }

        void TryStartPull()
        {
            EnemyBrain best = null;
            float bestDistance = float.MaxValue;
            Vector3 her = _stats.transform.position;
            foreach (var e in EnemyRegistry.All)
            {
                if (e == null || !e.isActiveAndEnabled || !e.IsAlive || e.Role == EnemyRole.Tools)
                    continue;
                Vector3 to = e.transform.position - her;
                to.y = 0f;
                float distance = to.magnitude;
                if (distance < CombatConstants.PinMinDistance || distance > EnemyBrain.WristPullReach)
                    continue;
                // 正面から引き込む。背中側の敵は対象外（羽交い締めの領分）。
                if (Vector3.Dot(_stats.transform.forward, to) <= 0f)
                    continue;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = e;
                }
            }

            if (best == null)
            {
                _nextTryAt = Time.time + RetrySeconds;
                return;
            }

            // 自分の通常攻撃の構え（フェイント含む）に入っていたら取り消して、引き寄せに切り替える。
            best.CancelBystanderWindup(0f);
            if (!best.TryStartWristPull(PullWindupSeconds))
            {
                _nextTryAt = Time.time + 0.1f;
                return;
            }

            _puller = best;
            _inputUsed = false;
        }

        /// <summary>検証用: 道具役でない 1 体を彼女の正面 1.9m に立たせ、残りは止める。</summary>
        void ArrangeTestPuller()
        {
            Vector3 her = _stats.transform.position;
            Vector3 forward = _stats.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.right;

            EnemyBrain chosen = null;
            foreach (var e in EnemyRegistry.All)
            {
                if (e == null || !e.isActiveAndEnabled || !e.IsAlive)
                    continue;
                if (chosen == null && e.Role != EnemyRole.Tools)
                {
                    chosen = e;
                    continue;
                }
                // 引き寄せ役以外（輪を付けた道具役も）は止めて遠ざける。
                e.CancelBystanderWindup(0f);
                e.LockForHandOff(12f);
                Vector3 away = e.transform.position - her;
                away.y = 0f;
                if (away.sqrMagnitude < 3.0f * 3.0f)
                    e.transform.position = her - forward * 3.2f + Vector3.right * 0.01f * e.GetInstanceID();
            }
            if (chosen == null)
                return;

            chosen.CancelBystanderWindup(0f);
            Vector3 spot = her + forward * 1.9f;
            spot.y = chosen.transform.position.y;
            chosen.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(-forward, Vector3.up));
            GameLog.Log($"[WristPull] test arranged puller={chosen.name} at 1.9m");
        }
    }
}
