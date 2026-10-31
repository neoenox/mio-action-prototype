using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// `-attacktest` … 最寄りの敵を彼女の 1.15m 前に固定し、殴られ役にする。
    ///
    /// 攻撃の「拳の位置」と「相手の体」が合っているかを、毎回同じ条件で撮り比べるための道具。
    /// 距離は `-attacktestdist=N` で差し替えられる（既定 1.15）。
    /// 攻撃自体は `-holdattack` で出させる。
    ///
    /// 敵は「彼女が攻撃していない間」にだけ置き直す。攻撃中に置き直すと、
    /// 踏み込みで詰めた距離が毎回 1.15m に戻され、届くはずの拳が届かなくなる。
    /// </summary>
    [AddComponentMenu("Ryona/Attack Test Driver")]
    public sealed class AttackTestDriver : MonoBehaviour
    {
        /// <summary>彼女の正面どれだけに敵を置くか（m）。</summary>
        public float Distance = 1.15f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            bool wanted = false;
            float distance = 1.15f;

            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "-attacktest", System.StringComparison.OrdinalIgnoreCase))
                    wanted = true;
                else if (string.Equals(args[i], "-attacktestdist", System.StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length && float.TryParse(args[i + 1], out float d))
                    distance = d;
            }

            if (!wanted)
                return;

            var go = new GameObject("Attack Test Driver");
            var driver = go.AddComponent<AttackTestDriver>();
            driver.Distance = distance;
        }

        EnemyBrain _target;
        PlayerCombat _combat;
        Vector3 _anchor;
        bool _hasAnchor;

        void Update()
        {
            if (_target == null)
            {
                _target = FindNearest();
                if (_target == null)
                    return;
            }

            if (_combat == null)
                _combat = FindFirstObjectByType<PlayerCombat>();

            var player = FindFirstObjectByType<PlayerStats>();
            if (player == null || player.IsDead || !_target.IsAlive)
            {
                _hasAnchor = false;
                _target = null;
                return;
            }

            // 殴られ役が倒れると「当たった画」が撮れないので、体力を毎フレーム戻す。
            _target.State.RestoreAll();

            // 攻撃は封じる（殴り返されると彼女がひるんで拳が伸びない）。
            _target.AttackCooldown = 999f;

            // 他の敵は遠ざける。近くにいるとオートエイムがそちらへ向き、
            // 固定した殴られ役ではなく別の敵を殴ってしまう。
            int far = 0;
            foreach (var brain in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (brain == null || brain == _target || !brain.IsAlive)
                    continue;

                far++;
                Vector3 away = player.transform.position - player.transform.forward * (12f + far * 4f);
                away.y = brain.transform.position.y;
                brain.transform.position = away;
                brain.AttackCooldown = 999f;
            }

            bool attacking = _combat != null && _combat.IsAttacking;

            if (!attacking || !_hasAnchor)
            {
                Vector3 forward = player.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f)
                    forward = Vector3.forward;

                _anchor = player.transform.position + forward.normalized * Distance;
                _anchor.y = _target.transform.position.y;
                _hasAnchor = true;

                _target.transform.rotation = Quaternion.LookRotation(
                    player.transform.position - _anchor, Vector3.up);

                Debug.Log($"[AttackTest] {_target.name} を {Distance:F2}m 前に固定");
            }

            // 毎フレーム固定する。AI は動かしたまま（姿勢は自然）だが、位置は動かさない。
            _target.transform.position = _anchor;
        }

        EnemyBrain FindNearest()
        {
            var player = FindFirstObjectByType<PlayerStats>();
            if (player == null)
                return null;

            EnemyBrain best = null;
            float bestDist = float.MaxValue;

            foreach (var brain in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (brain == null || !brain.IsAlive)
                    continue;

                float d = Vector3.Distance(brain.transform.position, player.transform.position);
                if (d >= bestDist)
                    continue;

                best = brain;
                bestDist = d;
            }

            return best;
        }
    }
}
