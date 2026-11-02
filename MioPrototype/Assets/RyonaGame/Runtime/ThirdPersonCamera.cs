using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 三人称カメラ。被弾時に寄る（仕様書 §2 の「カメラと演出」）ための
    /// 目標距離を外部から差し替えられるようにしてある。
    /// </summary>
    [AddComponentMenu("Ryona/Third Person Camera")]
    public sealed class ThirdPersonCamera : MonoBehaviour
    {
        [Header("References")]
        public Transform Target;

        [Header("Framing")]
        public Vector3 PivotOffset = new Vector3(0f, 1.45f, 0f);
        public float Distance = 3.4f;
        public float MinDistance = 1.2f;
        public float MaxDistance = 5.0f;
        public float MouseSensitivity = 2.2f;
        public float MinPitch = -15f;
        public float MaxPitch = 60f;
        public float FollowSharpness = 16f;
        public LayerMask CollisionMask = ~0;

        [Header("Belt Mode (fixed camera)")]
        [Tooltip("ベルトアクション用の固定カメラ。マウスでは回さず、ステージ軸（X）に沿ってだけ追う")]
        public bool BeltMode = true;

        [Tooltip("見下ろし角。0 で真横、大きいほど俯瞰")]
        public float BeltPitch = 18f;

        [Tooltip("カメラの向き（0 で画面奥が +Z）。真横のベルトなら 0")]
        public float BeltYaw = 0f;

        [Tooltip("画面の中心を合わせる高さ（m）")]
        public float BeltPivotHeight = 1.05f;

        [Tooltip("進行方向へ先回りして見る量（m）")]
        public float BeltLookAhead = 1.2f;

        [Tooltip("追従の速さ。小さいほどゆったり構える")]
        public float BeltFollowSharpness = 5f;

        [Tooltip("カメラが動ける X の範囲（ステージの端）")]
        public float BeltMinX = -12f;
        public float BeltMaxX = 28f;

        /// <summary>関所（<see cref="BeltStageGate"/>）が決める、カメラの進行上限。</summary>
        public float GateMaxX = float.PositiveInfinity;

        /// <summary>関所が決める、カメラの後退下限。</summary>
        public float GateMinX = float.NegativeInfinity;

        /// <summary>カメラが付いてくる距離。画面の端までは自分で歩ける（ベルトの手触り）。</summary>
        public float BeltDeadZone = 1.0f;

        /// <summary>いまの画面の中心 X（追従の目標値）。</summary>
        public float CurrentX => _beltX;

        /// <summary>実際に描画されている画面の中心 X（補間ぶん遅れる）。</summary>
        public float RenderedX => _smoothPivot.x;

        /// <summary>画面の中心を即座に合わせる。ロックした瞬間に端で体が切れないように。</summary>
        public void SnapTo(float x)
        {
            _beltX = x;
            _smoothPivot = new Vector3(x, _smoothPivot.y, _smoothPivot.z);
        }

        /// <summary>
        /// 一度でも構図を決めたか。起動直後はカメラの向きがまだ置かれていないので、
        /// 投影（<see cref="ScreenHalfWidth"/> や画面内判定）が当てにならない。
        /// </summary>
        public bool Framed { get; private set; }

        Camera _cam;

        /// <summary>
        /// 画面の左右の端までの距離（m）。奥行き <paramref name="z"/> に立っている点が
        /// 画面の端に来る X の幅を、実際のカメラの投影から測って返す。
        /// <para>
        /// fov・アスペクト・ヨー・ピッチ・距離を全部織り込んだ値になる。ベルトカメラは
        /// 斜めから見ているので、奥行きが変わると映る幅も変わる。固定値で縛ると
        /// 手前へ歩いたときに画面の外まで出られてしまう（実測）。
        /// </para>
        /// </summary>
        public float ScreenHalfWidth(float z)
        {
            if (_cam == null)
                _cam = GetComponent<Camera>();
            if (_cam == null)
                return Mathf.Max(1f, Distance);

            Vector3 p0 = new Vector3(_beltX, BeltPivotHeight, z);
            Vector3 p1 = p0 + Vector3.right;   // 1m 右の点

            Vector3 v0 = _cam.WorldToViewportPoint(p0);
            Vector3 v1 = _cam.WorldToViewportPoint(p1);

            if (v0.z <= 0f || v1.z <= 0f)
                return Mathf.Max(1f, Distance);

            float perMeter = v1.x - v0.x;   // 1m 進んだときの画面内の増分
            if (perMeter <= 0.0001f)
                return Mathf.Max(1f, Distance);

            float right = (1f - v0.x) / perMeter;
            float left = v0.x / perMeter;

            // 投影が壊れたとき（カメラが点の真上に来た等）に、画面外まで歩ける値へ
            // 飛ばないよう上限を付ける。
            return Mathf.Clamp(Mathf.Min(right, left), 0.5f, 12f);
        }

        float _beltX;

        float _beltLead;

        [Header("Combat Framing (演出用)")]
        [Tooltip("演出倍率の減衰カーブの強さ。大きいほど序盤で一気に抜ける")]
        public float EffectDecay = 4f;

        [Header("Down Framing (倒れているとき)")]
        [Tooltip("倒れたときに見る高さ。腰の高さまで下げる")]
        public float DownPivotHeight = 0.45f;

        [Tooltip("倒れたときの距離の倍率")]
        public float DownDistanceScale = 0.85f;

        [Tooltip("切り替えにかける秒数")]
        public float DownBlendSeconds = 0.45f;

        [Tooltip("倒れたときにカメラを回す角度（度）。顔が見える向きへ寄せる")]
        public float DownYawBias = 0f;

        [Tooltip("倒れたときに見下ろす角度（度）。仰向けの顔は上を向くので、上から覗く")]
        public float DownPitchBias = 20f;

        float _downBlend;
        CharacterAnimator _anim;
        float _nextAnimProbe;

        float _yaw;
        float _pitch = 10f;
        float _targetDistance;
        Vector3 _smoothPivot;
        readonly RaycastHit[] _hits = new RaycastHit[8];
        float _blockLogAt;

        // --- 演出用の状態（Distance とは独立。ユーザーのスクロールを壊さないため） ---

        /// <summary>寄る要求（1 未満）。</summary>
        float _pushScale = 1f;
        float _pushRemaining;
        float _pushDuration;

        /// <summary>引く要求（1 超）。</summary>
        float _pullScale = 1f;
        float _pullRemaining;
        float _pullDuration;

        float _pivotMeters;
        float _pivotRemaining;
        float _pivotDuration;

        /// <summary>腹パン演出などで、画面の中心を一時的に指定した X へ寄せる。</summary>
        float _focusX;
        float _focusPitch;
        float _focusYaw;
        float _focusRemaining;

        // このフレームで実際に使う値（毎フレーム TickEffects が更新する）
        float _distanceScale = 1f;
        float _pivotOffsetY;

        void Awake()
        {
            // シーンに古い値が残っていても効くよう、倒れカメラの値はここで確定させる
            DownPivotHeight = 0.45f;
            DownDistanceScale = 0.85f;
            DownBlendSeconds = 0.45f;
            // 真横からのカメラなので、倒れたときに 180° 回すと反対側へ大きく振れてしまう。
            // 同じ側のまま見下ろす（＝横から倒れた彼女を見る）。
            DownYawBias = 0f;
            DownPitchBias = 20f;

            // 構図の比較用: -camdist=5.2 でカメラの距離だけ差し替える（既定は scene の値）
            foreach (string a in System.Environment.GetCommandLineArgs())
            {
                const string key = "-camdist=";

                if (a.StartsWith(key, System.StringComparison.OrdinalIgnoreCase)
                    && float.TryParse(a.Substring(key.Length),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float d)
                    && d > 1f)
                {
                    Distance = d;
                    Debug.Log($"[Cam] Distance={Distance:F2}（起動引数で上書き）");
                }
            }

            // 実際に効いている距離を必ず出す（シーン値と起動引数のどちらが勝ったか分かるように）
            if (Distance > 1f && Mathf.Abs(Distance - 5.2f) > 0.01f)
                Debug.Log($"[Cam] Distance={Distance:F2}（既定 5.2 と違う値）");
            else
                Debug.Log($"[Cam] Distance={Distance:F2}");

            _beltX = Target != null ? Target.position.x : transform.position.x;
            _targetDistance = Distance;
            _smoothPivot = Pivot();
        }

        void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void LateUpdate()
        {
            // 演出の残り時間は実時間で進める（ヒットストップ中もフレーミングは動かす）
            TickEffects(Time.unscaledDeltaTime);

            if (Target == null)
                return;

            // 固定カメラ（ベルト）: マウスでは回さない。
            if (BeltMode)
            {
                TickBelt();
                Framed = true;
                return;
            }

            Vector2 look = InputReader.LookDelta() * MouseSensitivity;
            _yaw += look.x;
            _pitch = Mathf.Clamp(_pitch - look.y, MinPitch, MaxPitch);

            TickDown(Time.deltaTime);

            Vector3 pivot = Pivot();
            _smoothPivot = Vector3.Lerp(_smoothPivot, pivot, 1f - Mathf.Exp(-FollowSharpness * Time.deltaTime));

            Quaternion rotation = Quaternion.Euler(
                _pitch + DownPitchBias * _downBlend,
                _yaw + DownYawBias * _downBlend,
                0f);
            Vector3 direction = rotation * Vector3.back;

            // ユーザーが決めた Distance は書き換えず、演出倍率だけを掛ける。
            // 寄りで体にめり込まないよう下限だけ守る（引きは一時的に上限を超えてよい）。
            float scaledDistance = Mathf.Max(MinDistance,
                Distance * _distanceScale * Mathf.Lerp(1f, DownDistanceScale, _downBlend));

            float desired = scaledDistance;

            // 敵はカメラの障害物にしない。囲まれると敵の体でカメラが押され、
            // 気づくと地面すれすれの真上からの画になってしまう（実測）。
            int count = Physics.SphereCastNonAlloc(
                _smoothPivot, 0.22f, direction, _hits,
                scaledDistance, CollisionMask, QueryTriggerInteraction.Ignore);

            float nearest = scaledDistance;
            for (int i = 0; i < count; i++)
            {
                Collider c = _hits[i].collider;
                if (c == null)
                    continue;

                if (c.GetComponentInParent<EnemyFigure>() != null)
                    continue;

                // SphereCastNonAlloc は「最初から重なっている物」を distance 0 で返す。
                // 自キャラのコライダーがこれに当たり、自分の体でカメラが 1.2m まで寄る。
                if (_hits[i].distance <= 0.0001f)
                    continue;

                if (_hits[i].distance < nearest)
                    nearest = _hits[i].distance;
            }

            if (nearest < scaledDistance)
            {
                desired = Mathf.Max(MinDistance, nearest - 0.08f);

                // 半分以上寄るのは普通は起きない（壁際か、拾ってはいけない物を拾っている）。
                if (desired < scaledDistance * 0.5f && Time.time - _blockLogAt > 1f)
                {
                    _blockLogAt = Time.time;
                    string who = "?";
                    for (int i = 0; i < count; i++)
                        if (_hits[i].collider != null &&
                            Mathf.Abs(_hits[i].distance - nearest) < 0.01f)
                        {
                            var t = _hits[i].collider.transform;
                            who = $"{_hits[i].collider.name} <- {t.parent?.name}";
                            break;
                        }
                    Debug.Log($"[Cam] blocked by {who} nearest={nearest:F2} desired={desired:F2} want={scaledDistance:F2}");
                }
            }

            _targetDistance = Mathf.Lerp(_targetDistance, desired, 1f - Mathf.Exp(-22f * Time.deltaTime));

            transform.position = _smoothPivot + direction * _targetDistance;
            transform.rotation = Quaternion.LookRotation(_smoothPivot - transform.position, Vector3.up);

            Framed = true;
        }

        /// <summary>
        /// ベルト用の固定カメラ。マウスでは回さず、ステージ軸（X）に沿ってだけ追う。
        /// 「画面は固定、世界のほうが横に流れる」がベルトアクションの見え方。
        /// </summary>
        void TickBelt()
        {
            // 向いている方向へ少しだけ先回りして見る（歩き出しで画面が付いてくる）
            float facing = Mathf.Clamp(Target.forward.x, -1f, 1f);
            _beltLead = Mathf.Lerp(_beltLead, facing * BeltLookAhead,
                1f - Mathf.Exp(-4f * Time.deltaTime));

            bool focusing = _focusRemaining > 0f;

            // 腹パン中は見下ろしを弱め、折れて下を向いた顔を画面に入れる。
            float pitch = BeltPitch + (focusing ? _focusPitch : 0f);
            float distance = Mathf.Max(1f, Distance * _distanceScale);
            float height = BeltPivotHeight;

            // 倒れているあいだは少し引いて、体全体が入るようにする
            if (_downBlend > 0.001f)
            {
                distance *= Mathf.Lerp(1f, 1.12f, _downBlend);
                height = Mathf.Lerp(height, 0.62f, _downBlend);
            }

            // 画面の端までは自分で歩ける。中央から離れすぎたらカメラが付いてくる。
            // 敵が出ているあいだは関所が X を固定するので、1 画面ぶんロックされる。
            float px = Target.position.x;
            float dead = Mathf.Max(0f, BeltDeadZone);

            // dead zone は「望むカメラ中心（彼女 + LookAhead 先）」に掛ける。
            // px に掛けると、歩いている間だけ彼女が (lead - dead) ぶん先へ出て、
            // 立ち止まった瞬間に構図が戻る（実測 vx 0.46 / 0.26 と歩くたびにずれた）。
            // 中心に掛ければ、止まっていても歩いていても lead - dead = 0.8m 左で一定。
            float want = px + _beltLead;

            if (want > _beltX + dead)
                _beltX = want - dead;
            else if (want < _beltX - dead)
                _beltX = want + dead;

            // 腹パン演出中は、画面ロックの中心ではなくプレイヤーを中央に置く。
            // ロック中は彼女が画面の端にいることがあり、寄るだけでは小さく写るため。
            if (focusing)
            {
                _focusRemaining = Mathf.Max(0f, _focusRemaining - Time.unscaledDeltaTime);
                _beltX = _focusX;
            }

            float minX = Mathf.Max(BeltMinX, Mathf.Min(GateMinX, GateMaxX));
            float maxX = Mathf.Min(BeltMaxX, Mathf.Max(GateMinX, GateMaxX));

            _beltX = Mathf.Clamp(_beltX, minX, maxX);

            Vector3 pivot = new Vector3(
                Mathf.Clamp(_beltX, minX, maxX),
                height,
                0f);

            // 寄せる間は追従を速くして、演出の 0.1 秒で彼女を中央へ寄せる。
            float follow = focusing ? 26f : BeltFollowSharpness;
            float followDt = focusing ? Time.unscaledDeltaTime : Time.deltaTime;

            _smoothPivot = Vector3.Lerp(_smoothPivot, pivot,
                1f - Mathf.Exp(-follow * followDt));

            Quaternion rotation = Quaternion.Euler(pitch, BeltYaw + (focusing ? _focusYaw : 0f), 0f);
            Vector3 direction = rotation * Vector3.back;

            transform.position = _smoothPivot + direction * distance;
            transform.rotation = Quaternion.LookRotation(_smoothPivot - transform.position, Vector3.up);
        }

        Vector3 Pivot()
        {
            Vector3 basePivot = Target.position + PivotOffset + Vector3.up * _pivotOffsetY;
            if (_downBlend <= 0.001f)
                return basePivot;

            // 倒れたら腰を見る。立ったままの高さを見ていると、体が画面の下に落ちて見えない
            Vector3 downPivot = basePivot;
            if (_anim != null && _anim.Rig != null && _anim.Rig.Ready)
                downPivot = _anim.Rig.Hips.position + Vector3.up * 0.10f;
            else
                downPivot = Target.position + Vector3.up * DownPivotHeight;

            return Vector3.Lerp(basePivot, downPivot, _downBlend);
        }

        /// <summary>倒れている度合い（0..1）を進める。倒れたらカメラを低く寄せる。</summary>
        void TickDown(float deltaTime)
        {
            if (_anim == null && Time.time >= _nextAnimProbe)
            {
                _nextAnimProbe = Time.time + 0.5f;
                if (Target != null)
                    _anim = Target.GetComponentInChildren<CharacterAnimator>();
            }

            bool down = _anim != null && _anim.Current == CharacterAnimator.State.Down;
            float step = Mathf.Max(0.0001f, deltaTime) / Mathf.Max(0.05f, DownBlendSeconds);
            _downBlend = Mathf.MoveTowards(_downBlend, down ? 1f : 0f, step);
        }

        /// <summary>被弾時に一時的に寄るための距離指定。</summary>
        public void SetDistance(float distance)
        {
            Distance = Mathf.Clamp(distance, MinDistance, MaxDistance);
        }

        /// <summary>演出で掛かっている距離倍率（1 が通常）。</summary>
        public float EffectDistanceScale => _distanceScale;

        /// <summary>演出で掛かっているピボットの上下オフセット（m）。</summary>
        public float EffectPivotOffset => _pivotOffsetY;

        /// <summary>
        /// 演出として距離を一時的に倍率で変える（1 未満で寄る、1 超で引く）。時間は実時間で数える。
        ///
        /// <see cref="Distance"/> は一切書き換えないので、ホイールでの距離変更と干渉しない。
        /// 連続で呼ばれた場合は「より寄る倍率」と「残り時間の長い方」を採用し、
        /// 引く要求（1 超）は寄る要求が生きている間は無視する（当てた寄りを被弾の引きで消さない）。
        /// <paramref name="seconds"/> が 0 以下なら、その場で通常の距離へ戻す。
        /// </summary>
        /// <param name="scale">距離に掛ける倍率。1 で通常、0.9 で 1 割寄る。</param>
        /// <param name="seconds">効果を持たせる秒数（実時間）。</param>
        public void PushDistance(float scale, float seconds)
        {
            if (seconds <= 0f)
            {
                ResetDistanceEffect();
                return;
            }

            if (scale <= 0f)
                return;   // 0 以下はカメラが体に入ってしまうので無視する

            if (scale < 1f)
            {
                // 減衰済みの見た目と比べる。古い強い寄りが急に戻って跳ねるのを防ぐ。
                float live = _distanceScale < 1f ? _distanceScale : 1f;
                _pushScale = Mathf.Min(live, scale);

                _pushRemaining = Mathf.Max(_pushRemaining, seconds);
                _pushDuration = Mathf.Max(_pushDuration, _pushRemaining);
            }
            else
            {
                // 引く側は「強い方」を採用する（弱い引きで上書きされると見下ろしが効かない）
                float live = _distanceScale > 1f ? _distanceScale : 1f;
                _pullScale = Mathf.Max(live, scale);

                _pullRemaining = Mathf.Max(_pullRemaining, seconds);
                _pullDuration = Mathf.Max(_pullDuration, _pullRemaining);
            }
        }

        /// <summary>
        /// 演出としてピボットを上下させる（負で下がる＝見下ろし）。時間は実時間で数える。
        /// 連続で呼ばれた場合は「絶対値の大きい方（強く効く方）」と「残り時間の長い方」を採用する。
        /// <paramref name="seconds"/> が 0 以下なら、その場で通常の高さへ戻す。
        /// </summary>
        /// <param name="meters">上下の移動量（m）。負で下がる。</param>
        /// <param name="seconds">効果を持たせる秒数（実時間）。</param>
        public void PushPivot(float meters, float seconds)
        {
            if (seconds <= 0f)
            {
                ResetPivotEffect();
                return;
            }

            if (Mathf.Approximately(meters, 0f))
                return;

            if (Mathf.Abs(meters) >= Mathf.Abs(_pivotOffsetY))
                _pivotMeters = meters;

            _pivotRemaining = Mathf.Max(_pivotRemaining, seconds);
            _pivotDuration = Mathf.Max(_pivotDuration, _pivotRemaining);
        }

        /// <summary>画面の中心を一時的に指定した X へ寄せているか。</summary>
        public bool IsFocusing => _focusRemaining > 0f;

        /// <summary>
        /// 画面の中心を一時的に指定した X へ寄せる（腹パン演出でプレイヤーを中央に置く）。
        /// <see cref="BeltScreenLock"/> の範囲内の X を渡すこと。時間は実時間で数える。
        /// </summary>
        public void PushFocus(float x, float seconds, float pitchOffset = 0f, float yawOffset = 0f)
        {
            if (seconds <= 0f)
            {
                _focusRemaining = 0f;
                return;
            }

            _focusX = x;
            _focusPitch = pitchOffset;
            _focusYaw = yawOffset;
            _focusRemaining = Mathf.Max(_focusRemaining, seconds);
        }

        void OnDisable()
        {
            // タイトルへ戻ったときなどに演出が残らないよう、必ず既定値へ戻す
            ResetDistanceEffect();
            ResetPivotEffect();
            _focusRemaining = 0f;
            _focusPitch = 0f;
            _focusYaw = 0f;
        }

        /// <summary>演出の残り時間を進め、このフレームで使う倍率・オフセットを決める。</summary>
        void TickEffects(float deltaTime)
        {
            float dt = Mathf.Max(0f, deltaTime);

            _pushRemaining = Mathf.Max(0f, _pushRemaining - dt);
            _pullRemaining = Mathf.Max(0f, _pullRemaining - dt);
            _pivotRemaining = Mathf.Max(0f, _pivotRemaining - dt);

            float pushIn = Mathf.LerpUnclamped(1f, _pushScale, Envelope(_pushRemaining, _pushDuration));
            float pullOut = Mathf.LerpUnclamped(1f, _pullScale, Envelope(_pullRemaining, _pullDuration));

            // 寄りが生きている間は寄りを優先する
            _distanceScale = pushIn < 1f ? pushIn : pullOut;
            _pivotOffsetY = Mathf.LerpUnclamped(0f, _pivotMeters, Envelope(_pivotRemaining, _pivotDuration));
        }

        /// <summary>
        /// 0..1 の減衰。残り時間いっぱいで 1、切れると 0 になり、指数的に抜ける。
        /// 端で 0 に落ちるので、終わった瞬間に値が飛ばない。
        /// </summary>
        float Envelope(float remaining, float duration)
        {
            if (remaining <= 0f || duration <= 0f)
                return 0f;

            float decay = Mathf.Max(0.01f, EffectDecay);
            float edge = Mathf.Exp(-decay);
            float t = Mathf.Clamp01(remaining / duration);

            return (Mathf.Exp(-decay * (1f - t)) - edge) / (1f - edge);
        }

        void ResetDistanceEffect()
        {
            _pushScale = 1f;
            _pushRemaining = 0f;
            _pushDuration = 0f;

            _pullScale = 1f;
            _pullRemaining = 0f;
            _pullDuration = 0f;

            _distanceScale = 1f;
        }

        void ResetPivotEffect()
        {
            _pivotMeters = 0f;
            _pivotRemaining = 0f;
            _pivotDuration = 0f;

            _pivotOffsetY = 0f;
        }
    }
}
