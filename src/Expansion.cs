using System;
using System.Collections.Generic;
using UnityEngine;

namespace KerbalDoom
{
    public sealed partial class KerbalDoomAddon
    {
        public static KerbalDoomAddon Instance;
        private string marineKey = "";
        private DoomScenario boundScenario;
        private readonly Dictionary<string, MarineState> fallbackMarines = new Dictionary<string, MarineState>();
        private float sensitivity = 2, movementSpeed = 1, weaponSize = 1, shipDamage = 1, encounterInterval = 35, landingGrace = 30;
        private int menuTab;
        private bool defending, wavePending = true, resumeChecked;
        private Vessel defendedShip;
        private int wave, bestWave;
        private float waveRest = 15;
        private readonly List<DoomEnemy> waveEnemies = new List<DoomEnemy>();
        private readonly List<DoomPickup> pickups = new List<DoomPickup>();
        private readonly List<DoomEffect> effects = new List<DoomEffect>();
        private Vector2 equipmentScroll;
        private Guid encounterObserver;
        private bool observerWasLanded;

        private void RestoreSettings()
        {
            DoomScenario s = DoomScenario.Instance;
            if (s == null) return;
            DoomGlobalSettings global = DoomGlobalSettings.Current;
            if (!s.HasSettings || global.Revision > s.GlobalRevision)
            {
                s.Ambient = global.AmbientSpawns; s.EncounterLimit = global.AmbientLimit;
                s.Sensitivity = global.Sensitivity; s.Movement = global.Movement; s.WeaponSize = global.WeaponSize;
                s.ShipDamage = global.ShipDamage; s.SpawnInterval = global.SpawnInterval; s.LandingGrace = global.LandingGrace;
                s.HasSettings = true; s.GlobalRevision = global.Revision;
            }
            sensitivity = s.Sensitivity; movementSpeed = s.Movement; weaponSize = s.WeaponSize; shipDamage = s.ShipDamage;
            encounterInterval = s.SpawnInterval; landingGrace = s.LandingGrace; ambientEnabled = s.Ambient; ambientLimit = s.EncounterLimit;
            bestWave = s.BestWave;
        }
        private void PersistSettings()
        {
            DoomScenario s = DoomScenario.Instance; if (s == null) return;
            s.Sensitivity = sensitivity; s.Movement = movementSpeed; s.WeaponSize = weaponSize; s.ShipDamage = shipDamage;
            s.SpawnInterval = encounterInterval; s.LandingGrace = landingGrace; s.Ambient = ambientEnabled; s.EncounterLimit = ambientLimit;
            s.BestWave = bestWave;
            if (resumeChecked || defending)
            { s.Wave = wave; s.Defending = defending; s.DefendedVessel = defendedShip == null ? "" : defendedShip.id.ToString(); s.WavePending = wavePending; }
        }
        private MarineState CurrentMarine()
        {
            if (DoomScenario.Instance != null) return DoomScenario.Instance.Marine(marineKey);
            MarineState state;
            if (!fallbackMarines.TryGetValue(marineKey, out state)) { state = new MarineState(); fallbackMarines[marineKey] = state; }
            return state;
        }
        public void SaveMarine()
        {
            if (string.IsNullOrEmpty(marineKey)) return;
            MarineState state = CurrentMarine(); state.Health = Mathf.Clamp(health, 1, 100); state.Armor = armor; state.Weapon = weapon;
            Array.Copy(ammo, state.Ammo, 4);
            PersistSettings();
        }
        private void SyncMarine()
        {
            if (DoomScenario.Instance != boundScenario)
            { boundScenario = DoomScenario.Instance; marineKey = ""; RestoreSettings(); }
            string key = DoomInventory.KerbalKey(Observer);
            if (key == marineKey) return;
            SaveMarine(); marineKey = key;
            if (string.IsNullOrEmpty(key)) return;
            MarineState state = CurrentMarine(); health = state.Health; armor = state.Armor; weapon = state.Weapon;
            Array.Copy(state.Ammo, ammo, 4);
        }
        private bool HasWeapon(int index) { return DoomInventory.Owns(player != null ? player : Observer, index); }
        private bool ReloadFromInventory()
        {
            if (Observer == null) return false;
            int slot = WeaponSpec.All[weapon].AmmoSlot;
            if (WeaponSpec.All[weapon].Cost == 0) return true;
            int add = WaveRules.ReloadAmount(ammo[slot], DoomInventory.AmmoCaps[slot], DoomInventory.PackAmounts[slot]);
            if (add == 0 || !DoomInventory.Consume(Observer, DoomInventory.Ammo[slot])) return false;
            ammo[slot] += add; SaveMarine(); Play("DSWPNUP", .5f); return true;
        }
        private void UseSupply(string item)
        {
            if (Observer == null) return;
            if (item == "kdMedkit" && health >= 100 || item == "kdArmor" && armor >= 200) return;
            if (!DoomInventory.Consume(Observer, item)) { Notice("Предмета нет в инвентаре кербала"); return; }
            if (item == "kdMedkit") health = Mathf.Min(100, health + 50); else armor = Mathf.Min(200, armor + 100);
            SaveMarine(); Play("DSITEMUP", .5f);
        }
        private void GiveStarter()
        {
            if (Observer == null) return;
            int n = 0;
            foreach (string item in new [] {"kdPistol", "kdShotgun", "kdBullets", "kdShells", "kdMedkit"})
                if ((item != "kdPistol" && item != "kdShotgun" || DoomInventory.Count(Observer, item) == 0) && DoomInventory.Give(Observer, item)) n++;
            Notice("Добавлено в штатный инвентарь: " + n + ". Оружие: 1/2, патроны: R, аптечка: H");
        }
        private void TickExpansion(float dt)
        {
            if (Observer.id != encounterObserver || (!Observer.isEVA && Observer.Landed && !observerWasLanded))
                nextAmbient = Mathf.Max(nextAmbient, landingGrace);
            encounterObserver = Observer.id; observerWasLanded = Observer.Landed;
            if (!resumeChecked && DoomScenario.Instance != null)
            {
                DoomScenario s = DoomScenario.Instance;
                if (!s.Defending) resumeChecked = true;
                else foreach (Vessel v in FlightGlobals.VesselsLoaded)
                    if (v != null && v.id.ToString() == s.DefendedVessel)
                    { defendedShip = v; defending = true; wave = WaveRules.ResumeWave(s.Wave, s.WavePending); wavePending = true; waveRest = 15; resumeChecked = true; Notice("Защита корабля: следующая волна через 15 секунд"); break; }
            }
            TickDefense(dt);
            for (int i = pickups.Count - 1; i >= 0; i--)
            {
                DoomPickup pickup = pickups[i]; pickup.Age += dt;
                if (pickup.Age > 180 || pickup.Root == null) { pickup.Dispose(); pickups.RemoveAt(i); continue; }
                if (Observer.isEVA && (pickup.Position.World - Observer.transform.position).sqrMagnitude < 2.5f && DoomInventory.Give(Observer, pickup.Item))
                { Play("DSITEMUP", .4f); pickup.Dispose(); pickups.RemoveAt(i); }
            }
            for (int i = effects.Count - 1; i >= 0; i--)
                if (!effects[i].Tick(dt)) { effects[i].Dispose(); effects.RemoveAt(i); }
            if (active && assets != null && ClockSecondChanged())
            {
                // Re-evaluate possession after stock transfers, without granting weapons from cached save state.
                if (!HasWeapon(weapon)) for (int i = 0; i < WeaponSpec.All.Length; i++) if (HasWeapon(i)) { weapon = i; break; }
            }
        }
        private int lastSecond = -1;
        private bool ClockSecondChanged() { int n = (int)clock; if (n == lastSecond) return false; lastSecond = n; return true; }
        public void OnEnemyKilled(DoomEnemy enemy)
        {
            if (pickups.Count >= 64 || assets == null) return;
            string item;
            float roll = UnityEngine.Random.value;
            if (roll < .15f) item = "kdMedkit";
            else if (roll < .25f) item = "kdArmor";
            else if (enemy.Spec.Prefix == "SPOS") item = "kdShells";
            else if (enemy.Spec.Prefix == "CYBR") item = "kdCells";
            else item = "kdBullets";
            Vector3 pos = enemy.Position.World;
            if (enemy.Spec.Flying) pos -= UpAt(pos) * 1.5f;
            pickups.Add(new DoomPickup(worldBody, pos + UpAt(pos) * .15f, item, assets, spriteMaterial));
        }
        private void DrawPickups(Transform camera)
        { foreach (DoomPickup pickup in pickups) pickup.Draw(camera, UpAt(pickup.Position.World)); }
        public void PlayAt(string name, Vector3 position, float volume)
        {
            if (assets == null || string.IsNullOrEmpty(name)) return;
            AudioClip clip = assets.Sound(name); if (clip == null) return;
            GameObject emitter = new GameObject("KerbalDoom/SpatialSound"); emitter.transform.position = position;
            AudioSource source = emitter.AddComponent<AudioSource>(); source.clip = clip; source.spatialBlend = 1;
            source.minDistance = 3; source.maxDistance = 140; source.rolloffMode = AudioRolloffMode.Linear;
            source.volume = volume * GameSettings.SHIP_VOLUME; source.Play(); Destroy(emitter, clip.length + .1f);
        }
        private void AddEffect(Vector3 start, Vector3 end, float life, float radius, Color color, string kind = null)
        {
            if (effects.Count >= 96 || worldBody == null) return;
            effects.Add(kind == null ? new DoomEffect(worldBody, start, end, life, radius, color) :
                new DoomEffect(worldBody, start, end, life, radius, color, assets, kind, spriteMaterial));
        }
        private void ShotEffect(Vector3 from, Vector3 to) { AddEffect(from + Aim.forward * .3f, to, .065f, 0, new Color(1, .85f, .4f)); }
        private void ImpactEffect(Vector3 position, bool blood = false) { AddEffect(position, position, .22f, .2f, Color.white, blood ? "blood" : "puff"); }
        private void ProjectileImpactEffect(Vector3 position, string kind, float radius) { AddEffect(position, position, .4f, Mathf.Min(radius, 3), Color.white, kind); }
        public Vector3 OpenDirection(Vector3 pos, Vector3 goal, Vector3 up, float radius, float height, bool left)
        {
            foreach (float angle in new [] {35f, -35, 70, -70, 110, -110})
            {
                Vector3 direction = Quaternion.AngleAxis(left ? angle : -angle, up) * goal;
                RaycastHit hit;
                if (!WorldCast(pos + up * .45f, direction, radius + 1.1f, out hit) &&
                    !WorldCast(pos + up * height * .8f, direction, radius + 1.1f, out hit)) return direction;
            }
            return Vector3.zero;
        }
        private void ClearExpansionActors()
        {
            foreach (var pickup in pickups) pickup.Dispose(); pickups.Clear();
            foreach (var effect in effects) effect.Dispose(); effects.Clear();
            waveEnemies.Clear(); if (defending) { if (!wavePending) wave = Math.Max(0, wave - 1); wavePending = true; waveRest = 15; }
        }
        private void StartDefense(Vessel ship)
        {
            if (ship == null || ship.isEVA || !ship.Landed) { Notice("Выбери приземлённый корабль"); return; }
            if (!LoadAssets()) return;
            defendedShip = ship; defending = true; wave = 0; wavePending = true; waveRest = 15; waveEnemies.Clear();
            resumeChecked = true; PersistSettings(); Notice("Защита: " + ship.vesselName + ". Первая волна через 15 секунд после закрытия меню");
        }
        private void StopDefense()
        { defending = false; waveEnemies.Clear(); PersistSettings(); }
        private void TickDefense(float dt)
        {
            if (!defending) return;
            if (defendedShip == null || defendedShip.rootPart == null || defendedShip.rootPart.State == PartStates.DEAD)
            { StopDefense(); Notice("Защита закончена: корабль потерян"); return; }
            if (defendedShip.packed || !defendedShip.loaded || defendedShip.mainBody != worldBody) return;
            if ((Observer.transform.position - defendedShip.transform.position).sqrMagnitude > 600 * 600)
            { StopDefense(); Notice("Защита остановлена: ты покинул район корабля"); return; }
            if (!wavePending)
            {
                foreach (DoomEnemy enemy in waveEnemies) if (enemy.Root != null && !enemy.Dead) return;
                bestWave = Math.Max(bestWave, wave); wavePending = true; waveRest = 20; PersistSettings();
                Notice("Волна " + wave + " отбита! 20 секунд на подготовку");
                if (Observer.isEVA) { DoomInventory.Give(Observer, "kdMedkit"); DoomInventory.Give(Observer, "kdBullets"); DoomInventory.Give(Observer, "kdShells"); }
            }
            waveRest -= dt; if (waveRest > 0) return;
            if (!LoadAssets()) { StopDefense(); return; }
            waveEnemies.Clear(); int next = wave + 1;
            Vector3 origin = defendedShip.transform.position, up = UpAt(origin);
            Vector3 basis = Vector3.Cross(up, Vector3.right).normalized;
            if (basis.sqrMagnitude < .01f) basis = Vector3.Cross(up, Vector3.forward).normalized;
            int expected = WaveRules.Size(next);
            for (int attempt = 0; attempt < expected * 8 && waveEnemies.Count < expected && enemies.Count < 64; attempt++)
            {
                int type = WaveRules.Boss(next) && waveEnemies.Count == 0 ? MonsterSpec.All.Length - 1 : UnityEngine.Random.Range(0, WaveRules.EnemyTier(next));
                Vector3 offset = Quaternion.AngleAxis(UnityEngine.Random.Range(0, 360), up) * basis * UnityEngine.Random.Range(30, 65);
                if (TrySpawnAt(type, origin + offset, up)) waveEnemies.Add(enemies[enemies.Count - 1]);
            }
            if (waveEnemies.Count == 0) { waveRest = 5; return; }
            wave = next; wavePending = false; PersistSettings();
            Notice("ВОЛНА " + wave + (WaveRules.Boss(wave) ? " — КИБЕРДЕМОН" : "") + ": " + waveEnemies.Count + " врагов");
        }
        private void DrawTargetInfo(float width, float height)
        {
            if (FlightCamera.fetch == null) return;
            RaycastHit hit; DoomEnemy enemy;
            Transform cam = Aim;
            if (Cast(cam.position, cam.forward, 180, out hit, out enemy, false))
            {
                string label = "";
                if (enemy != null) label = enemy.Spec.Name + " — " + Math.Max(0, enemy.Health) + " / " + enemy.Spec.Health + " HP";
                else if (hit.collider != null)
                {
                    Part part = hit.collider.GetComponentInParent<Part>(); PartDamageState state;
                    if (part != null && part.partInfo != null)
                        label = part.partInfo.title + " — " + (partHealth.TryGetValue(part, out state) ? Mathf.CeilToInt(state.Health) : Mathf.CeilToInt(CombatRules.PartDurability(part.mass))) + " HP";
                }
                GUI.Label(new Rect(width * .5f - 180, height * .5f + 20, 360, 28), label, small);
            }
            if (defending) GUI.Label(new Rect(20, 25, 550, 40), "ЗАЩИТА: " + defendedShip.vesselName + " • ВОЛНА " + wave + (wavePending ? " • подготовка " + Mathf.CeilToInt(waveRest) + " с" : ""), text);
        }
        private void DrawExpansionMenu()
        {
            if (menuTab == 1) DrawEquipment();
            if (menuTab == 2)
            {
                GUILayout.Label("ЗАЩИТА КОРАБЛЯ", title);
                GUILayout.Label("Рекорд: " + bestWave + " волн. Кибердемон — каждая пятая волна.", text);
                if (defending)
                {
                    GUILayout.Label("Корабль: " + (defendedShip == null ? "потерян" : defendedShip.vesselName) + "\nВолна: " + wave + (wavePending ? " • подготовка" : " • бой"), text);
                    if (GUILayout.Button("Остановить защиту", button)) StopDefense();
                }
                else
                {
                    GUILayout.Label("Выбери аппарат рядом. Сначала загрузите оружие и припасы в инвентарь кербала.", text);
                    foreach (Vessel v in FlightGlobals.VesselsLoaded)
                        if (v != null && !v.isEVA && v.Landed && !v.packed && v.mainBody == worldBody && (v.transform.position - Observer.transform.position).sqrMagnitude < 300 * 300)
                            if (GUILayout.Button("Защищать: " + v.vesselName, button)) StartDefense(v);
                }
            }
            if (menuTab == 3)
            {
                GUILayout.Label("НАСТРОЙКИ", title);
                GUILayout.Label("Чувствительность: " + sensitivity.ToString("F1"), text); sensitivity = GUILayout.HorizontalSlider(sensitivity, .2f, 6);
                GUILayout.Label("Скорость: " + movementSpeed.ToString("F1") + "× (ходьба 4,5 / бег 8,5 м/с)", small); movementSpeed = GUILayout.HorizontalSlider(movementSpeed, .5f, 3);
                GUILayout.Label("Размер оружия: " + weaponSize.ToString("F1"), text); weaponSize = GUILayout.HorizontalSlider(weaponSize, .5f, 1.5f);
                GUILayout.Label("Урон кораблям: " + shipDamage.ToString("F1") + "× (0 = без разрушений)", small); shipDamage = GUILayout.HorizontalSlider(shipDamage, 0, 3);
                ambientEnabled = GUILayout.Toggle(ambientEnabled, "Случайные встречи вне Кербина");
                GUILayout.Label("Предел врагов: " + ambientLimit, text); ambientLimit = Mathf.RoundToInt(GUILayout.HorizontalSlider(ambientLimit, 1, 40));
                GUILayout.Label("Интервал волн: " + Mathf.RoundToInt(encounterInterval) + " с", text); encounterInterval = GUILayout.HorizontalSlider(encounterInterval, 10, 120);
                GUILayout.Label("Подготовка после посадки: " + Mathf.RoundToInt(landingGrace) + " с", text); landingGrace = GUILayout.HorizontalSlider(landingGrace, 5, 120);
                PersistSettings();
            }
            if (GUILayout.Button("Продолжить  [F7]", button)) SetMenu(false);
        }
        private void DrawEquipment()
        {
            GUILayout.Label("ШТАТНЫЙ ИНВЕНТАРЬ", title);
            GUILayout.Label("В VAB/SPH: раздел «Груз» → DOOM. Переноси предметы штатным интерфейсом KSP в кабину или кербалу.", small);
            if (Observer == null) return;
            if (!Observer.isEVA) GUILayout.Label("IVA: оружие и припасы берутся из штатных контейнеров этого аппарата.", small);
            equipmentScroll = GUILayout.BeginScrollView(equipmentScroll, GUILayout.Height(220));
            for (int i = 0; i < WeaponSpec.All.Length; i++)
            {
                GUILayout.BeginHorizontal();
                Texture2D icon = DoomInventoryIcons.GetTexture(DoomInventory.Weapons[i]);
                if (icon != null) GUILayout.Label(icon, GUILayout.Width(30), GUILayout.Height(30));
                GUILayout.Label((i + 1) + ". " + WeaponSpec.All[i].Name + (HasWeapon(i) ? " ✓" : " — нет"), small);
                if (GUILayout.Button(HasWeapon(i) ? "Взять" : "+ в инвентарь", button, GUILayout.Width(125)))
                { if (HasWeapon(i)) weapon = i; else if (!DoomInventory.Give(Observer, DoomInventory.Weapons[i])) Notice("Недостаточно места в инвентаре"); }
                GUILayout.EndHorizontal();
            }
            for (int i = 0; i < 4; i++)
            {
                GUILayout.BeginHorizontal(); GUILayout.Label(new [] {"Пули", "Дробь", "Ракеты", "Ячейки"}[i] + ": " + ammo[i] + " • упаковок: " + DoomInventory.Count(Observer, DoomInventory.Ammo[i]), small);
                if (GUILayout.Button("+ упаковка", button, GUILayout.Width(125))) DoomInventory.Give(Observer, DoomInventory.Ammo[i]);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Аптечка (" + DoomInventory.Count(Observer, "kdMedkit") + ")", button)) UseSupply("kdMedkit");
            if (GUILayout.Button("Броня (" + DoomInventory.Count(Observer, "kdArmor") + ")", button)) UseSupply("kdArmor");
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Тест: положить стартовый набор", button)) GiveStarter();
            GUILayout.Label("Кнопки «+» — тестовая выдача. Обычный путь: загрузка предметов в редакторе. R расходует упаковку патронов, H — аптечку.", small);
        }
    }
}
