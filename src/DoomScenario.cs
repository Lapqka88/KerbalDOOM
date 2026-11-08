using System;
using System.Collections.Generic;
using System.Globalization;

namespace KerbalDoom
{
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.SPACECENTER, GameScenes.FLIGHT, GameScenes.TRACKSTATION)]
    public sealed class DoomScenario : ScenarioModule
    {
        public static DoomScenario Instance;
        public readonly Dictionary<string, MarineState> Marines = new Dictionary<string, MarineState>();
        public int BestWave, Wave;
        public bool Defending, WavePending = true;
        public string DefendedVessel = "";
        public float Sensitivity = 2, Movement = 1, WeaponSize = 1, ShipDamage = 1, SpawnInterval = 35, LandingGrace = 30;
        public int EncounterLimit = 12;
        public bool Ambient = true;
        public bool HasSettings;
        public long GlobalRevision;
        public ConfigNode ImportedMap;
        public override void OnAwake() { base.OnAwake(); Instance = this; }
        public MarineState Marine(string name)
        {
            MarineState state;
            if (!Marines.TryGetValue(name, out state)) { state = new MarineState(); Marines[name] = state; }
            return state;
        }
        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node); Instance = this;
            HasSettings = node.HasValue("sensitivity");
            long.TryParse(node.GetValue("globalRevision"), out GlobalRevision);
            ImportedMap = node.GetNode("DOOM_MAP") == null ? null : node.GetNode("DOOM_MAP").CreateCopy();
            int.TryParse(node.GetValue("bestWave"), out BestWave); int.TryParse(node.GetValue("wave"), out Wave);
            BestWave = Math.Max(0, Math.Min(100000, BestWave)); Wave = Math.Max(0, Math.Min(100000, Wave));
            bool.TryParse(node.GetValue("defending"), out Defending); DefendedVessel = node.GetValue("defendedVessel") ?? "";
            bool pending; if (bool.TryParse(node.GetValue("wavePending"), out pending)) WavePending = pending;
            Sensitivity = Read(node, "sensitivity", 2, .2f, 6); Movement = Read(node, "movement", 1, .5f, 3);
            WeaponSize = Read(node, "weaponSize", 1, .5f, 1.5f); ShipDamage = Read(node, "shipDamage", 1, 0, 3);
            SpawnInterval = Read(node, "spawnInterval", 35, 10, 120); LandingGrace = Read(node, "landingGrace", 30, 5, 120);
            EncounterLimit = (int)Read(node, "encounterLimit", 12, 1, 40);
            bool flag; if (bool.TryParse(node.GetValue("ambient"), out flag)) Ambient = flag;
            Marines.Clear();
            foreach (ConfigNode marine in node.GetNodes("MARINE"))
            {
                string key = marine.GetValue("name"); if (string.IsNullOrEmpty(key)) continue;
                var state = new MarineState { Health = (int)Read(marine, "health", 100, 1, 100), Armor = (int)Read(marine, "armor", 0, 0, 200), Weapon = (int)Read(marine, "weapon", 0, 0, 7) };
                for (int i = 0; i < 4; i++) state.Ammo[i] = (int)Read(marine, "ammo" + i, 0, 0, DoomInventory.AmmoCaps[i]);
                Marines[key] = state;
            }
        }
        private static float Read(ConfigNode node, string key, float fallback, float min, float max)
        {
            return SaveRules.Read(node.GetValue(key), fallback, min, max);
        }
        public override void OnSave(ConfigNode node)
        {
            if (KerbalDoomAddon.Instance != null) KerbalDoomAddon.Instance.SaveMarine();
            base.OnSave(node);
            if (KerbalDoomAddon.Instance != null) KerbalDoomAddon.Instance.SaveImportedMap();
            if (ImportedMap != null) node.AddNode(ImportedMap.CreateCopy());
            node.AddValue("globalRevision", GlobalRevision);
            node.AddValue("bestWave", BestWave); node.AddValue("wave", Wave); node.AddValue("defending", Defending); node.AddValue("defendedVessel", DefendedVessel);
            node.AddValue("wavePending", WavePending);
            node.AddValue("sensitivity", SaveRules.Format(Sensitivity)); node.AddValue("movement", SaveRules.Format(Movement)); node.AddValue("weaponSize", SaveRules.Format(WeaponSize)); node.AddValue("shipDamage", SaveRules.Format(ShipDamage));
            node.AddValue("spawnInterval", SaveRules.Format(SpawnInterval)); node.AddValue("landingGrace", SaveRules.Format(LandingGrace)); node.AddValue("encounterLimit", EncounterLimit); node.AddValue("ambient", Ambient);
            foreach (var pair in Marines)
            {
                ConfigNode marine = node.AddNode("MARINE"); marine.AddValue("name", pair.Key); marine.AddValue("health", pair.Value.Health);
                marine.AddValue("armor", pair.Value.Armor); marine.AddValue("weapon", pair.Value.Weapon);
                for (int i = 0; i < 4; i++) marine.AddValue("ammo" + i, pair.Value.Ammo[i]);
            }
        }
        private void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
