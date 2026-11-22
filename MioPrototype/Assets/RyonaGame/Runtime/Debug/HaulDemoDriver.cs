using System.IO;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// <c>-hauldemo</c> で起動したとき、仰向けとうつ伏せの引き起こしを交互に繰り返す。
    /// 実機で「脇の下から抱え上げて拘束する」流れを確認するためのデモ。
    /// </summary>
    [AddComponentMenu("Ryona/Haul Demo Driver")]
    [DefaultExecutionOrder(30000)]
    public sealed class HaulDemoDriver : MonoBehaviour
    {
        PlayerStats _player;
        GroundHaulUp _haul;
        PlayerGrapple _grapple;
        EnemyBrain _enemy;

        int _phase;
        bool _prone;
        float _nextAction;
        string _shotDir;
        float _nextShot;
        int _shot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!LaunchOptions.Has("-hauldemo"))
                return;

            GroundHaulUp.Forced = true;
            var go = new GameObject("Haul Demo Driver");
            DontDestroyOnLoad(go);
            go.AddComponent<HaulDemoDriver>();
            GameLog.Log("[HaulDemo] start");
        }

        void Start()
        {
            _nextAction = Time.time + 1.5f;
            _shotDir = LaunchOptions.Value("-hauldemoshots");
            _nextShot = Time.time + 0.5f;
        }

        void Update()
        {
            CaptureIfRequested();

            if (_player == null)
            {
                _player = FindFirstObjectByType<PlayerStats>();
                if (_player == null)
                    return;
                _haul = _player.GroundMount != null ? _player.GroundMount.Haul : null;
                _grapple = _player.GetComponent<PlayerGrapple>();
            }

            if (_haul == null || _grapple == null)
                return;

            switch (_phase)
            {
                case 0:
                    if (Time.time >= _nextAction)
                    {
                        ForceDown(_prone);
                        _phase = 1;
                    }
                    break;

                case 1:
                    if (_haul.Active)
                    {
                        _phase = 2;
                        GameLog.Log($"[HaulDemo] haul started prone={_prone}");
                    }
                    break;

                case 2:
                    if (_grapple.IsGrappled)
                    {
                        _phase = 3;
                        _nextAction = Time.time + 2.5f;
                        GameLog.Log($"[HaulDemo] hold kind={_grapple.Kind} prone={_prone}");
                    }
                    break;

                case 3:
                    if (Time.time >= _nextAction)
                    {
                        _grapple.Release();
                        _prone = !_prone;
                        _phase = 4;
                        _nextAction = Time.time + 1.2f;
                    }
                    break;

                case 4:
                    if (Time.time >= _nextAction)
                    {
                        _phase = 0;
                        _nextAction = Time.time + 0.4f;
                    }
                    break;
            }
        }

        void CaptureIfRequested()
        {
            if (string.IsNullOrEmpty(_shotDir) || Time.time < _nextShot)
                return;
            _nextShot = Time.time + 0.1f;
            Directory.CreateDirectory(_shotDir);
            ScreenCapture.CaptureScreenshot(Path.Combine(_shotDir, $"shot_{_shot:000}.png"));
            _shot++;
        }

        void ForceDown(bool prone)
        {
            EnemyBrain best = null;
            float bestDistance = 5f;
            foreach (var enemy in EnemyRegistry.All)
            {
                if (enemy == null || !enemy.isActiveAndEnabled || !enemy.IsAlive)
                    continue;
                float distance = Vector3.Distance(enemy.transform.position, _player.transform.position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = enemy;
                }
            }
            if (best == null)
            {
                _nextAction = Time.time + 1f;
                _phase = 0;
                return;
            }

            _enemy = best;
            Vector3 spot = _player.transform.position + _player.transform.forward * 1.15f;
            best.transform.position = spot;
            best.transform.rotation = Quaternion.LookRotation(
                (_player.transform.position - spot).normalized, Vector3.up);
            best.HoldFire(0f);
            _player.ForceDown(8f, "hauldemo", best.transform, forward: prone);
            GameLog.Log($"[HaulDemo] down prone={prone} at {spot}");
        }

        void OnGUI()
        {
            if (_player == null)
                return;
            string text = _prone ? "HAUL DEMO  うつ伏せ → 前方拘束" : "HAUL DEMO  仰向け → 背後拘束";
            GUI.Box(new Rect(20, 20, 420, 34), text);
            GUI.Label(new Rect(28, 26, 410, 24), "Esc で終了（自動で交互に再生）");
        }
    }
}
