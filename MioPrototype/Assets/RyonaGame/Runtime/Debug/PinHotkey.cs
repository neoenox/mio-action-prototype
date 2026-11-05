using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 押さえつけ（<see cref="GrappleKind.Pin"/>）をすぐ試すための入口。
    ///
    /// AI は背後からの掴みを優先するうえ、押さえつけは「正面 0.80〜1.30m」でしか出ないため、
    /// 普通に遊んでいると正面から掴まれる場面がほとんど無い（実測でも 50 秒で 1〜2 回）。
    /// 姿勢とカメラを確認できるよう、<c>P</c> キーで一番近い敵に押さえつけを指示する。
    ///
    /// <c>-pinnow</c> を付けて起動すると、1.5 秒後に 1 回だけ同じことをする（自動確認用）。
    /// </summary>
    [AddComponentMenu("Ryona/Pin Hotkey")]
    public sealed class PinHotkey : MonoBehaviour
    {
        PlayerStats _player;
        float _autoAt = -1f;

        /// <summary><c>-armlocknow</c> で 1 回だけ関節技を指示する時刻。</summary>
        float _autoArmLockAt = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<PinHotkey>() != null)
                return;

            bool auto = false;
            bool autoLock = false;
            foreach (string a in System.Environment.GetCommandLineArgs())
            {
                if (string.Equals(a, "-pinnow", System.StringComparison.OrdinalIgnoreCase))
                    auto = true;
                else if (string.Equals(a, "-armlocknow", System.StringComparison.OrdinalIgnoreCase))
                    autoLock = true;
            }

            var go = new GameObject("Pin Hotkey");
            var hotkey = go.AddComponent<PinHotkey>();
            if (auto)
            {
                hotkey._autoAt = Time.time + 1.5f;
                Debug.Log("[Pin] -pinnow: 1.5 秒後に押さえつけを 1 回指示します");
            }
            if (autoLock)
            {
                hotkey._autoArmLockAt = Time.time + 1.5f;
                Debug.Log("[Pin] -armlocknow: 1.5 秒後に関節技を 1 回指示します");
            }
        }

        void Update()
        {
            if (_player == null)
                _player = FindFirstObjectByType<PlayerStats>();

            if (_autoAt > 0f && Time.time >= _autoAt)
            {
                _autoAt = -1f;
                TryOrderGrapple(true);
            }
            if (_autoArmLockAt > 0f && Time.time >= _autoArmLockAt)
            {
                _autoArmLockAt = -1f;
                TryOrderGrapple(false);
            }

            if (InputReader.PinTestPressed())
                TryOrderGrapple(true);
            if (InputReader.ArmLockTestPressed())
                TryOrderGrapple(false);
        }

        void TryOrderGrapple(bool pin)
        {
            if (_player == null || _player.IsDead)
                return;

            var grapple = _player.GetComponent<PlayerGrapple>();
            if (grapple != null && grapple.IsGrappled)
            {
                Debug.Log("[Pin] ホットキー: すでに掴まれているので指示しない");
                return;
            }

            // 一番近い敵を彼女の正面 1.0m へ立たせて、押さえつけを指示する。
            EnemyBrain best = null;
            float bestDistance = float.MaxValue;
            foreach (var enemy in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (enemy == null || !enemy.IsAlive || enemy.IsRising)
                    continue;

                float d = Vector3.Distance(enemy.transform.position, _player.transform.position);
                if (d >= bestDistance)
                    continue;

                best = enemy;
                bestDistance = d;
            }

            if (best == null)
                return;

            Vector3 forward = _player.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.right;

            Vector3 spot = _player.transform.position + forward * 1.0f;
            spot.y = best.transform.position.y;
            best.transform.position = spot;
            best.transform.rotation = Quaternion.LookRotation(-forward, Vector3.up);

            if (pin)
            {
                best.CanPin = true;
                best.QueuePin();
                Debug.Log($"[Pin] ホットキー: {best.name} に押さえつけを指示");
            }
            else
            {
                best.CanArmLock = true;
                best.QueueArmLock();
                Debug.Log($"[Pin] ホットキー: {best.name} に関節技を指示");
            }
        }
    }
}
