using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Ryona.Game
{
    /// <summary>
    /// 実機（ビルド済みプレイヤーを含む）で各種モーションを手早く切り替えて確認するためのショートカット。
    /// 数字キーで番号を入力し、<c>Enter</c> キーで該当モーションへ直接派生する。
    /// </summary>
    [AddComponentMenu("Ryona/Motion Shortcut")]
    [DefaultExecutionOrder(25000)]
    public sealed class MotionShortcut : MonoBehaviour
    {
        const int MaxShortcutId = 15;

        struct ShortcutEntry
        {
            public int Id;
            public string Label;
        }

        static readonly ShortcutEntry[] Entries =
        {
            new ShortcutEntry { Id = 1,  Label = "羽交い締め → 腹パン連撃" },
            new ShortcutEntry { Id = 2,  Label = "羽交い締め → 顔パン連撃" },
            new ShortcutEntry { Id = 3,  Label = "正面拘束（膝蹴り）" },
            new ShortcutEntry { Id = 4,  Label = "壁押さえつけ（膝→2人→3人）" },
            new ShortcutEntry { Id = 5,  Label = "ベアハッグ（正面抱え締め）" },
            new ShortcutEntry { Id = 6,  Label = "足取り（足首掴み→転倒）" },
            new ShortcutEntry { Id = 7,  Label = "引き起こし（仰向け→羽交い締め）" },
            new ShortcutEntry { Id = 8,  Label = "引き起こし（うつ伏せ→正面拘束）" },
            new ShortcutEntry { Id = 9,  Label = "マウントパンチ（仰向け）" },
            new ShortcutEntry { Id = 10, Label = "マウント（うつ伏せ寝返り→殴り）" },
            new ShortcutEntry { Id = 11, Label = "プレイヤー攻撃（弱3段コンボ）" },
            new ShortcutEntry { Id = 12, Label = "プレイヤー攻撃（強・崩し）" },
            new ShortcutEntry { Id = 13, Label = "食らい（腹・顔・強怯み）" },
            new ShortcutEntry { Id = 14, Label = "ダウン単体（うつ伏せ→起き上がり）" },
            new ShortcutEntry { Id = 15, Label = "ダウン単体（仰向け吹き飛び→起き上がり）" },
        };

        PlayerStats _player;
        PlayerCombat _combat;
        PlayerMotor _motor;
        PlayerGrapple _grapple;
        CharacterAnimator _anim;
        HeldBellyPunchDirector _belly;
        ThirdPersonCamera _camera;
        BeltScreenLock _screenLock;

        bool _defaultBellyValuesCaptured;
        float _defaultFacePunchChance = 0.5f;
        float _defaultThrowDelaySeconds = 1.6f;
        float _defaultHandOffChance = 0.35f;

        string _inputBuffer = string.Empty;
        int _lastId = -1;
        bool _freezeAi;
        bool _allowActiveEnemiesAi;
        bool _collapsed;

        EnemyBrain _activeTarget;
        EnemyBrain _activeSubTarget;
        EnemyBrain _activeThirdTarget;
        Coroutine _sequence;

        // 起動引数で指定した番号を順に再生する（実機の通し確認用）。
        static int[] _autoIds;
        static float _autoInterval = 3.5f;
        static string _autoBurstPrefix;
        static int _autoBurstFrames = 700;
        static float _autoBurstInterval = 0.05f;
        static bool _autoParsed;

        RectTransform _panelRect;
        Text _bodyText;
        string _lastRenderedText;
        bool _built;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InstallSceneHook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Ensure();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => Ensure();

        static void ParseAutoOptions()
        {
            if (_autoParsed)
                return;
            _autoParsed = true;

            string list = LaunchOptions.Value("-motionseq");
            if (!string.IsNullOrEmpty(list))
            {
                string[] parts = list.Split(',');
                var ids = new List<int>(parts.Length);
                foreach (string part in parts)
                {
                    if (int.TryParse(part.Trim(), out int id) && id >= 1 && id <= MaxShortcutId)
                        ids.Add(id);
                }
                _autoIds = ids.Count > 0 ? ids.ToArray() : null;
            }

            if (LaunchOptions.TryFloat("-motionseqinterval", out float interval) && interval > 0.5f)
                _autoInterval = interval;
            if (LaunchOptions.TryInt("-motionseqburstframes", out int frames) && frames > 0)
                _autoBurstFrames = frames;
            if (LaunchOptions.TryFloat("-motionseqburstinterval", out float burstInterval) && burstInterval > 0f)
                _autoBurstInterval = burstInterval;
            _autoBurstPrefix = LaunchOptions.Value("-motionseqburst");
        }

        static void Ensure()
        {
            ParseAutoOptions();
            if (FindFirstObjectByType<MotionShortcut>() != null)
                return;

            if (FindFirstObjectByType<PlayerStats>() == null)
                return;

            var go = new GameObject("Motion Shortcut");
            go.AddComponent<MotionShortcut>();
        }

        void Awake()
        {
            BuildUi();
            if (_autoIds != null && _autoIds.Length > 0)
                StartCoroutine(RunAutoSequence());
            if (!string.IsNullOrEmpty(_autoBurstPrefix))
                StartCoroutine(RunAutoBurst());
            ResolveReferences();
            RefreshUiText();
        }

        System.Collections.IEnumerator RunAutoSequence()
        {
            yield return new WaitForSecondsRealtime(1f);
            GameLog.Log($"[Shortcut] auto sequence start count={_autoIds.Length} interval={_autoInterval:F1}");
            for (int i = 0; i < _autoIds.Length; i++)
            {
                ExecuteShortcut(_autoIds[i]);
                GameLog.Log($"[Shortcut] auto {i + 1}/{_autoIds.Length} id={_autoIds[i]}");
                yield return new WaitForSecondsRealtime(_autoInterval);
            }
            GameLog.Log("[Shortcut] auto sequence done");
        }

        System.Collections.IEnumerator RunAutoBurst()
        {
            string dir = System.IO.Path.GetDirectoryName(_autoBurstPrefix);
            string name = System.IO.Path.GetFileNameWithoutExtension(_autoBurstPrefix);
            float next = Time.unscaledTime;
            for (int i = 0; i < _autoBurstFrames; i++)
            {
                while (Time.unscaledTime < next)
                    yield return null;
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"{name}_{i:000}.png"));
                next += _autoBurstInterval;
            }
        }


        void Update()
        {
            if (!_built)
                BuildUi();

            if (_player == null)
                ResolveReferences();

            if (_freezeAi)
                KeepIdleEnemiesFrozen();

            if (!PauseMenu.IsPaused && Time.timeScale > 0f)
                HandleKeyboardInput();

            RefreshUiText();
        }

        void ResolveReferences()
        {
            if (_player == null)
                _player = FindFirstObjectByType<PlayerStats>();
            if (_player == null)
                return;

            _combat = _player.GetComponent<PlayerCombat>();
            _motor = _player.GetComponent<PlayerMotor>();
            _grapple = _player.GetComponent<PlayerGrapple>();
            _anim = _player.GetComponentInChildren<CharacterAnimator>();
            _belly = FindFirstObjectByType<HeldBellyPunchDirector>();
            _screenLock = FindFirstObjectByType<BeltScreenLock>();

            if (_belly != null && !_defaultBellyValuesCaptured)
            {
                _defaultFacePunchChance = _belly.FacePunchChance;
                _defaultThrowDelaySeconds = _belly.ThrowDelaySeconds;
                var handOff = FindFirstObjectByType<GrappleHandOff>();
                if (handOff != null)
                    _defaultHandOffChance = handOff.Chance;
                _defaultBellyValuesCaptured = true;
            }

            if (_camera == null)
            {
                var cam = Camera.main;
                _camera = cam != null ? cam.GetComponent<ThirdPersonCamera>() : FindFirstObjectByType<ThirdPersonCamera>();
            }
        }

        void HandleKeyboardInput()
        {
            int digit = ReadDigitPressed();
            if (digit >= 0)
            {
                char ch = (char)('0' + digit);
                _inputBuffer = _inputBuffer.Length >= 2 ? ch.ToString() : _inputBuffer + ch;
            }

            if (BackspacePressed() && _inputBuffer.Length > 0)
                _inputBuffer = _inputBuffer.Substring(0, _inputBuffer.Length - 1);

            if (TabPressed())
                _collapsed = !_collapsed;

            if (EnterPressed())
            {
                if (_inputBuffer.Length > 0)
                {
                    if (int.TryParse(_inputBuffer, out int id))
                    {
                        _inputBuffer = string.Empty;
                        ExecuteShortcut(id);
                    }
                    else
                    {
                        _inputBuffer = string.Empty;
                    }
                }
                else if (_lastId >= 0)
                {
                    ExecuteShortcut(_lastId);
                }
            }
        }

        /// <summary>
        /// 指定番号のモーション確認へ遷移する。成功時は true、無効番号時は false。
        /// </summary>
        public bool ExecuteShortcut(int id)
        {
            if (id < 0 || id > MaxShortcutId)
            {
                GameLog.Log($"[Shortcut] unknown id={id}");
                return false;
            }

            ResolveReferences();
            if (_player == null)
                return false;

            if (id == 0)
            {
                ExecuteResetOrToggleAi();
                _lastId = 0;
                RefreshUiText();
                return true;
            }

            _freezeAi = true;
            _allowActiveEnemiesAi = id <= 10;
            _player.SetGodMode(true);

            PrepareCleanState(keepWallSite: id == 4);

            Vector3 forward = Vector3.right;
            Vector3 playerPos = _player.transform.position;
            List<EnemyBrain> enemies = CollectEnemies();
            EnemyBrain e0 = enemies.Count > 0 ? enemies[0] : null;
            EnemyBrain e1 = enemies.Count > 1 ? enemies[1] : null;
            EnemyBrain e2 = enemies.Count > 2 ? enemies[2] : null;

            switch (id)
            {
                case 1:
                case 2:
                    LaunchBellyOrFaceCombo(id == 2, playerPos, forward, e0, e1);
                    break;
                case 3:
                    LaunchFrontPin(playerPos, forward, e0);
                    break;
                case 4:
                    LaunchWallPin(forward, e0, e1, e2);
                    break;
                case 5:
                    LaunchBearHug(playerPos, forward, e0);
                    break;
                case 6:
                    LaunchAnkleGrab(playerPos, forward, e0);
                    break;
                case 7:
                case 8:
                    LaunchHaul(prone: id == 8, playerPos, forward, e0);
                    break;
                case 9:
                case 10:
                    LaunchMount(prone: id == 10, playerPos, forward, e0);
                    break;
                case 11:
                    LaunchPlayerLightCombo(playerPos, forward, e0);
                    break;
                case 12:
                    LaunchPlayerHeavyBreak(playerPos, forward, e0);
                    break;
                case 13:
                    LaunchHurtSequence(playerPos, forward, e0);
                    break;
                case 14:
                case 15:
                    LaunchDownSolo(prone: id == 14, playerPos, forward, e0);
                    break;
            }

            _lastId = id;
            GameLog.Log($"[Shortcut] #{id} {LabelOf(id)}");
            RefreshUiText();
            return true;
        }

        void ExecuteResetOrToggleAi()
        {
            // ショートカットは god mode を立てるので、リセットで必ず戻す（立てっぱなし防止）。
            _player.SetGodMode(false);

            bool busy = (_grapple != null && _grapple.IsGrappled)
                || _player.IsDown
                || (_combat != null && _combat.IsAttacking)
                || (_player.GroundMount != null && _player.GroundMount.BlocksGetUp)
                || _sequence != null;

            if (busy)
            {
                _freezeAi = true;
                _allowActiveEnemiesAi = false;
                PrepareCleanState(keepWallSite: false);
                RestoreDefaultBellySettings();
                GameLog.Log("[Shortcut] #0 state reset (AI frozen)");
                return;
            }

            _freezeAi = !_freezeAi;
            _allowActiveEnemiesAi = false;
            _activeTarget = null;
            _activeSubTarget = null;
            _activeThirdTarget = null;
            RestoreDefaultBellySettings();

            if (_screenLock != null)
                _screenLock.enabled = true;

            if (!_freezeAi)
            {
                foreach (var enemy in CollectEnemies())
                    enemy.ClearPreviewHold();
            }
            else
            {
                KeepIdleEnemiesFrozen();
            }

            GameLog.Log($"[Shortcut] #0 AI freeze={_freezeAi}");
        }

        void RestoreDefaultBellySettings()
        {
            if (_belly != null && _defaultBellyValuesCaptured)
            {
                _belly.FacePunchChance = _defaultFacePunchChance;
                _belly.ThrowDelaySeconds = _defaultThrowDelaySeconds;
                var handOff = FindFirstObjectByType<GrappleHandOff>();
                if (handOff != null)
                    handOff.Chance = _defaultHandOffChance;
            }
        }

        void PrepareCleanState(bool keepWallSite)
        {
            if (_sequence != null)
            {
                StopCoroutine(_sequence);
                _sequence = null;
            }

            Time.timeScale = 1f;
            GroundHaulUp.Forced = false;
            GroundHaulUp.Chance = 0.35f;

            _activeTarget = null;
            _activeSubTarget = null;
            _activeThirdTarget = null;

            _player.GroundMount?.Haul?.End(false);
            _grapple?.ResetForMotionPreview();
            _combat?.CancelAttack();
            _player.RestoreAll();
            _player.GroundMount?.ClearSearchDelay();
            _anim?.ResetForMotionPreview();
            _camera?.PushFocus(0f, 0f);

            if (!keepWallSite && _screenLock != null)
                _screenLock.enabled = true;

            if (_motor != null && !keepWallSite)
            {
                _motor.ForwardLimitX = float.PositiveInfinity;
                _motor.BackLimitX = float.NegativeInfinity;
            }

            Vector3 pos = _player.transform.position;
            pos.y = 0f;
            pos.z = _motor != null ? _motor.LaneZ : 0f;
            TeleportPlayer(pos, Quaternion.LookRotation(Vector3.right, Vector3.up));

            List<EnemyBrain> enemies = CollectEnemies();
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyBrain enemy = enemies[i];
                enemy.ResetForMotionPreview();
                PlaceEnemy(enemy, new Vector3(pos.x - 4.2f - i * 0.8f, 0f, pos.z),
                    Quaternion.LookRotation(Vector3.right, Vector3.up));
                enemy.HoldFire(999f);
                enemy.StayPlanted(999f);
            }
        }

        void LaunchBellyOrFaceCombo(bool face, Vector3 playerPos, Vector3 forward, EnemyBrain e0, EnemyBrain e1)
        {
            if (_belly != null)
            {
                _belly.FacePunchChance = face ? 1f : 0f;
                _belly.ThrowDelaySeconds = 0.35f;
            }
            var handOff = FindFirstObjectByType<GrappleHandOff>();
            if (handOff != null)
                handOff.Chance = 0f;

            if (e0 != null)
            {
                PlaceEnemy(e0, playerPos - forward * CombatConstants.BackGrabStandoff,
                    Quaternion.LookRotation(forward, Vector3.up));
                e0.ClearPreviewHold();
                _activeTarget = e0;
                e0.ForceStartBackGrab(GrappleKind.Back);
            }

            if (e1 != null)
            {
                PlaceEnemy(e1, playerPos + forward * 0.90f,
                    Quaternion.LookRotation(-forward, Vector3.up));
                e1.ClearPreviewHold();
                e1.HoldFire(0.8f);
                _activeSubTarget = e1;
            }
        }

        void LaunchFrontPin(Vector3 playerPos, Vector3 forward, EnemyBrain e0)
        {
            if (WallPinSites.Find(playerPos) != null)
            {
                playerPos.x = 0f;
                TeleportPlayer(playerPos, Quaternion.LookRotation(forward, Vector3.up));
            }

            if (e0 == null)
                return;

            PlaceEnemy(e0, playerPos + forward * CombatConstants.GrabStandoff,
                Quaternion.LookRotation(-forward, Vector3.up));
            e0.ClearPreviewHold();
            _activeTarget = e0;
            e0.ForceStartPin(wall: false);
        }

        void LaunchWallPin(Vector3 forward, EnemyBrain e0, EnemyBrain e1, EnemyBrain e2)
        {
            if (_screenLock != null)
                _screenLock.enabled = false;

            if (_motor != null)
            {
                _motor.ForwardLimitX = float.PositiveInfinity;
                _motor.BackLimitX = float.NegativeInfinity;
            }

            float laneZ = _motor != null ? _motor.LaneZ : 0f;
            WallPinSite site = WallPinSites.Nearest(_player.transform.position);
            Vector3 playerPos = _player.transform.position;
            playerPos.y = 0f;
            playerPos.z = laneZ;
            if (site != null)
                playerPos.x = site.CenterX;

            TeleportPlayer(playerPos, Quaternion.LookRotation(forward, Vector3.up));

            if (e1 != null)
            {
                PlaceEnemy(e1, new Vector3(playerPos.x - 1.5f, 0f, laneZ),
                    Quaternion.LookRotation(forward, Vector3.up));
                e1.ClearPreviewHold();
                _activeSubTarget = e1;
            }

            if (e2 != null)
            {
                PlaceEnemy(e2, new Vector3(playerPos.x + 1.8f, 0f, laneZ),
                    Quaternion.LookRotation(-forward, Vector3.up));
                e2.ClearPreviewHold();
                _activeThirdTarget = e2;
            }

            if (e0 != null)
            {
                PlaceEnemy(e0, new Vector3(playerPos.x + 0.95f, 0f, laneZ),
                    Quaternion.LookRotation(-forward, Vector3.up));
                e0.ClearPreviewHold();
                _activeTarget = e0;
                e0.ForceStartPin(wall: true);
            }
        }

        void LaunchBearHug(Vector3 playerPos, Vector3 forward, EnemyBrain e0)
        {
            if (e0 == null)
                return;

            PlaceEnemy(e0, playerPos + forward * CombatConstants.BearHugStandoff,
                Quaternion.LookRotation(-forward, Vector3.up));
            e0.ClearPreviewHold();
            _activeTarget = e0;
            e0.ForceStartBearHug();
        }

        void LaunchAnkleGrab(Vector3 playerPos, Vector3 forward, EnemyBrain e0)
        {
            if (e0 == null)
                return;

            PlaceEnemy(e0, playerPos - forward * 0.72f,
                Quaternion.LookRotation(forward, Vector3.up));
            e0.ClearPreviewHold();
            _activeTarget = e0;
            e0.ForceStartBackGrab(GrappleKind.AnkleGrab);
        }

        void LaunchHaul(bool prone, Vector3 playerPos, Vector3 forward, EnemyBrain e0)
        {
            GroundHaulUp.Forced = true;

            if (e0 != null)
            {
                PlaceEnemy(e0, playerPos + forward * 1.15f,
                    Quaternion.LookRotation(-forward, Vector3.up));
                e0.ClearPreviewHold();
                _activeTarget = e0;
                _player.ForceDown(8f, "shortcut-haul", e0.transform, forward: prone);
            }
        }

        void LaunchMount(bool prone, Vector3 playerPos, Vector3 forward, EnemyBrain e0)
        {
            GroundHaulUp.Forced = false;
            GroundHaulUp.Chance = 0f;

            if (e0 != null)
            {
                PlaceEnemy(e0, playerPos + forward * 1.15f,
                    Quaternion.LookRotation(-forward, Vector3.up));
                e0.ClearPreviewHold();
                _activeTarget = e0;
                _player.ForceDown(8f, "shortcut-mount", e0.transform, forward: prone);
            }
        }

        void LaunchPlayerLightCombo(Vector3 playerPos, Vector3 forward, EnemyBrain e0)
        {
            if (e0 != null)
            {
                PlaceEnemy(e0, playerPos + forward * 1.05f,
                    Quaternion.LookRotation(-forward, Vector3.up));
                e0.StayPlanted(10f);
                e0.HoldFire(999f);
            }

            if (_combat != null)
                _sequence = StartCoroutine(RunLightCombo());
        }

        void LaunchPlayerHeavyBreak(Vector3 playerPos, Vector3 forward, EnemyBrain e0)
        {
            if (e0 != null)
            {
                PlaceEnemy(e0, playerPos + forward * 1.10f,
                    Quaternion.LookRotation(-forward, Vector3.up));
                e0.StayPlanted(10f);
                e0.HoldFire(999f);
            }

            if (_combat != null)
                _sequence = StartCoroutine(RunHeavyBreakCombo());
        }

        void LaunchHurtSequence(Vector3 playerPos, Vector3 forward, EnemyBrain e0)
        {
            if (e0 == null)
                return;

            PlaceEnemy(e0, playerPos + forward * 0.95f,
                Quaternion.LookRotation(-forward, Vector3.up));
            e0.StayPlanted(10f);
            e0.HoldFire(999f);
            _sequence = StartCoroutine(RunHurtPreview(e0));
        }

        void LaunchDownSolo(bool prone, Vector3 playerPos, Vector3 forward, EnemyBrain e0)
        {
            Transform attacker = null;
            if (e0 != null)
            {
                PlaceEnemy(e0, playerPos + forward * 1.15f,
                    Quaternion.LookRotation(-forward, Vector3.up));
                e0.StayPlanted(10f);
                e0.HoldFire(999f);
                attacker = e0.transform;
            }

            _player.GroundMount?.DelayNextSearch(30f);
            _player.ForceDown(2.2f, "shortcut-down", attacker, forward: prone);
            _player.RequestGetUp();
        }

        IEnumerator RunLightCombo()
        {
            _combat.Begin(AttackLibrary.Light(), 0);
            while (_combat.IsAttacking)
                yield return null;

            _combat.Begin(AttackLibrary.Light(), 1);
            while (_combat.IsAttacking)
                yield return null;

            _combat.Begin(AttackLibrary.Heavy(), 2);
            while (_combat.IsAttacking)
                yield return null;

            _sequence = null;
        }

        IEnumerator RunHeavyBreakCombo()
        {
            _combat.Begin(AttackLibrary.Heavy(), 0);
            while (_combat.IsAttacking)
                yield return null;

            yield return new WaitForSeconds(0.15f);

            _combat.Begin(AttackLibrary.BreakAttack(), 0);
            while (_combat.IsAttacking)
                yield return null;

            _sequence = null;
        }

        IEnumerator RunHurtPreview(EnemyBrain attacker)
        {
            TriggerHurtHit(attacker, PunchZone.Body, AttackLibrary.Light());
            yield return new WaitForSeconds(0.85f);

            TriggerHurtHit(attacker, PunchZone.Face, AttackLibrary.Light());
            yield return new WaitForSeconds(0.85f);

            TriggerHurtHit(attacker, PunchZone.Body, AttackLibrary.Heavy());
            _sequence = null;
        }

        void TriggerHurtHit(EnemyBrain attacker, PunchZone zone, AttackData attack)
        {
            if (_player == null || _player.IsDead || attacker == null)
                return;

            _player.State.ResetFlinch();
            _player.LastHitZone = zone;
            _player.ReceiveHit(attacker.State, attack, attacker.transform);
        }

        void KeepIdleEnemiesFrozen()
        {
            foreach (EnemyBrain enemy in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (enemy == null || !enemy.IsAlive)
                    continue;

                if (IsParticipatingEnemy(enemy))
                    continue;

                enemy.HoldFire(10f);
                enemy.StayPlanted(10f);
            }
        }

        bool IsParticipatingEnemy(EnemyBrain enemy)
        {
            if (_allowActiveEnemiesAi &&
                (enemy == _activeTarget || enemy == _activeSubTarget || enemy == _activeThirdTarget))
                return true;

            if (_grapple != null && _grapple.IsGrappled)
            {
                if (enemy == _grapple.Captor || enemy == _grapple.WallAssist ||
                    enemy == _grapple.WallAssistCandidate || enemy == _grapple.WallThird)
                    return true;
            }

            if (_player != null && _player.GroundMount != null)
            {
                if (enemy == _player.GroundMount.Attacker)
                    return true;
                if (_player.GroundMount.Haul != null && enemy == _player.GroundMount.Haul.Attacker)
                    return true;
            }

            if (_belly != null && enemy == _belly.BellyPuncher)
                return true;

            return false;
        }

        void TeleportPlayer(Vector3 pos, Quaternion rot)
        {
            var cc = _player.GetComponent<CharacterController>();
            if (cc != null)
                cc.enabled = false;
            _player.transform.SetPositionAndRotation(pos, rot);
            if (cc != null)
                cc.enabled = true;
        }

        static void PlaceEnemy(EnemyBrain enemy, Vector3 pos, Quaternion rot)
        {
            if (enemy == null)
                return;

            pos.y = enemy.transform.position.y;
            enemy.transform.SetPositionAndRotation(pos, rot);
        }

        static List<EnemyBrain> CollectEnemies()
        {
            EnemyBrain[] found = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            var list = new List<EnemyBrain>(found.Length);
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null)
                    list.Add(found[i]);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return list;
        }

        static string LabelOf(int id)
        {
            if (id == 0)
                return "リセット / 敵AI切替";

            for (int i = 0; i < Entries.Length; i++)
            {
                if (Entries[i].Id == id)
                    return Entries[i].Label;
            }

            return "無効な番号";
        }

        void BuildUi()
        {
            if (_built)
                return;

            _built = true;

            var canvasGo = new GameObject("Motion Shortcut Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 95;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panelGo.transform.SetParent(canvasGo.transform, false);

            _panelRect = panelGo.GetComponent<RectTransform>();
            _panelRect.anchorMin = new Vector2(0f, 1f);
            _panelRect.anchorMax = new Vector2(0f, 1f);
            _panelRect.pivot = new Vector2(0f, 1f);
            _panelRect.anchoredPosition = new Vector2(26f, -72f);
            _panelRect.sizeDelta = new Vector2(340f, 440f);

            var back = panelGo.GetComponent<Image>();
            back.color = new Color(0f, 0f, 0f, 0.52f);
            back.raycastTarget = false;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(panelGo.transform, false);

            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(12f, 8f);
            textRt.offsetMax = new Vector2(-12f, -8f);

            _bodyText = textGo.GetComponent<Text>();
            _bodyText.font = MatchHud.JapaneseFont;
            _bodyText.fontSize = 15;
            _bodyText.lineSpacing = 1.08f;
            _bodyText.alignment = TextAnchor.UpperLeft;
            _bodyText.color = new Color(1f, 1f, 1f, 0.88f);
            _bodyText.raycastTarget = false;
            _bodyText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _bodyText.verticalOverflow = VerticalWrapMode.Overflow;
        }

        void RefreshUiText()
        {
            if (_bodyText == null)
                return;

            string status;
            if (_inputBuffer.Length > 0)
            {
                string preview = int.TryParse(_inputBuffer, out int parsed) ? LabelOf(parsed) : "無効";
                status = $"▶ 入力中: [ {_inputBuffer} ] ({preview}) ─ Enterで派生";
            }
            else if (_lastId >= 0)
            {
                status = $"▶ 選択中: [{_lastId}] {LabelOf(_lastId)} (Enterで再実行)";
            }
            else
            {
                status = "▶ 数字を入力して Enter でモーション再生";
            }

            string text;
            if (_collapsed)
            {
                _panelRect.sizeDelta = new Vector2(340f, 64f);
                text = "【モーション確認】数字 → Enter (Tab:開く)\n" + status;
            }
            else
            {
                _panelRect.sizeDelta = new Vector2(340f, 440f);
                var sb = new System.Text.StringBuilder(512);
                sb.AppendLine("【モーション確認】数字 → Enter (BS:消去 / Tab:開閉)");
                sb.AppendLine(status);
                for (int i = 0; i < Entries.Length; i++)
                    sb.Append(Entries[i].Id < 10 ? " " : "").Append(Entries[i].Id).Append(": ").AppendLine(Entries[i].Label);
                sb.Append(" 0: リセット / 敵AI切替 (現在: ").Append(_freezeAi ? "停止" : "通常").Append(')');
                text = sb.ToString();
            }

            if (text != _lastRenderedText)
            {
                _lastRenderedText = text;
                _bodyText.text = text;
            }
        }

        static int ReadDigitPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.digit0Key.wasPressedThisFrame || kb.numpad0Key.wasPressedThisFrame) return 0;
                if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) return 1;
                if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) return 2;
                if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) return 3;
                if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) return 4;
                if (kb.digit5Key.wasPressedThisFrame || kb.numpad5Key.wasPressedThisFrame) return 5;
                if (kb.digit6Key.wasPressedThisFrame || kb.numpad6Key.wasPressedThisFrame) return 6;
                if (kb.digit7Key.wasPressedThisFrame || kb.numpad7Key.wasPressedThisFrame) return 7;
                if (kb.digit8Key.wasPressedThisFrame || kb.numpad8Key.wasPressedThisFrame) return 8;
                if (kb.digit9Key.wasPressedThisFrame || kb.numpad9Key.wasPressedThisFrame) return 9;
                return -1;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0)) return 0;
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) return 1;
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) return 2;
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) return 3;
            if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) return 4;
            if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5)) return 5;
            if (Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6)) return 6;
            if (Input.GetKeyDown(KeyCode.Alpha7) || Input.GetKeyDown(KeyCode.Keypad7)) return 7;
            if (Input.GetKeyDown(KeyCode.Alpha8) || Input.GetKeyDown(KeyCode.Keypad8)) return 8;
            if (Input.GetKeyDown(KeyCode.Alpha9) || Input.GetKeyDown(KeyCode.Keypad9)) return 9;
#endif
            return -1;
        }

        static bool BackspacePressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
                return kb.backspaceKey.wasPressedThisFrame || kb.deleteKey.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete);
#else
            return false;
#endif
        }

        static bool TabPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
                return kb.tabKey.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Tab);
#else
            return false;
#endif
        }

        static bool EnterPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
                return kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#else
            return false;
#endif
        }
    }
}
