using System.Collections;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>アリーナの歩ける範囲と波進行の回帰テスト。</summary>
    public sealed class ArenaRuntimeTests
    {
        [UnityTest]
        public IEnumerator ロック中の歩ける幅を記録する()
        {
            yield return TestScene.LoadPrototype();

            var motor = Object.FindFirstObjectByType<PlayerMotor>();
            var gate = Object.FindFirstObjectByType<BeltScreenLock>();
            Assert.IsNotNull(motor);
            Assert.IsNotNull(gate);

            float deadline = Time.realtimeSinceStartup + 5f;
            while (!gate.IsLocked && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsTrue(gate.IsLocked, "開始時にアリーナがロックされる");
            float width = motor.ForwardLimitX - motor.BackLimitX;
            Debug.Log($"[ArenaTest] walkable={width:F2}m center={gate.LockX:F2} " +
                      $"limits={motor.BackLimitX:F2}..{motor.ForwardLimitX:F2}");
            Assert.Greater(width, 2.5f,
                $"EdgeMargin 縮小で歩ける幅が増える（変更前 1.37m / 実測 {width:F2}m）");
        }
        [UnityTest]
        public IEnumerator 十波は五アリーナに二波ずつ割り当てられてクリアする()
        {
            yield return TestScene.LoadPrototype();

            var director = Object.FindFirstObjectByType<EncounterDirector>();
            var player = Object.FindFirstObjectByType<PlayerStats>();
            var gate = Object.FindFirstObjectByType<BeltScreenLock>();
            Assert.IsNotNull(director);
            Assert.IsNotNull(player);
            Assert.IsNotNull(gate);

            director.WaveGap = 0.05f;
            var lethal = new AttackData
            {
                id = "arena_lethal", kind = AttackKind.Heavy, damage = 9999f,
                partDamage = 0f, targetPart = BodyPart.Torso
            };

            int seenArenas = 0;
            int lastArena = -1;
            for (int wave = 1; wave <= 10 && !director.StageCleared; wave++)
            {
                int arena = director.Plan.ArenaOf(wave);
                if (arena != lastArena)
                {
                    seenArenas++;
                    Assert.AreEqual(wave == 1 ? -6f : -6f + (arena * director.ArenaPitch),
                                    director.Plan.CenterOf(arena), 0.001f);
                    lastArena = arena;
                }

                // 敵はアリーナ右端の外から入る（歩き出した後の位置ではなく入場地点で見る）
                Assert.Greater(director.LastEntryX, director.Plan.CenterOf(arena) + 1f,
                    "敵がアリーナの右側の外から入る");

                foreach (var brain in Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                {
                    if (brain != null && brain.IsAlive)
                        brain.TakeHit(player.State, lethal);
                }

                float deadline = Time.realtimeSinceStartup + 3f;
                while (!director.StageCleared && director.Alive > 0 && Time.realtimeSinceStartup < deadline)
                    yield return null;

                // アリーナを移動するときは先にロックが外れる。入場地点まで歩かせてから次を待つ。
                if (!director.StageCleared && director.ArenaMovingNext && !gate.IsLocked)
                    player.transform.position = new Vector3(director.NextTriggerX + 0.1f, 0f, player.transform.position.z);

                while (!director.StageCleared && director.Wave <= wave && Time.realtimeSinceStartup < deadline)
                    yield return null;
            }

            Assert.AreEqual(5, seenArenas, "10 波が 5 アリーナに分かれる");
            Assert.IsTrue(director.StageCleared, "最終波で STAGE CLEAR になる");
            Assert.AreEqual(10, director.Wave);
        }
    }
}
