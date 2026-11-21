using System.Collections;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;

namespace Ryona.Tests
{
    /// <summary>
    /// キャラクターのリグ（<see cref="CharacterAnimator"/> と <see cref="ProceduralRig"/>）を待つ。
    ///
    /// リグは VRM の読み込みが終わってから組み上がる。シーンを読んだ直後は
    /// <see cref="CharacterAnimator"/> がまだ無いか、あっても <see cref="ProceduralRig.Ready"/> が false。
    /// 組み上がる前の腕や頭の位置を測ると、実際と違う値になる。
    ///
    /// 使い方:
    /// <code>
    /// yield return TestRig.WaitReady(stats, 8f);
    /// _anim = TestRig.RequireReady(stats);
    /// </code>
    /// </summary>
    internal static class TestRig
    {
        public const string NotReadyMessage = "キャラクターのリグが組み上がらない（VRM の読み込み待ちで時間切れ）";

        /// <summary><paramref name="owner"/> の配下で組み上がったアニメーター。まだなら null。</summary>
        public static CharacterAnimator ReadyAnimator(Component owner)
        {
            if (owner == null)
                return null;

            var anim = owner.GetComponentInChildren<CharacterAnimator>();
            return anim != null && anim.Rig != null && anim.Rig.Ready ? anim : null;
        }

        /// <summary>リグが組み上がるまで、実時間で最大 <paramref name="timeoutSeconds"/> 秒待つ。</summary>
        public static IEnumerator WaitReady(Component owner, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (Time.realtimeSinceStartup < deadline && ReadyAnimator(owner) == null)
                yield return null;
        }

        /// <summary>組み上がったアニメーターを返す。組み上がっていなければテストを失敗させる。</summary>
        public static CharacterAnimator RequireReady(Component owner, string message = NotReadyMessage)
        {
            var anim = ReadyAnimator(owner);
            Assert.IsNotNull(anim, message);
            return anim;
        }
    }
}
