using System;

namespace Ryona
{
    /// <summary>
    /// ウェーブ（アリーナ）の配置を決める純ロジック。
    /// <para>
    /// 1 アリーナに <see cref="WavesPerArena"/> 波。中心は <see cref="StartX"/> から
    /// <see cref="Pitch"/> ずつ並ぶ。最後のアリーナを超える波（無限モード）は
    /// 最後のアリーナに留まる（<see cref="ArenaOf"/>）。
    /// </para>
    /// </summary>
    public readonly struct ArenaPlan
    {
        public readonly float StartX;
        public readonly float Pitch;
        public readonly int WavesPerArena;
        public readonly int ArenaCount;

        public static ArenaPlan Default => new ArenaPlan(-6f, 10f, 2, 5);

        public ArenaPlan(float startX, float pitch, int wavesPerArena, int arenaCount)
        {
            StartX = startX;
            Pitch = pitch > 0f ? pitch : 10f;
            WavesPerArena = Math.Max(1, wavesPerArena);
            ArenaCount = Math.Max(1, arenaCount);
        }

        /// <summary>アリーナ <paramref name="arena"/>（0 始まり）の中心 X。</summary>
        public float CenterOf(int arena) => StartX + Math.Max(0, arena) * Pitch;

        /// <summary>波番号（1 始まり）が属するアリーナ。最後を超える場合は最後に留まる。</summary>
        public int ArenaOf(int wave)
        {
            int index = (Math.Max(1, wave) - 1) / WavesPerArena;
            return Math.Min(index, ArenaCount - 1);
        }

        /// <summary>この波が最後のアリーナか（無限モードで留まるか）。</summary>
        public bool IsLastArenaOf(int wave) => ArenaOf(wave) >= ArenaCount - 1;

        /// <summary>敵が入ってくる X。アリーナ右端の少し外。</summary>
        public float EntryX(int arena, float screenHalfWidth, float extra)
            => CenterOf(arena) + Math.Max(0f, screenHalfWidth) + Math.Max(0f, extra);
    }
}
