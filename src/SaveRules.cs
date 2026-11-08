using System;
using System.Globalization;

namespace KerbalDoom
{
    public sealed class MarineState
    {
        public int Health = 100, Armor, Weapon;
        public int[] Ammo = {0, 0, 0, 0};
    }
    public static class SaveRules
    {
        public static string Format(float value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        public static float Read(string text, float fallback, float min, float max)
        {
            float value;
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value)
                ? Math.Max(min, Math.Min(max, value)) : fallback;
        }
    }
}
