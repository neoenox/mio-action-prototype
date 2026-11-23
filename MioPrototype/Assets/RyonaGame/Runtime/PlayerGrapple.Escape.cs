using System;
using System.Collections;
using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class PlayerGrapple : MonoBehaviour
    {
        internal void Escape()
        {
            EndWallPin();
            var captor = Captor;
            int inputs = InputCount;
            float spent = _session.HeldSeconds;

            ClearPinHandOff();
            IsGrappled = false;

            // 振りほどきも解除のひとつ。連打の最後の 1 発がそのまま攻撃にならないよう時刻を残す（Issue #55）。
            ReleasedAt = Time.time;
            _session.LimitRemaining = 0f;
            _session.SuppressRemaining = 0f;
            _session.Captor = null;
            _hugStage = HugStage.Rest;
            _hugTime = 0f;
            _hugPhase = 0f;

            PlaySfx(SfxId.Guard, 0.9f);

            // 振りほどいた側に反撃の隙を作る（掴んでいた側がよろける）
            if (captor != null)
                captor.ForceStagger(CombatConstants.GrappleEscapeStun);

            GameLog.Log($"[Grapple] escape inputs={inputs} need={RequiredInputs:F1} t={spent:F2}s");
            Escaped?.Invoke();
        }

        /// <summary>
        /// 押さえつけ（Pin）の時間切れ。後ろ拘束では呼ばない。
        /// 壁では投げてから解放する。腹ダメージが上限に達していればうつ伏せに倒れる。
        /// 実際の解除は <see cref="Release"/> が行う（解除時刻もそこで記録される）。
        /// </summary>
        /// <param name="resolveBellyDown">
        /// 拘束中に溜まった腹ダメージをダウンとして消化するか。
        /// 壁の投げ捨ては、この直後に投げ捨てのダウンを自分で起こすので false で呼ぶ
        /// （true のままだと 1 フレームに 2 回ダウンし、<c>[Down] held-belly</c> と
        /// <c>[Down] wall-toss</c> が並ぶ）。
        /// </param>
        void Fail(bool resolveBellyDown = true)
        {
            if (IsWallPin && WallPhase == WallStage.Held && !Stats.IsDown)
            {
                EndWallSplit();
                BeginWallToss();
                return;
            }

            var captor = Captor;
            int inputs = InputCount;
            bool ankleTrip = Kind == GrappleKind.AnkleGrab;
            Release();

            // 足取りは、振りほどけなければ前へ転ぶ（うつ伏せ。飛ばさず、足を取られて崩れる）。
            if (ankleTrip)
                Stats.ForceDown(CombatConstants.DownDuration, "ankle-trip",
                    captor != null ? captor.transform : null, forward: true);
            else if (resolveBellyDown)
                Stats.ResolveHeldBellyDown();

            // 拘束を解いた直後に、別の敵から即座に追撃されないようにする。
            foreach (var enemy in EnemyRegistry.All)
            {
                if (enemy == null || enemy == captor || !enemy.IsAlive)
                    continue;

                if (Vector3.Distance(enemy.transform.position, transform.position) > 6f)
                    continue;

                enemy.HoldFire(CombatConstants.GrappleReleaseGraceSeconds);
            }

            GameLog.Log($"[Grapple] release kind={Kind} inputs={inputs} need={RequiredInputs:F1}");

            Failed?.Invoke();
        }

        /// <summary>
        /// 壁押さえつけを終える。すぐにレーンへ戻さず、戻りの秒数
        /// （通常 <see cref="WallExitSeconds"/>、投げ捨ては <see cref="ThrowExitSeconds"/>）かけて
        /// z を 0 へ戻す（戻りの姿勢は <see cref="CharacterAnimator"/> が壁の重みで作る）。
        /// </summary>
        void EndWallPin()
        {
            if (!IsWallPin)
                return;

            StopWallSequence();

            _wallSite?.RetractAfterRelease();
            _wallSite = null;

            // 二人がかりの途中で壁拘束が終わったら、二体目も持ち場から離す。
            if (WallAssistCandidate != null)
                WallAssistCandidate.ReleaseWallAssist();

            _session.ClearWall();
            _session.ClearSplit();
            _wallExitFromZ = transform.position.z;

            // 投げ捨てのときは、飛んでいる間に z をレーンへ戻す（壁の上をすり抜けない）。
            _wallExitSeconds = _wallThrowExitSeconds > 0f ? _wallThrowExitSeconds : WallExitSeconds;
            _wallThrowExitSeconds = 0f;
            _wallExitRemaining = _wallExitSeconds;

            var motor = Motor();
            if (motor != null)
            {
                motor.SetLaneZOverride(_wallExitFromZ);
                motor.SnapToLaneFacing();
            }
        }

        /// <summary>
        /// 壁からレーンへの戻りを打ち切る。引き渡しのように、掴まれたまま
        /// 別の移動（押し出し・寄せ）が始まるときに使う。
        /// </summary>
        void ClearWallExit()
        {
            _wallExitRemaining = 0f;
            Motor()?.ClearLaneZOverride();
        }

        PlayerMotor Motor()
        {
            if (_motor == null)
                _motor = GetComponent<PlayerMotor>();
            return _motor;
        }

        static void PlaySfx(SfxId id, float volume)
        {
            var sfx = ProceduralSfx.Instance;
            if (sfx != null)
                sfx.Play(id, volume);
        }
    }
}
