using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 足音と、倒れたときの「ドサッ」。
    ///
    /// 足音は歩行アニメーションの接地（<see cref="CharacterAnimator.Footstep"/>）で鳴らす。
    /// 一定間隔のタイマーで鳴らすと、速度を変えたときに足と音がずれるため。
    /// 打撃音などは <see cref="CombatFeel"/> / <see cref="EnemyBrain"/> が
    /// <see cref="ProceduralSfx"/> を通して鳴らす（ここでは扱わない）。
    /// </summary>
    [AddComponentMenu("Ryona/Footstep Sfx")]
    public sealed class FootstepSfx : MonoBehaviour
    {
        [Header("Body Drop")]
        /// <summary>
        /// 着地の通知（<see cref="CharacterAnimator.BodyLanded"/>）が来なかったときの代わりの秒数。
        /// 通常は体が床に着いた瞬間に鳴らす（以前は倒れ始めの 1.1 秒後に固定で鳴らしていたので、
        /// 吹き飛ばされて 0.4 秒で着いた後、0.7 秒遅れて音が来ていた）。
        /// </summary>
        public float BodyDropDelay = 1.6f;

        PlayerStats _player;
        CharacterAnimator _anim;
        bool _subscribed;

        float _pendingBody;
        bool _landedThisDown;
        bool _sawDown;
        float _retry;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InstallSceneHook()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
            UnityEngine.SceneManagement.LoadSceneMode mode) => Boot();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<FootstepSfx>() != null)
                return;

            var go = new GameObject("Footstep Sfx");
            go.AddComponent<FootstepSfx>();
        }

        void Update()
        {
            // VRM は非同期で載るので、見つかるまでは 0.5 秒おきに探す
            if (_player == null || !_subscribed)
            {
                _retry -= Time.deltaTime;
                if (_retry <= 0f)
                {
                    _retry = 0.5f;
                    Resolve();
                }
            }

            if (_player == null || _player.State == null)
                return;

            var state = _player.State;

            // 倒れる音。倒れ始めてから、体が床に着く頃に鳴らす。
            if (state.IsDown && !_sawDown)
            {
                _sawDown = true;
                _landedThisDown = false;
                _pendingBody = BodyDropDelay;
            }
            else if (!state.IsDown)
            {
                _sawDown = false;
            }

            if (_pendingBody > 0f)
            {
                _pendingBody -= Time.deltaTime;
                if (_pendingBody <= 0f && !_landedThisDown)
                    Play(SfxId.BodyDrop, 1f, 1f);
            }
        }

        /// <summary>体が床に着いた瞬間。音と画面の揺れを同時に出す。</summary>
        void OnBodyLanded(float strength)
        {
            if (_landedThisDown)
                return;
            _landedThisDown = true;
            _pendingBody = 0f;
            Play(SfxId.BodyDrop, Mathf.Lerp(0.7f, 1f, strength), 1f);
            FindFirstObjectByType<CameraShake>()?.Shake(0.05f * strength, 0.14f);
        }

        /// <summary>
        /// VRM は非同期で載るので、Animator は毎回探し直す（載った時点で 1 度だけ購読）。
        /// </summary>
        void Resolve()
        {
            if (_player == null)
                _player = FindFirstObjectByType<PlayerStats>();

            // 載せ替えで Animator が消えたら購読をやり直す
            if (_anim == null)
                _subscribed = false;

            if (_anim == null && _player != null)
                _anim = _player.GetComponentInChildren<CharacterAnimator>(true);

            if (_anim != null && !_subscribed)
            {
                _anim.Footstep += OnFootstep;
                _anim.BodyLanded += OnBodyLanded;
                _subscribed = true;

                GameLog.Log($"[Sfx] 足音の購読 {_anim.name}");
            }
        }

        void OnDestroy()
        {
            if (_anim != null && _subscribed)
            {
                _anim.Footstep -= OnFootstep;
                _anim.BodyLanded -= OnBodyLanded;
            }
        }

        void OnFootstep(int side)
        {
            if (_player == null || _player.State == null)
                return;

            var state = _player.State;
            if (!state.IsAlive || state.IsDown || _player.IsStunned)
                return;

            // 速いほど強く。左右で少し高さを変えると、同じ音の連打に聞こえない。
            float speed = Mathf.InverseLerp(0.5f, 4.5f, _anim != null ? _anim.PlanarSpeed : 1.5f);
            float volume = Mathf.Lerp(0.45f, 1f, speed);
            float pitch = side == 0 ? 1.03f : 0.97f;

            Play(SfxId.Step, volume, pitch);
        }

        static void Play(SfxId id, float volume, float pitch)
        {
            var sfx = ProceduralSfx.Instance;
            if (sfx == null)
                return;

            sfx.Play(id, volume, pitch);
        }
    }
}
