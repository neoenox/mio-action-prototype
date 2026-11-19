using System;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// ゲーム全体の調整値。**設定の真実源はこのアセット 1 つ**。
    ///
    /// <para>
    /// 同じ値が「シーンの保存値」「フィールド初期化子」「<c>Awake</c> のハードコード」に
    /// 散っていると、どれが勝つか分からず、インスペクタで調整しても効かない事故になる
    /// （旧 <see cref="ThirdPersonCamera"/> がそうだった）。
    /// ここへ集約し、各コンポーネントは起動時にこのアセットを読むだけにする。
    /// コンポーネント側のフィールドは <c>NonSerialized</c> の実行時値で、シーンの値は使わない。
    /// </para>
    ///
    /// <para><b>ルール</b></para>
    /// <list type="bullet">
    /// <item>調整は <c>Assets/RyonaGame/Resources/RyonaConfig.asset</c> を編集する。</item>
    /// <item>実行時の差し替えは起動引数だけ（例: <c>-camdist</c>）。「アセット → 起動引数」の順。</item>
    /// <item>アセットが無いとき（空のテストシーン等）は、ここの既定値で動く。</item>
    /// <item>既定値は「移行時点のシーンで実際に効いていた値」。コードの初期化子と違うものがあるので、
    /// 勝手にコード側の値へ戻さないこと（例: <c>BeltLock.HalfWidth</c> はシーンで 5、旧初期化子は 3.2）。</item>
    /// </list>
    /// </summary>
    [CreateAssetMenu(menuName = "Ryona/Config", fileName = "RyonaConfig")]
    public sealed class RyonaConfig : ScriptableObject
    {
        // --- カメラ -------------------------------------------------------------

        /// <summary>
        /// 三人称カメラ（<see cref="ThirdPersonCamera"/>）の調整値。
        /// </summary>
        [Serializable]
        public sealed class CameraSettings
        {
            [Header("Framing")]
            public Vector3 PivotOffset = new Vector3(0f, 1.45f, 0f);
            public float Distance = 5.2f;
            public float MinDistance = 1.2f;
            public float MaxDistance = 6f;
            public float MouseSensitivity = 2.2f;
            public float MinPitch = -15f;
            public float MaxPitch = 60f;
            public float FollowSharpness = 16f;

            [Header("Belt Mode (fixed camera)")]
            [Tooltip("ベルトアクション用の固定カメラ。マウスでは回さず、ステージ軸（X）に沿ってだけ追う")]
            public bool BeltMode = true;

            [Tooltip("移動中や戦闘中に敵が見切れすぎないよう、ベルトカメラの手動ズームをここまでにする（m）")]
            public float BeltMinZoomDistance = 3.8f;

            [Tooltip("腹パン・顔パンなど、顔と拳を見せる寄りの演出で許す最短距離（m）")]
            public float BeltCloseUpMinDistance = 0.82f;

            [Tooltip("見下ろし角。0 で真横、大きいほど俯瞰")]
            public float BeltPitch = 14f;

            [Tooltip("カメラの向き（0 で画面奥が +Z）。真横のベルトなら 0")]
            public float BeltYaw = 0f;

            [Tooltip("画面の中心を合わせる高さ（m）")]
            public float BeltPivotHeight = 0.86f;

            [Tooltip("進行方向へ先回りして見る量（m）")]
            public float BeltLookAhead = 1.8f;

            [Tooltip("追従の速さ。小さいほどゆったり構える")]
            public float BeltFollowSharpness = 8f;

            [Tooltip("倒れている間の追従の速さ。吹き飛ばされても画面から切れないようにする")]
            public float DownFollowSharpness = 16f;

            [Tooltip("カメラが動ける X の範囲（ステージの端）")]
            public float BeltMinX = -16f;
            public float BeltMaxX = 34f;

            [Tooltip("カメラが付いてくる距離。画面の端までは自分で歩ける（ベルトの手触り）")]
            public float BeltDeadZone = 1f;

            [Header("Combat Framing (演出用)")]
            [Tooltip("演出倍率の減衰カーブの強さ。大きいほど序盤で一気に抜ける")]
            public float EffectDecay = 4f;

            [Header("Down Framing (倒れているとき)")]
            [Tooltip("倒れたときに見る高さ。腰の高さまで下げる")]
            public float DownPivotHeight = 0.45f;

            [Tooltip("倒れたときの距離の倍率")]
            public float DownDistanceScale = 0.85f;

            [Tooltip("切り替えにかかる秒数")]
            public float DownBlendSeconds = 0.45f;

            [Tooltip("倒れたときにカメラを回す角度（度）。顔が見える向きへ寄せる")]
            public float DownYawBias = 0f;

            [Tooltip("倒れたときに見下ろす角度（度）。仰向けの顔は上を向くので、上から覗く")]
            public float DownPitchBias = 20f;
        }

        // --- プレイヤー ---------------------------------------------------------

        /// <summary>横移動（<see cref="PlayerMotor"/>）の調整値。</summary>
        [Serializable]
        public sealed class MotorSettings
        {
            public float BaseSpeed = 4.2f;
            public float Acceleration = 28f;
            public float TurnSpeed = 14f;
            public float Gravity = -22f;

            [Tooltip("ベルト用の向き。奥行き（画面の上下）に動くときは向きを変えず、左右入力でだけ向き直る")]
            public bool BeltFacing = true;
        }

        /// <summary>プレイヤーの攻撃（<see cref="PlayerCombat"/>）の調整値。</summary>
        [Serializable]
        public sealed class CombatSettings
        {
            [Header("Auto Aim")]
            public float AutoAimRange = 2.2f;
            public float AutoAimMaxDegrees = 70f;

            [Header("Recovery")]
            [Tooltip("硬直後、踏み込みで出た足が戻るまで次の攻撃を受け付けない許容量")]
            public float SettleEpsilon = 0.001f;

            [Tooltip("硬直中に押された攻撃を覚えておく時間（先行入力）")]
            public float InputBufferSeconds = 0.15f;

            [Tooltip("3段コンボの受付時間。この秒数内に弱を押すと次の段へ進む")]
            public float ComboWindowSeconds = 0.45f;
        }

        /// <summary>VRM の差し替え（<see cref="VrmCharacterSlot"/>）の調整値。</summary>
        [Serializable]
        public sealed class VrmSlotSettings
        {
            public float YawOffset = 0f;
            public float Scale = 1f;
        }

        // --- 敵 ----------------------------------------------------------------

        /// <summary>敵 AI（<see cref="EnemyBrain"/>）の調整値。</summary>
        [Serializable]
        public sealed class EnemySettings
        {
            [Header("Tuning")]
            [Tooltip("歩き（追跡）の速度 m/s")]
            public float MoveSpeed = 2.6f;

            [Tooltip("攻撃間合いの基準値。実間合いは AttackRange * 0.85 になる")]
            public float AttackRange = 1.45f;

            [Tooltip("攻撃別の予備動作が 0 以下のときのフォールバック秒数")]
            public float WindupSeconds = 0.45f;

            [Tooltip("攻撃後の硬直秒数")]
            public float RecoverSeconds = 0.7f;

            [Tooltip("旋回の速さ（Slerp 係数）")]
            public float TurnSpeed = 8f;

            [Header("Attack: Jab")]
            public float JabSeconds = 0.35f;
            public float JabRange = 1.45f;

            [Header("Attack: Heavy")]
            public float HeavySeconds = 0.75f;
            public float HeavyRange = 1.7f;

            [Header("Attack: Break")]
            public float BreakSeconds = 0.55f;
            public float BreakRange = 1.55f;

            [Header("Attack: Grab")]
            [Tooltip("掴みの予備動作秒数。長めにして「来る」と分かるようにする")]
            public float GrabSeconds = 0.55f;

            [Tooltip("掴みが届く距離 m。至近距離でだけ出る")]
            public float GrabRange = 1.15f;

            [Tooltip("掴み自体のダメージ。本命は掴んだ後のレバガチャ")]
            public float GrabDamage = 3f;

            [Tooltip("着弾判定の余裕 m。見た目と当たり判定のズレを吸収する")]
            public float StrikeTolerance = 0.35f;

            [Tooltip("予備動作まで見せてから振らずに引く確率（0 で無効）。1 回の接近につき 1 度だけ")]
            [Range(0f, 1f)] public float FeintChance = 0.22f;

            [Tooltip("フェイント後に再び踏み込むまでの秒数。短いほど「二度撃ち」に見える")]
            public float FeintDelaySeconds = 0.45f;

            [Tooltip("彼女の体との最小距離（m）。0 以下なら押し出さない")]
            public float MinPlayerGap = 0f;

            [Header("Damage")]
            [Tooltip("Jab のダメージ")]
            public float JabDamage = 3f;

            [Tooltip("Heavy のダメージ。ひるみは AttackLibrary.Heavy の値を使う")]
            public float HeavyDamage = 7f;

            [Tooltip("Break のダメージ")]
            public float BreakDamage = 5f;

            [Header("Strafe")]
            [Tooltip("横回りを続ける秒数の基準値。実際は 0.75〜1.25 倍でばらつく")]
            public float StrafeSeconds = 1.1f;

            [Tooltip("横回り中の移動速度倍率")]
            public float StrafeSpeedScale = 0.75f;

            [Tooltip("横回りに入るたび、間合いを詰め直す確率")]
            [Range(0f, 1f)] public float StrafeCloseInChance = 0.35f;

            [Tooltip("間合いを詰め直すときの距離倍率（1 未満で近づく）")]
            public float StrafeCloseInScale = 0.85f;

            [Tooltip("順番待ちの敵が立つ距離（間合いの何倍か）")]
            public float WaitingRingScale = 2f;

            [Header("Telegraph (予備動作の予告)")]
            public float TelegraphStartScale = 0.35f;
            public float TelegraphMaxScale = 3.5f;
            public float TelegraphMaxHoldSeconds = 0.1f;
            public Color TelegraphStartColor = new Color(0.45f, 0.45f, 0.45f, 1f);
            public Color TelegraphColor = new Color(1f, 0.28f, 0.18f, 1f);

            [Header("Timing")]
            [Tooltip("硬直・怯みが明けてから次の予備動作に入るまでの間隔")]
            public float AttackCooldown = 1.4f;

            [Header("Stagger")]
            public float StaggerSecondsSmall = 0.2f;
            public float StaggerSecondsMedium = 0.4f;
            public float StaggerSecondsLarge = 0.8f;

            [Tooltip("立ち上がりにかける秒数")]
            public float RiseSeconds = 1.6f;

            [Header("Punch Zone")]
            [Range(0f, 1f)] public float JabFaceChance = 0.65f;
            [Range(0f, 1f)] public float HeavyFaceChance = 0.35f;
            [Tooltip("拳が狙いの座標にこの距離まで届いたら当たり（m）")]
            public float PunchHitRadius = 0.3f;

            /// <summary>
            /// 個体ごとの実行時コピーを作る。敵はアセットを直接書き換えず、
            /// このコピーを <see cref="EnemyBrain"/> が持つ（テストの個体別調整のため）。
            /// </summary>
            public EnemySettings Clone() => (EnemySettings)MemberwiseClone();
        }

        /// <summary>敵の被弾演出（<see cref="EnemyReaction"/>）の調整値。</summary>
        [Serializable]
        public sealed class EnemyReactionSettings
        {
            public float FlashSeconds = 0.08f;
            public float SquashSeconds = 0.12f;
            public float KnockbackDistance = 0.06f;
            public Color FlashColor = Color.white;
        }

        /// <summary>
        /// 捕まえられ方（拘束）の調整値。足取りのように「敵が仕掛け、彼女が掴まれる」ものをまとめる。
        /// </summary>
        [Serializable]
        public sealed class GrappleSettings
        {
            [Header("Ankle Grab (足取り)")]
            [Tooltip("振りほどくのに必要な入力回数。短い拘束なので少なめ")]
            [Min(1)] public int AnkleMashInputs = 8;

            [Tooltip("拘束時間（秒）。振りほどけないと前へ転ぶ")]
            public float AnkleLimitSeconds = 1.6f;

            [Tooltip("掴んだ側と彼女の距離（m）。低く沈んだ敵の腕が足首まで届く")]
            public float AnkleStandoff = 0.85f;

            [Tooltip("走って逃げる彼女を背後から掴んだとき、足取りになる割合。残りは羽交い締め")]
            [Range(0f, 1f)] public float AnkleChance = 0.5f;
        }

        /// <summary>敵の同時攻撃の制限（<see cref="EnemyGroup"/>）の調整値。</summary>
        [Serializable]
        public sealed class GroupSettings
        {
            [Tooltip("同時に予備動作〜攻撃に入れる敵の数。SPEC §5 の上限は 2")]
            [Min(1)] public int MaxAttackers = 2;
        }

        // --- 進行・画面 ---------------------------------------------------------

        /// <summary>ウェーブ進行（<see cref="EncounterDirector"/>）の調整値。</summary>
        [Serializable]
        public sealed class EncounterSettings
        {
            [Tooltip("次の一団が出るまでに進む距離（1 画面ぶん）")]
            public float ScreenSpan = 8f;

            [Tooltip("画面の右端の、これだけ外から入ってくる（m）")]
            public float SpawnAhead = 5.4f;

            [Tooltip("プレイヤーの目の前に出ないための最小距離（m）")]
            public float MinSpawnGap = 2.2f;

            [Tooltip("倒れてから「倒れる音」を鳴らすまでの秒数")]
            public float BodyDropDelay = 0.7f;

            [Range(0f, 1f)] public float BodyDropVolume = 0.95f;
        }

        /// <summary>画面ロック（<see cref="BeltScreenLock"/>）の調整値。</summary>
        [Serializable]
        public sealed class BeltLockSettings
        {
            [Tooltip("ロック時に見せる画面の半幅（m）。旧シーン値は 5（スクリプト初期化子は 3.2）")]
            public float HalfWidth = 5f;

            [Tooltip("画面端の余白（m）。旧シーン値は 1.6（スクリプト初期化子は 0.9）")]
            public float EdgeMargin = 1.6f;

            [Tooltip("関所を置く距離の幅（m）")]
            public float GateRange = 3f;

            [Tooltip("敵の走査間隔（秒）")]
            public float ScanSeconds = 0.1f;

            [Tooltip("この距離まで敵が入ったらロックを検討する（m）")]
            public float EngageRange = 4.5f;

            [Tooltip("画面端からこのぶん内側で敵を検知する（m）")]
            public float EngageScreenMargin = 0f;
        }

        // --- 演出 --------------------------------------------------------------

        /// <summary>ヒットストップ・揺れ・声（<see cref="CombatFeel"/>）の調整値。</summary>
        [Serializable]
        public sealed class FeelSettings
        {
            [Header("Hit Stop (seconds)")]
            public float HitHitStop = 0.065f;
            public float ReceivedHitStop = 0.07f;

            [Header("Camera Shake (meter, seconds)")]
            public float HitShake = 0.06f;
            public float HitShakeSeconds = 0.16f;
            public float ReceivedShake = 0.12f;
            public float ReceivedShakeSeconds = 0.26f;

            [Header("Heavy Attack")]
            [Tooltip("Heavy のときの揺れ倍率")]
            public float HeavyShakeScale = 1.4f;

            [Tooltip("Heavy のときのヒットストップ倍率")]
            public float HeavyHitStopScale = 1.3f;

            [Header("Voice")]
            [Tooltip("被弾の声を続けて出さない間隔（秒）。連続で殴られても「あっあっあっ」にしない")]
            public float HurtVoiceInterval = 0.55f;
        }

        /// <summary>被弾時のカメラワーク（<see cref="CameraDirector"/>）の調整値。</summary>
        [Serializable]
        public sealed class CameraDirectionSettings
        {
            [Header("Push In")]
            public float PushInScale = 0.9f;
            public float PushInSeconds = 0.2f;

            [Header("Hit")]
            public float HitPullScale = 1.1f;
            public float HitPullSeconds = 0.3f;
            public float HitPivotDrop = -0.1f;
            public float HitPivotSeconds = 0.3f;

            [Header("Stagger")]
            public float StaggerPullScale = 1.16f;
            public float StaggerPullSeconds = 0.45f;
            public float StaggerPivotDrop = -0.18f;
            public float StaggerPivotSeconds = 0.45f;

            [Header("Death")]
            public float DeathPullScale = 1.25f;
            public float DeathPivotDrop = -0.85f;
            public float DeathPivotSeconds = 0.5f;
            public float DeathHoldSeconds = 0.5f;
        }

        // --- アセット -----------------------------------------------------------

        [Header("Camera")]
        public CameraSettings Camera = new CameraSettings();

        [Header("Player")]
        public MotorSettings Motor = new MotorSettings();
        public VrmSlotSettings VrmSlot = new VrmSlotSettings();
        public CombatSettings Combat = new CombatSettings();

        [Header("Enemy")]
        public EnemySettings Enemy = new EnemySettings();
        public EnemyReactionSettings EnemyReaction = new EnemyReactionSettings();
        public GroupSettings Group = new GroupSettings();

        [Header("Grapple")]
        public GrappleSettings Grapple = new GrappleSettings();

        [Header("Stage / UI")]
        public EncounterSettings Encounter = new EncounterSettings();
        public BeltLockSettings BeltLock = new BeltLockSettings();

        [Header("Feel")]
        public FeelSettings Feel = new FeelSettings();
        public CameraDirectionSettings CameraDirection = new CameraDirectionSettings();

        /// <summary>
        /// Resources 上のアセット名。<c>Assets/RyonaGame/Resources/{ResourcePath}.asset</c>。
        /// Editor の作成ツールもこの名前からパスを組み立てる。
        /// </summary>
        public const string ResourcePath = "RyonaConfig";

        static RyonaConfig _instance;

        /// <summary>
        /// 唯一の設定アセット。無ければ既定値のインスタンスを返す（テスト・空シーン用）。
        /// 参照は各コンポーネントの <c>Awake</c> で 1 度取るだけでよい。
        /// エディタでアセットを編集すると <see cref="OnConfigChanged"/> が発火する。
        /// </summary>
        public static RyonaConfig Instance
        {
            get
            {
                if (_instance != null)
                    return _instance;

                _instance = Resources.Load<RyonaConfig>(ResourcePath) ?? CreateInstance<RyonaConfig>();
                return _instance;
            }
            internal set => _instance = value;
        }

        /// <summary>
        /// 設定変更時のコールバック。<see cref="ConfigurableMonoBehaviour"/> が購読する。
        /// </summary>
        public static event Action<RyonaConfig> OnConfigChanged;

        /// <summary>
        /// 設定の再適用を通知する。通常はエディタの <c>OnValidate</c> から呼ばれる。
        /// </summary>
        internal static void NotifyChanged(RyonaConfig config)
        {
            OnConfigChanged?.Invoke(config != null ? config : Instance);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // インスペクタで値を変えた瞬間、再生中のコンポーネントへ再適用する。
            NotifyChanged(this);
        }
#endif
    }
}
