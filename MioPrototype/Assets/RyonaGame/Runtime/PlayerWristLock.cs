using System;
using Ryona;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ryona.Game
{
    /// <summary>背中側で両手首をつなぐ拘束リング。連打で割る短時間の状態異常。</summary>
    [AddComponentMenu("Ryona/Player Wrist Lock")]
    public sealed class PlayerWristLock : MonoBehaviour

    {
        readonly WristLockRules _rules = WristLockRules.Default;
        PlayerStats _stats;
        PlayerGrapple _grapple;
        bool _locked;
        float _elapsed;
        int _inputs;
        float _endedAt = -1f;
        EnemyBrain _attacher;

        public bool IsLocked => _locked;
        internal float Remaining => Mathf.Max(0f, _rules.DurationSeconds - _elapsed);
        internal float RemainingRatio => Remaining / Mathf.Max(0.01f, _rules.DurationSeconds);
        internal int Inputs => _inputs;
        internal int InputsToBreak => _rules.InputsToBreak;
        internal bool InGrace => _rules.IsInGrace(Time.time, _endedAt);
        internal float MoveScale => _locked ? _rules.MoveScale : 1f;
        internal float EscapeNeedScale => _locked || HasGrabPenalty ? _rules.EscapeNeedScale : 1f;

        public event Action Broken;
        static bool _forceTest;
        static string _burstPrefix;

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
            _forceTest = LaunchOptions.Has("-wristlocktest");
            _burstPrefix = LaunchOptions.Value("-wristlockburst");
            var stats = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
            if (stats != null && stats.GetComponent<PlayerWristLock>() == null)
                stats.gameObject.AddComponent<PlayerWristLock>();
            if (stats != null && stats.GetComponent<WristLockVisual>() == null)
                stats.gameObject.AddComponent<WristLockVisual>();
        }


        void Awake()
        {
            _stats = GetComponent<PlayerStats>();
            _grapple = GetComponent<PlayerGrapple>();
        }

        void Update()
        {
            if (!_locked)
            {
                if (_forceTest && Time.time > 2f && _stats != null)
                {
                    foreach (var e in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                    {
                        if (e != null && e.Role == EnemyRole.Tools && TryAttach(e))
                        {
                            _forceTest = false;
                            break;
                        }
                    }
                }
                return;
            }
            _elapsed += Time.deltaTime;
            if (_rules.IsExpired(_elapsed))
                End(false);
        }

        internal bool TryAttach(EnemyBrain attacker)
        {
            if (_locked || _stats == null || _stats.IsDead || _stats.IsDown)
                return false;
            if (!string.IsNullOrEmpty(_burstPrefix))
                StartCoroutine(BurstShots());
            bool grappled = _grapple != null && _grapple.IsGrappled;
            if (!_rules.CanAttach(Time.time, _endedAt, grappled, _stats.IsDown))
                return false;

            _locked = true;
            _elapsed = 0f;
            _inputs = 0;
            _attacher = attacker;
            GameLog.Log($"[WristLock] attached by {(attacker != null ? attacker.name : "?")}");
            return true;
        }

        /// <summary>攻撃入力を割る入力として受ける。バッファには積まない。</summary>
        internal void BreakInput()
        {
            if (!_locked)
                return;
            _inputs++;
            GameLog.Log($"[WristLock] break input {_inputs}/{_rules.InputsToBreak}");
            if (_rules.IsBroken(_inputs))
                End(true);
        }

        /// <summary>掴まれたので輪を外す。必要回数への倍率だけ残す。</summary>
        internal void DetachOnGrab()
        {
            if (!_locked)
                return;
            End(false, keepPenalty: true);
        }

        float _penaltyUntil = -1f;
        internal bool HasGrabPenalty => Time.time < _penaltyUntil;

        void End(bool broken, bool keepPenalty = false)
        {
            _locked = false;
            _endedAt = Time.time;
            _elapsed = 0f;
            _inputs = 0;
            if (keepPenalty)
                _penaltyUntil = Time.time + _rules.GraceSeconds;
            GameLog.Log($"[WristLock] end broken={broken} penalty={keepPenalty}");
            if (broken)
            {
                Broken?.Invoke();
                if (_attacher != null && _attackerNear())
                {
                    var light = AttackLibrary.Light();
                    light.damage = 0f;
                    light.flinch = 12f;
                    _attacher.TakeHit(_stats.State, light);
                }
            }
            _attacher = null;
        }

        System.Collections.IEnumerator BurstShots()
        {
            int index = 0;
            float next = Time.unscaledTime;
            while (index < 120)
            {
                while (Time.unscaledTime < next)
                    yield return null;
                string dir = System.IO.Path.GetDirectoryName(_burstPrefix);
                string name = System.IO.Path.GetFileNameWithoutExtension(_burstPrefix);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"{name}_{index:000}.png"));
                next += 0.05f;
                index++;
            }
        }


        bool _attackerNear() => _attacher != null && _stats != null &&
            Vector3.Distance(_attacher.transform.position, _stats.transform.position) < 2.5f;
    }
}
