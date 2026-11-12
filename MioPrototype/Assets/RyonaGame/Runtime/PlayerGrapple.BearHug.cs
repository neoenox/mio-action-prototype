using System;
using System.Collections;
using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class PlayerGrapple : MonoBehaviour
    {
        /// <summary>
        /// ベアハッグ中、掴み位置を「密着 → 少し離れる → 密着」と動かす。
        /// ダメージは密着した瞬間（<see cref="IsBearHugContact"/>）だけに入れる。
        /// </summary>
        void TickBearHugBody(float dt)
        {
            if (Captor == null)
                return;

            switch (_hugStage)
            {
                case HugStage.Pull:
                    _hugTime += dt;
                    {
                        float x = Mathf.Clamp01(_hugTime / HugPullSeconds);

                        // 最初の 35% はほとんど動かず（相手が体重を預けて粘る）、
                        // 残りで一気に引き込む。等速で寄せると「滑っている」ように見える。
                        _hugPhase = x < 0.35f
                            ? 0.05f * (x / 0.35f)
                            : 0.05f + 0.95f * Mathf.SmoothStep(
                                0f, 1f, (x - 0.35f) / 0.65f);

                        // 引き寄せの勢いは中盤で最大。密着の瞬間には 0 へ戻る。
                        PullPulse = Mathf.Sin(Mathf.PI * x);

                        if (_hugTime >= HugPullSeconds)
                        {
                            // 引き込みの勢いで少し行き過ぎてから戻る（ぶつかる感じ）。
                            _hugPhase = 1.08f;
                            PullPulse = 0f;
                            _hugStage = HugStage.Contact;
                            _hugTime = 0f;
                        }
                    }
                    break;

                case HugStage.Contact: // 密着（ここでダメージ）
                    _hugTime += dt;
                    _hugPhase = Mathf.MoveTowards(_hugPhase, 1f, dt * 0.8f);
                    PullPulse = Mathf.MoveTowards(PullPulse, 0f, dt * 12f);
                    if (_hugTime >= HugContactSeconds)
                    {
                        _hugStage = HugStage.Release;
                        _hugTime = 0f;
                    }
                    break;

                case HugStage.Release: // 少し離す
                    _hugTime += dt;
                    _hugPhase = 1f - Mathf.SmoothStep(0f, 1f,
                        Mathf.Clamp01(_hugTime / HugReleaseSeconds));
                    PullPulse = Mathf.MoveTowards(PullPulse, 0f, dt * 10f);
                    if (_hugTime >= HugReleaseSeconds)
                    {
                        _hugPhase = 0f;
                        _hugStage = HugStage.Rest;
                        _hugTime = 0f;
                    }
                    break;
            }

            SqueezePulse = Mathf.Clamp01(_hugPhase);

            // 掴み位置の基準（CombatConstants.BearHugStandoff）から、密着度に応じて前後させる。
            // 方向は「相手から見た彼女」の向きを使い、横へは流れないようにする。
            Vector3 dir = transform.position - Captor.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f)
                dir = -Captor.transform.forward;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f)
                return;
            dir.Normalize();

            float distance = CombatConstants.BearHugStandoff - HugTightInset * _hugPhase;
            Vector3 spot = Captor.transform.position + dir * distance;
            spot.y = transform.position.y;
            transform.position = spot;
        }

    }
}
