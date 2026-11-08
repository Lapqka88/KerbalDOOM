using System;

namespace KerbalDoom
{
    public static class CombatRules
    {
        public static bool AmbientAllowed(string bodyName, bool homeWorld, bool terrain, bool landed, bool splashed, double altitude)
        {
            return !homeWorld && !string.Equals(bodyName, "Kerbin", StringComparison.OrdinalIgnoreCase)
                && terrain && landed && !splashed && altitude < 40;
        }
        public static float PartDurability(float mass) { return Math.Max(50, Math.Min(1000, 60 + Math.Max(0, mass) * 120)); }
        public struct Placement { public float X, Y, Width, Height; }
        public static Placement WeaponPlacement(WadArchive.Patch frame, WadArchive.Patch ready, float width, float height, float hud, float scale)
        {
            // Preserve animation offsets but anchor every idle weapon at the same screen centre/bottom.
            float originX = width * .5f - (-ready.Left + ready.Width * .5f) * scale;
            float originY = height - hud - 2 - (-ready.Top + ready.Height) * scale;
            return new Placement { X = originX - frame.Left * scale, Y = originY - frame.Top * scale,
                Width = frame.Width * scale, Height = frame.Height * scale };
        }
        public static int BottomOpaqueRow(WadArchive.Patch patch)
        {
            for (int y = patch.Height - 1; y >= 0; y--)
                for (int x = 0; x < patch.Width; x++)
                    if (patch.Pixels[(y * patch.Width + x) * 4 + 3] != 0) return y;
            return patch.Height - 1;
        }
    }
    public sealed class PartDamageState
    {
        public float Health { get; private set; }
        public PartDamageState(float mass) { Health = CombatRules.PartDurability(mass); }
        public bool Apply(int damage) { Health = Math.Max(0, Health - Math.Max(0, damage)); return Health <= 0; }
    }
}
