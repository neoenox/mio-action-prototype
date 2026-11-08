using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>
    /// テスト中の音。
    ///
    /// PlayMode テストは被弾ボイスを何度も鳴らすので、そのままだと耳障りで作業にならない。
    /// <see cref="TestScene.LoadPrototype"/> が <see cref="AudioListener.volume"/> を 0 にするので、
    /// テスト中は音が出ない。声のイベント（<c>ProceduralSfx.HurtVoicePlayed</c>）は止めないため、
    /// 表情のテストには影響しない。
    /// </summary>
    public sealed class TestAudioTests
    {
        [UnityTest]
        public IEnumerator テスト中は消音されている()
        {
            yield return TestScene.LoadPrototype();

            Assert.AreEqual(0f, AudioListener.volume, 1e-4f,
                "テスト中は AudioListener.volume を 0 にして音を出さない");
        }
    }
}
