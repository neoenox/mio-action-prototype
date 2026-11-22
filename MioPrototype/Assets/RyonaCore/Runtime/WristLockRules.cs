namespace Ryona
{
    /// <summary>手首ロック（拘束リング）の純粋ルール。</summary>
    public readonly struct WristLockRules
    {
        public readonly float DurationSeconds;
        public readonly int InputsToBreak;
        public readonly float MoveScale;
        public readonly float EscapeNeedScale;
        public readonly float CooldownSeconds;
        public readonly float GraceSeconds;

        public WristLockRules(float durationSeconds, int inputsToBreak, float moveScale,
            float escapeNeedScale, float cooldownSeconds, float graceSeconds)
        {
            DurationSeconds = durationSeconds > 0f ? durationSeconds : 4f;
            InputsToBreak = inputsToBreak > 0 ? inputsToBreak : 8;
            MoveScale = moveScale > 0f ? moveScale : 0.7f;
            EscapeNeedScale = escapeNeedScale > 0f ? escapeNeedScale : 1.3f;
            CooldownSeconds = cooldownSeconds > 0f ? cooldownSeconds : 25f;
            GraceSeconds = graceSeconds > 0f ? graceSeconds : 1.5f;
        }

        public static WristLockRules Default => new WristLockRules(4f, 8, 0.7f, 1.3f, 25f, 1.5f);

        public bool IsBroken(int inputs) => inputs >= InputsToBreak;
        public bool IsExpired(float elapsed) => elapsed >= DurationSeconds;
        // -1 は「まだ一度も解除していない」のセンチネル。0 は時刻として有効なので使わない。
        public bool IsInGrace(float now, float endedAt) => endedAt >= 0f && now - endedAt < GraceSeconds;
        public bool CanAttach(float now, float endedAt, bool grappled, bool down)
            => !grappled && !down && !IsInGrace(now, endedAt) && (endedAt < 0f || now - endedAt >= CooldownSeconds);
        public float ScaledEscapeNeed(float need) => need * EscapeNeedScale;
    }
}
