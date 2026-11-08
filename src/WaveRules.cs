using System;

namespace KerbalDoom
{
    public static class WaveRules
    {
        public static int Size(int wave) { return (int)Math.Min(30L, 3 + Math.Max(1L, wave) * 2); }
        public static bool Boss(int wave) { return wave > 0 && wave % 5 == 0; }
        public static int EnemyTier(int wave) { return wave < 3 ? 4 : wave < 5 ? 7 : 8; }
        public static int ReloadAmount(int current, int capacity, int pack) { return Math.Max(0, Math.Min(pack, capacity - current)); }
        public static int ResumeWave(int saved, bool pending) { return Math.Max(0, saved - (pending ? 0 : 1)); }
    }
}
