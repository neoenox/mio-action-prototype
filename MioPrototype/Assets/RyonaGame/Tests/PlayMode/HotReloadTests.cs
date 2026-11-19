using NUnit.Framework;
using Ryona.Game;
using UnityEngine;

namespace Ryona.Tests
{
    /// <summary>
    /// 設定ホットリロード（<see cref="RyonaConfig.OnConfigChanged"/>）の回帰テスト。
    ///
    /// インスペクタ編集 → <c>OnValidate</c> の発火そのものはエディタ UI 依存なので自動化できない。
    /// ここでは「通知が届いたら再適用される」「無効化中の変更が再有効化で反映される」を固定する。
    /// </summary>
    public sealed class HotReloadTests
    {
        RyonaConfig _original;
        RyonaConfig _config;
        GameObject _go;

        [SetUp]
        public void SetUp()
        {
            _original = RyonaConfig.Instance;
            _config = ScriptableObject.CreateInstance<RyonaConfig>();
            RyonaConfig.Instance = _config;
            _go = new GameObject("hot-reload-test");
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                Object.DestroyImmediate(_go);
            if (_config != null)
                Object.DestroyImmediate(_config);
            RyonaConfig.Instance = _original;
        }

        [Test]
        public void NotifyChanged_ReappliesLiveComponent()
        {
            _config.Feel.HitShake = 0.01f;
            var feel = _go.AddComponent<CombatFeel>();

            Assert.AreEqual(0.01f, feel.HitShake, 1e-4f, "追加直後は Awake が適用する");

            _config.Feel.HitShake = 0.33f;
            RyonaConfig.NotifyChanged(_config);

            Assert.AreEqual(0.33f, feel.HitShake, 1e-4f, "通知で再生中のコンポーネントへ再適用される");
        }

        [Test]
        public void ReEnable_AppliesChangeMadeWhileDisabled()
        {
            _config.Feel.HitShake = 0.01f;
            var feel = _go.AddComponent<CombatFeel>();

            feel.enabled = false;
            _config.Feel.HitShake = 0.44f;
            RyonaConfig.NotifyChanged(_config);
            Assert.AreEqual(0.01f, feel.HitShake, 1e-4f, "無効中は再適用されない");

            feel.enabled = true;
            Assert.AreEqual(0.44f, feel.HitShake, 1e-4f, "再有効化で最新の設定が入る");
        }
    }
}
