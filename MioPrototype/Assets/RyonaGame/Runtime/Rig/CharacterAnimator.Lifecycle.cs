using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class CharacterAnimator : MonoBehaviour
    {
        void Awake()
        {
            if (Rig == null)
                Rig = GetComponent<ProceduralRig>();
            if (Rig == null)
                Rig = gameObject.AddComponent<ProceduralRig>();
        }

        void Start()
        {
            if (Rig != null && !Rig.Ready)
                Rig.Capture();

            Bind();

            // 読み込み直後の 1 フレームだけ T ポーズが見えるのを防ぐ
            ApplyPose();
        }

        void OnDestroy()
        {
            if (_bound && Stats != null)
            {
                Stats.Staggered -= OnStaggered;
                Stats.HitReceived -= OnHitReceived;
                Stats.Revived -= OnRevived;

                if (Grapple != null)
                {
                    Grapple.Escaped -= OnGrappleEscaped;
                    Grapple.Released -= OnGrappleReleased;
                }
            }
        }

        void Bind()
        {
            if (_bound || Stats == null)
                return;

            // 踏み込みで root が前へ出るので、その分だけ足を残すために必要。
            if (Motor == null)
                Motor = GetComponentInParent<PlayerMotor>();

            if (Grapple == null)
                Grapple = GetComponentInParent<PlayerGrapple>();

            // VRM 側の animator は VrmCharacterSlot が追加するので、Combat が未設定のまま。
            // ここで拾わないと Combat.IsAttacking が常に false になり、
            // State.Attack に入らず「攻撃の判定だけ出て拳が動かない」状態になる。
            if (Combat == null)
                Combat = GetComponentInParent<PlayerCombat>();

            Stats.Staggered += OnStaggered;
            Stats.HitReceived += OnHitReceived;
            Stats.Revived += OnRevived;

            if (Grapple != null)
            {
                Grapple.Escaped += OnGrappleEscaped;
                Grapple.Released += OnGrappleReleased;
            }

            _bound = true;
        }

        void OnRevived()
        {
            // HP 0 の倒れ姿勢を切り上げ、次のフレームで通常の起き上がりに入る。
            if (_state == State.Down)
                _stateDuration = 0f;
        }

        /// <summary>
        /// もがいて振りほどけた瞬間、両手で相手を突き放す。
        /// 振りほどいた直後に構えへ戻るだけだと「するりと抜けた」ように見える。
        /// </summary>
        void OnGrappleEscaped()
        {
            _shoveTime = ShoveSeconds;
        }

        /// <summary>
        /// 時間切れなどで拘束が解けた瞬間、両手で押し返してから構えへ戻る。
        /// いきなり Idle へ戻すと「するりと抜けた」ように見える。
        /// </summary>
        void OnGrappleReleased()
        {
            _shoveTime = ShoveSeconds;
        }

        /// <summary>直前フレームの拘束の種類。引き渡しで姿勢が切り替わった瞬間を検出する。</summary>
        GrappleKind? _lastGrappleKind;
        bool _wasHandingOff;

        /// <summary>
        /// ガード中に殴られたら、腕ごと押し戻される。
        /// 姿勢が変わらないと「受け止めた」感触が出ないため、姿勢側で少しだけ動かす。
        /// </summary>
        void OnHitReceived(HitResult result)
        {
            if (_state == State.Guard)
                _guardRecoil = Mathf.Max(_guardRecoil, GuardRecoilSeconds);
        }

        void OnStaggered(HitResult result)
        {
            // 拘束中もダメージは受けるが、倒れるまでは拘束姿勢を維持する。
            if (Grapple != null && Grapple.IsGrappled && Stats != null && !Stats.IsDead)
                return;
            _tier = result.Stagger;
            _stunPlayed = true;
            CaptureFaceYaw();
            CaptureHurtSide();

            if (CurrentHitZone == PunchZone.Face && _hurtSide != 2)
            {
                _faceFlickAt = Time.time;
                _faceFlickLogged = false;
            }

            // どの向きから・どの場所を殴られたか。見た目の分岐が効いているかを数字で確かめる
            // （<c>-statlog</c> のときだけ。既定は黙る）。
            if (PlayerStats.StatLogging)
            {
                string zone = CurrentHitZone?.ToString() ?? "none";
                Debug.Log($"[Hurt] zone={zone} tier={result.Stagger} " +
                    $"side={_hurtSide} t={Time.time:F2}");
            }

            // ひるみの蓄積はダウン（倒れる）。
            // 倒れている時間は「ダウンの合計 − 起き上がり」で、合計が DownDuration と揃う。
            if (Stats != null && Stats.State != null && Stats.IsDown)
            {
                if (_state != State.Down && (_state != State.GetUp || !Stats.IsGettingUp))
                    SetState(State.Down, Mathf.Max(0.4f, Stats.DownRemaining - GetUpSeconds));
                return;
            }

            SetState(State.Stagger, Mathf.Max(0.12f, result.StunSeconds));
        }

    }
}
