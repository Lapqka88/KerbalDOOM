using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace KerbalDoom
{
    // Main-menu defaults, independent of a loaded save. Preserve unrelated configuration keys on write.
    public sealed class DoomGlobalSettings
    {
        private static DoomGlobalSettings current;
        public static DoomGlobalSettings Current { get { return current ?? Load(); } }
        public static string ConfigPath { get { return Path.Combine(KSPUtil.ApplicationRootPath, "GameData/KerbalDoom/PluginData/settings.cfg"); } }
        public string WadPath = "", ToggleKey = "F8", MenuKey = "F7";
        public bool AmbientSpawns = true, EasterEggs = true;
        public int AmbientLimit = 12;
        public float Sensitivity = 2, Movement = 1, WeaponSize = 1, ShipDamage = 1, SpawnInterval = 35, LandingGrace = 30;
        public long Revision;
        public string LoadError = "";

        public static DoomGlobalSettings Load()
        {
            DoomGlobalSettings result = new DoomGlobalSettings();
            try
            {
                ConfigNode document = File.Exists(ConfigPath) ? ConfigNode.Load(ConfigPath) : null;
                ConfigNode node = document == null ? null : document.GetNode("KERBAL_DOOM") ?? document;
                if (node != null)
                {
                    result.WadPath = node.GetValue("wadPath") ?? result.WadPath;
                    result.ToggleKey = ValidKey(node.GetValue("toggleKey"), "F8");
                    result.MenuKey = ValidKey(node.GetValue("menuKey"), "F7");
                    result.AmbientSpawns = ReadBool(node, "ambientSpawns", true);
                    result.EasterEggs = ReadBool(node, "easterEggs", true);
                    result.AmbientLimit = (int)SaveRules.Read(node.GetValue("ambientLimit"), 12, 1, 40);
                    result.Sensitivity = SaveRules.Read(node.GetValue("sensitivity"), 2, .2f, 6);
                    result.Movement = SaveRules.Read(node.GetValue("movement"), 1, .5f, 3);
                    result.WeaponSize = SaveRules.Read(node.GetValue("weaponSize"), 1, .5f, 1.5f);
                    result.ShipDamage = SaveRules.Read(node.GetValue("shipDamage"), 1, 0, 3);
                    result.SpawnInterval = SaveRules.Read(node.GetValue("spawnInterval"), 35, 10, 120);
                    result.LandingGrace = SaveRules.Read(node.GetValue("landingGrace"), 30, 5, 120);
                    long revision;
                    if (long.TryParse(node.GetValue("globalRevision"), NumberStyles.Integer, CultureInfo.InvariantCulture, out revision))
                        result.Revision = Math.Max(0, Math.Min(DateTime.MaxValue.Ticks, revision));
                }
            }
            catch (Exception e)
            {
                result.LoadError = e.Message;
                Debug.LogWarning("[KerbalDoom] Global settings load: " + e.Message);
            }
            current = result;
            return result;
        }

        public void Save()
        {
            Normalize();
            ConfigNode document = File.Exists(ConfigPath) ? ConfigNode.Load(ConfigPath) : null;
            if (document == null && File.Exists(ConfigPath)) throw new InvalidDataException("Existing settings.cfg could not be read; it was left unchanged");
            if (document == null) document = new ConfigNode();
            ConfigNode node = document.GetNode("KERBAL_DOOM");
            if (node == null)
            {
                // Standard KSP config is a root document containing a named child.
                node = document.AddNode("KERBAL_DOOM");
            }
            Set(node, "wadPath", WadPath); Set(node, "toggleKey", ToggleKey); Set(node, "menuKey", MenuKey);
            Set(node, "ambientSpawns", AmbientSpawns ? "true" : "false"); Set(node, "ambientLimit", AmbientLimit.ToString(CultureInfo.InvariantCulture));
            Set(node, "sensitivity", SaveRules.Format(Sensitivity)); Set(node, "movement", SaveRules.Format(Movement));
            Set(node, "weaponSize", SaveRules.Format(WeaponSize)); Set(node, "shipDamage", SaveRules.Format(ShipDamage));
            Set(node, "spawnInterval", SaveRules.Format(SpawnInterval)); Set(node, "landingGrace", SaveRules.Format(LandingGrace));
            Set(node, "easterEggs", EasterEggs ? "true" : "false");
            Revision = Math.Max(Math.Min(Revision, DateTime.MaxValue.Ticks), DateTime.UtcNow.Ticks) + 1;
            Set(node, "globalRevision", Revision.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
            string staged = ConfigPath + ".pending";
            document.Save(staged);
            if (!File.Exists(staged)) throw new IOException("KSP did not write the settings file");
            File.Copy(staged, ConfigPath, true); File.Delete(staged);
            LoadError = ""; current = this;
            Debug.Log("[KerbalDoom] Main-menu settings saved; revision=" + Revision);
        }

        public void ResetOptions()
        {
            AmbientSpawns = true; EasterEggs = true; AmbientLimit = 12;
            Sensitivity = 2; Movement = 1; WeaponSize = 1; ShipDamage = 1; SpawnInterval = 35; LandingGrace = 30;
        }
        public DoomGlobalSettings Copy() { return (DoomGlobalSettings)MemberwiseClone(); }

        private void Normalize()
        {
            WadPath = (WadPath ?? "").Replace("\r", "").Replace("\n", "").Trim();
            ToggleKey = ValidKey(ToggleKey, "F8"); MenuKey = ValidKey(MenuKey, "F7");
            if (string.Equals(ToggleKey, MenuKey, StringComparison.OrdinalIgnoreCase)) { ToggleKey = "F8"; MenuKey = "F7"; }
            AmbientLimit = Math.Max(1, Math.Min(40, AmbientLimit));
            Sensitivity = Bound(Sensitivity, 2, .2f, 6); Movement = Bound(Movement, 1, .5f, 3);
            WeaponSize = Bound(WeaponSize, 1, .5f, 1.5f); ShipDamage = Bound(ShipDamage, 1, 0, 3);
            SpawnInterval = Bound(SpawnInterval, 35, 10, 120); LandingGrace = Bound(LandingGrace, 30, 5, 120);
        }
        private static float Bound(float value, float fallback, float min, float max)
        { return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value)); }
        private static string ValidKey(string value, string fallback)
        {
            try
            {
                KeyCode code = (KeyCode)Enum.Parse(typeof(KeyCode), value ?? "", true);
                return code == KeyCode.None || !Enum.IsDefined(typeof(KeyCode), code) ? fallback : code.ToString();
            }
            catch { return fallback; }
        }
        private static bool ReadBool(ConfigNode node, string key, bool fallback)
        { bool value; return bool.TryParse(node.GetValue(key), out value) ? value : fallback; }
        private static void Set(ConfigNode node, string key, string value) { node.SetValue(key, value, true); }
    }
}
