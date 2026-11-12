using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 手続き的モーション。アニメーションクリップを使わず、ボーンを直接動かす。
    ///
    /// 状態: 立ち / 歩き / 怯み(4段) / ダウン / 攻撃
    /// すべて Rig の「向き指定」と「2ボーンIK」だけで組んである。
    /// </summary>
    [AddComponentMenu("Ryona/Character Animator")]
    [DefaultExecutionOrder(20000)]
    public sealed partial class CharacterAnimator : MonoBehaviour
    {
        public enum State
        {
            Idle = 0,
            Walk = 1,
            Stagger = 2,
            Down = 3,
            Attack = 4,
            Guard = 5,
            GetUp = 6,

            /// <summary>敵に掴まれてもがいている。移動も攻撃もできない。</summary>
            Grabbed = 7
        }

        [Header("References")]
        public ProceduralRig Rig;
        public PlayerStats Stats;
        public PlayerCombat Combat;
        public PlayerMotor Motor;

        /// <summary>掴まれているかを見る。未設定なら Bind で探す。</summary>
        public PlayerGrapple Grapple;

        /// <summary>
        /// 歩行で足が接地した瞬間。(0=左, 1=右)
        /// 足音をアニメーションに合わせるために使う（速度を変えても音がずれない）。
        /// </summary>
        public event System.Action<int> Footstep;

        /// <summary>実際の移動速度（足音の強さに使う）。</summary>
        public float PlanarSpeed => _planarSpeed;

        [Tooltip("Combat が無いとき（プレビュー）に使う攻撃データ")]
        public AttackData AttackOverride;

        /// <summary>
        /// もがきの脈（0〜1）をプレビューで直接入れる。0 未満で無効（実際の Grapple を見る）。
        /// 本番ではレバガチャの入力で 1 まで上がり、時間で減る。
        /// </summary>
        [System.NonSerialized] public float StrugglePulseOverride = -1f;

        [Header("Walk")]
        public float StrideLength = 0.42f;
        public float StepHeight = 0.075f;
        public float ArmSwing = 0.85f;

        [Header("Blend")]
        [Tooltip("状態が切り替わったとき、前の姿勢から寄せる秒数")]
        public float IdleBlendSeconds = 0.16f;
        public float AttackBlendSeconds = 0.06f;
        public float DownBlendSeconds = 0.10f;

        [Header("Guard")]
        [Tooltip("ガードを出してから構え終わるまで")]
        public float GuardBlendSeconds = 0.09f;

        [Tooltip("ガード中に殴られたとき、押し返される演出の長さ")]
        public float GuardRecoilSeconds = 0.18f;

        [Header("Down Pose (倒れたときの腕)")]
        [Tooltip("仰向けに倒れたときの左腕の向き。立ち姿勢のローカル方向（-Y が下）")]
        public Vector3 DownArmLUpper = new Vector3(0.85f, -0.50f, -0.25f);
        public Vector3 DownArmLFore = new Vector3(0.60f, -0.30f, -0.55f);
        public Vector3 DownArmRUpper = new Vector3(-0.85f, -0.50f, 0.25f);
        public Vector3 DownArmRFore = new Vector3(-0.60f, -0.30f, 0.55f);

        [Header("Stagger")]
        public float LightBend = 22f;
        public float MediumBend = 42f;
        public float HeavyBend = 60f;

        [Header("Get Up")]
        [Tooltip("倒れてから立ち上がり終わるまでの秒数")]
        public float GetUpSeconds = 0.80f;

        public State Current => _state;
        public float StateTime => _stateTime;
        public float StateDuration => _stateDuration;

        // 腹パン演出（後ろ拘束中に別の敵の腹パンチを受けたとき）の残り時間。
        float _heldBellyPunchRemaining;
        float _heldBellyFatigue;
        float _heldBellyCarry;
        float _heldBellyPunchTotal;

        float _heldFacePunchRemaining;
        float _heldFacePunchTotal;
        int _heldFacePunchStartFrame;

        /// <summary>顔面パンチの強さ（0.2〜1.5）。姿勢と表情の振れ幅に掛ける。</summary>
        float _heldFacePunchStrength = 1f;

        /// <summary>パンチの向き（彼女のローカル XZ）が渡されているか。</summary>
        bool _hasFacePunchDirection;

        /// <summary>拳が頭を押す向き（彼女のローカル）。x=左右、z=前後。</summary>
        float _facePunchLocalX;
        float _facePunchLocalZ;

        // 投げられて、正面の敵に腹を差し出す体勢への移行（0..1）。
        // パンチの「く」の字とは別物で、折れはしない。コンボのあいだ保持する。
        bool _heldThrowLeadOn;
        float _heldThrowLead;

        /// <summary>いま腹パンの「く」の字オーバーレイ中か。</summary>
        public bool IsHeldBellyPunch => _heldBellyPunchRemaining > 0f;

        /// <summary>いま拘束中の顔面パンチ（首が手前へ振れる）オーバーレイ中か。</summary>
        public bool IsHeldFacePunch => _heldFacePunchRemaining > 0f;

        /// <summary>
        /// 投げられて、腹を差し出す体勢に入っている最中か（コンボのあいだ保持）。
        /// 食らいの姿勢ではないので、折れ・怯み・痛み表情・ヒットストップを伴わない。
        /// </summary>
        public bool IsHeldThrowLead => _heldThrowLeadOn || _heldThrowLead > 0.001f;

        /// <summary>いま投げの体勢移行がどこまで入っているか（0..1）。</summary>
        public float HeldThrowLead => _heldThrowLead;

        /// <summary>
        /// 拘束中に顔面を殴られたときの首の振れ（0..1）。当たった瞬間から動き、約 0.03 秒で振り切る。
        /// 通常の顔面パンチ（<see cref="ApplyFaceFlick"/>）と同じ向き＝カメラ側へ顔を背ける。
        /// テストと Editor 診断が数値で確認できるように公開している。
        /// </summary>
        public float HeldFacePunchFlick
        {
            get
            {
                if (_heldFacePunchTotal <= 0f || _heldFacePunchRemaining <= 0f)
                    return 0f;

                float t = _heldFacePunchTotal - _heldFacePunchRemaining;
                float inT = Mathf.SmoothStep(0.28f, 1f, t / 0.032f);
                float outT = 1f - Mathf.SmoothStep(0f, 1f, (t - 0.10f) / 0.25f);
                return Mathf.Clamp01(inT * outT);
            }
        }

        /// <summary>
        /// 顔面パンチの衝撃（0..1）。頭が一気に振れてから戻るまで。姿勢側がこれで頭・首を振る。
        /// </summary>
        public float HeldFacePunchImpulse
        {
            get
            {
                if (_heldFacePunchTotal <= 0f || _heldFacePunchRemaining <= 0f)
                    return 0f;

                float t = _heldFacePunchTotal - _heldFacePunchRemaining;
                // 接触の静止画にも小さな反応を出し、再開後に残りの振れへつなげる。
                float inT = Mathf.SmoothStep(0.24f, 1f, t / 0.034f);
                float outT = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.13f) / 0.26f));
                return Mathf.Clamp01(inT * outT) * _heldFacePunchStrength;
            }
        }

        /// <summary>
        /// 顔面パンチを受けたあと、次の一発まで歯を食いしばって踏ん張る（0..1）。
        /// 「やられているだけ」に見えないよう、目を開けて睨み返す姿勢に使う。
        /// </summary>
        public float HeldFacePunchBrace
        {
            get
            {
                if (_heldFacePunchTotal <= 0f || _heldFacePunchRemaining <= 0f)
                    return 0f;

                float t = _heldFacePunchTotal - _heldFacePunchRemaining;
                float up = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.16f) / 0.10f));
                float down = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.36f) / 0.19f));
                return Mathf.Clamp01(up * down);
            }
        }

    }
}
