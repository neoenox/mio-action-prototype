using System;

namespace Ryona
{
    /// <summary>踏みとどまり入力の判定。</summary>
    public enum PinballTiming
    {
        TooEarly,
        InWindow,
        TooLate,
        RoundClosed
    }

    /// <summary>挟み撃ちの突き飛ばし（ピンポン）の純粋ルール。</summary>
    public readonly struct PinballRules
    {
        public readonly int Rounds;
        public readonly float BaseDistance;
        public readonly float DistanceGain;
        public readonly float BaseSpeed;
        public readonly float SpeedGain;
        public readonly float WindowHalfSeconds;
        public readonly float WindowNarrowing;

        public PinballRules(int rounds, float baseDistance, float distanceGain,
            float baseSpeed, float speedGain, float windowHalfSeconds, float windowNarrowing)
        {
            Rounds = Math.Max(1, rounds);
            BaseDistance = Math.Max(0f, baseDistance);
            DistanceGain = Math.Max(0f, distanceGain);
            BaseSpeed = Math.Max(0.1f, baseSpeed);
            SpeedGain = Math.Max(0f, speedGain);
            WindowHalfSeconds = Math.Max(0.01f, windowHalfSeconds);
            WindowNarrowing = Math.Max(0f, windowNarrowing);
        }

        public static PinballRules Default => new PinballRules(3, 1.15f, 0.18f, 3.2f, 0.5f, 0.12f, 0.02f);

        /// <summary>0 始まりの往復で飛ぶ距離（m）。</summary>
        public float DistanceOf(int round) => BaseDistance + DistanceGain * Math.Max(0, round);

        /// <summary>0 始まりの往復の速さ（m/s）。</summary>
        public float SpeedOf(int round) => BaseSpeed + SpeedGain * Math.Max(0, round);

        /// <summary>往復ごとに狭まる受け止め窓の半幅（秒）。</summary>
        public float HalfWindowOf(int round) =>
            MathfMax(0.03f, WindowHalfSeconds - WindowNarrowing * Math.Max(0, round));

        static float MathfMax(float a, float b) => a > b ? a : b;

        /// <summary>入力時刻と受け止め時刻の差から判定する。</summary>
        public PinballTiming Judge(float inputTime, float catchTime, int round)
        {
            if (round < 0 || round >= Rounds)
                return PinballTiming.RoundClosed;
            float delta = inputTime - catchTime;
            float half = HalfWindowOf(round);
            if (delta < -half)
                return PinballTiming.TooEarly;
            if (delta > half)
                return PinballTiming.TooLate;
            return PinballTiming.InWindow;
        }

        public bool IsFinal(int round) => round >= Rounds - 1;
    }
}
