using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace KerbalDoom
{
    public sealed partial class KerbalDoomAddon
    {
        private DoomMapWorld mapWorld;
        private BodyPoint mapAnchor;
        private string mapName = "";
        private string[] mapNames;
        private int selectedMap, mapSkill = 1;
        private float mapScale = .04f;
        private Vector3 mapForward;
        private bool mapRestoreChecked, mapCompletedNotice;
        private readonly HashSet<int> consumedMapThings = new HashSet<int>();
        private readonly Dictionary<DoomEnemy, int> mapActors = new Dictionary<DoomEnemy, int>();
        private readonly Dictionary<int, GameObject> mapLoot = new Dictionary<int, GameObject>();
        private readonly Dictionary<Guid, MapReturn> mapReturns = new Dictionary<Guid, MapReturn>();
        private sealed class MapReturn { public Vessel Vessel; public BodyPoint Point; public Quaternion Rotation; }

        private void EnsureMapNames()
        {
            if (mapNames != null) return;
            try { mapNames = new DoomMapArchive(wadPath).Maps.ToArray(); }
            catch (Exception e) { mapNames = new string[0]; Notice("Карты WAD: " + e.Message); }
        }
        private void BuildImportedMap()
        {
            if (!WorldReady || !LoadAssets()) return;
            if (!Observer.Landed && Observer.heightFromTerrain > 500)
            { Notice("Для ходьбы по карте выбери поверхность или низкий полёт. Обычный режим DOOM доступен и на орбите."); return; }
            EnsureMapNames(); if (mapNames.Length == 0) return;
            if (mapReturns.Count != 0) { Notice("Сначала верни перенесённые аппараты и кербалов из предыдущей карты"); return; }
            ConfigNode previous = DoomScenario.Instance == null ? null : DoomScenario.Instance.ImportedMap;
            if (previous != null && mapWorld == null && previous.GetNodes("RETURN").Length != 0 && previous.GetValue("body") == worldBody.bodyName)
            { RestoreImportedMap(previous); Notice("Восстановлена предыдущая карта. Сначала верни её участников."); return; }
            if (previous != null && previous.GetValue("body") != worldBody.bodyName && previous.GetNodes("RETURN").Length != 0)
            { Notice("Сначала верни аппараты из карты на " + previous.GetValue("body")); return; }
            StopDefense(); ClearActors(); DisposeMap();
            Vector3 origin = Observer.transform.position, up = UpAt(origin);
            mapForward = Vector3.ProjectOnPlane(Aim.forward, up).normalized;
            if (mapForward.sqrMagnitude < .01f) mapForward = Vector3.Cross(up, Vector3.right).normalized;
            origin += mapForward * 55 + up * 24;
            try
            {
                mapName = mapNames[Mathf.Clamp(selectedMap, 0, mapNames.Length - 1)];
                DoomMapData preview = new DoomMapArchive(wadPath).Load(mapName);
                int lowest = int.MaxValue; foreach (var sector in preview.Sectors) lowest = Math.Min(lowest, sector.Floor);
                float down = (float)(preview.MapFloorAt(preview.PlayerStart.X, preview.PlayerStart.Y) - lowest) * mapScale;
                float radius = (float)Math.Sqrt(Math.Pow(Math.Max(Math.Abs(preview.MinX - preview.PlayerStart.X), Math.Abs(preview.MaxX - preview.PlayerStart.X)), 2) +
                    Math.Pow(Math.Max(Math.Abs(preview.MinY - preview.PlayerStart.Y), Math.Abs(preview.MaxY - preview.PlayerStart.Y)), 2)) * mapScale;
                double terrain = worldBody.GetAltitude(Observer.transform.position), terrainMin = terrain;
                for (int sample = 0; sample < 8; sample++)
                {
                    Vector3 probe = origin + Quaternion.AngleAxis(sample * 45, up) * mapForward * radius;
                    double altitudeAt = worldBody.TerrainAltitude(worldBody.GetLatitude(probe), worldBody.GetLongitude(probe), false);
                    terrain = Math.Max(terrain, altitudeAt); terrainMin = Math.Min(terrainMin, altitudeAt);
                }
                int highestFloor = lowest; foreach (var sector in preview.Sectors) highestFloor = Math.Max(highestFloor, sector.Floor);
                if (terrain - terrainMin + (highestFloor - lowest) * mapScale > 850)
                { Notice("Слишком неровный рельеф для карты. Выбери ровную площадку — KSP ограничивает распознавание высоких платформ."); return; }
                double altitude = worldBody.GetAltitude(origin);
                if (terrain + down + 12 > altitude) origin += up * (float)(terrain + down + 12 - altitude);
                mapAnchor = new BodyPoint(worldBody, origin);
                mapWorld = new DoomMapWorld(wadPath, mapName, worldBody, origin, mapForward, mapScale);
                mapRestoreChecked = true;
                mapCompletedNotice = false; consumedMapThings.Clear(); mapActors.Clear();
                SaveImportedMap(); Notice(mapName + " загружена рядом с аппаратом. «Войти» переносит только выбранного кербала/аппарат.");
                if (DoomGlobalSettings.Current.EasterEggs) status = DoomEasterEggs.Quip("map");
            }
            catch (Exception e) { DisposeMap(); Notice("Не удалось загрузить карту: " + e.Message); Debug.LogException(e); }
        }
        private void EnterImportedMap()
        {
            if (mapWorld == null || !WorldReady) return;
            Vessel v = Observer;
            if (!v.isEVA && !v.Landed) { Notice("Для переноса аппарата сначала посади его; режим DOOM в воздухе доступен без переноса"); return; }
            Vector3 up = UpAt(mapWorld.PlayerStartWorld);
            float clearance = v.isEVA ? .25f : VesselGroundClearance(v, up);
            if (!v.isEVA && clearance > 5) { Notice("Для оригинальных коридоров нужен небольшой аппарат или ровер"); return; }
            Vector3 target = mapWorld.PlayerStartWorld + up * clearance;
            if (!v.isEVA && !VesselFits(v, target, up)) { Notice("Аппарат не помещается у старта карты. Оставь его снаружи и войди кербалом."); return; }
            if (!mapReturns.ContainsKey(v.id)) mapReturns[v.id] = new MapReturn { Vessel = v, Point = new BodyPoint(v.mainBody, v.transform.position), Rotation = v.transform.rotation };
            if (active) StopMode(null);
            v.SetPosition(target); v.SetWorldVelocity(Vector3d.zero);
            if (v.isEVA) v.SetRotation(Quaternion.LookRotation(mapWorld.PlayerForward, up));
            SetMenu(false); Toggle(); SaveImportedMap();
        }
        private static float VesselGroundClearance(Vessel vessel, Vector3 up)
        {
            float min = 0;
            foreach (Part part in vessel.parts)
            {
                if (part == null) continue;
                foreach (Collider col in part.GetComponentsInChildren<Collider>())
                {
                    if (!col.enabled || col.isTrigger || col.GetComponentInParent<Part>() != part) continue;
                    Bounds b = col.bounds;
                    float extent = Mathf.Abs(up.x) * b.extents.x + Mathf.Abs(up.y) * b.extents.y + Mathf.Abs(up.z) * b.extents.z;
                    min = Mathf.Min(min, Vector3.Dot(b.center - vessel.transform.position, up) - extent);
                }
            }
            return -min + .2f;
        }
        private bool VesselFits(Vessel vessel, Vector3 target, Vector3 up)
        {
            bool initialized = false; Bounds bounds = new Bounds(vessel.transform.position, Vector3.one);
            foreach (Part part in vessel.parts)
                if (part != null) foreach (Collider col in part.GetComponentsInChildren<Collider>())
                    if (col.enabled && !col.isTrigger && col.GetComponentInParent<Part>() == part)
                    { if (!initialized) { bounds = col.bounds; initialized = true; } else bounds.Encapsulate(col.bounds); }
            Vector3 movedCenter = bounds.center + target - vessel.transform.position;
            foreach (Collider col in Physics.OverlapBox(movedCenter, bounds.extents * .95f, Quaternion.identity,
                Physics.DefaultRaycastLayers & ~(1 << 9) & ~(1 << 10), QueryTriggerInteraction.Ignore))
            {
                Part own = col.GetComponentInParent<Part>();
                if (own == null || own.vessel != vessel) return false;
            }
            return true;
        }
        private void ReturnFromMap(bool all)
        {
            if (active) StopMode(null);
            foreach (MapReturn ret in new List<MapReturn>(mapReturns.Values))
            {
                if (ret.Vessel == null || !ret.Vessel.loaded || ret.Vessel.packed || ret.Vessel.mainBody != ret.Point.Body) continue;
                if (!all && ret.Vessel != Observer) continue;
                ret.Vessel.SetPosition(ret.Point.World); ret.Vessel.SetRotation(ret.Rotation); ret.Vessel.SetWorldVelocity(Vector3d.zero);
                mapReturns.Remove(ret.Vessel.id);
            }
            SaveImportedMap();
        }
        private void RemoveImportedMap()
        {
            ReturnFromMap(true);
            if (mapReturns.Count != 0) { Notice("Часть перенесённых аппаратов не загружена. Загрузи их и верни перед удалением карты."); return; }
            ClearActors(); DisposeMap();
            if (DoomScenario.Instance != null) DoomScenario.Instance.ImportedMap = null;
        }
        private void TickMap(float dt)
        {
            if (!mapRestoreChecked && DoomScenario.Instance != null)
            {
                ConfigNode saved = DoomScenario.Instance.ImportedMap;
                if (saved == null) mapRestoreChecked = true;
                else if (saved.GetValue("body") == worldBody.bodyName) RestoreImportedMap(saved);
            }
            if (mapWorld == null) return;
            if (mapAnchor.Body != worldBody) { SaveImportedMap(); DisposeMap(); mapRestoreChecked = false; return; }
            Vector3 point = Observer.isEVA ? PlayerPosition : Aim.position;
            mapWorld.Tick(dt, point, true, active && Input.GetKeyDown(KeyCode.E));
            if (mapWorld.Completed && !mapCompletedNotice)
            { mapCompletedNotice = true; Notice(mapName + " пройдена! Аппарат сохранён. Выбери следующую карту или вернись ко входу."); Play("DSSWTCHN", .6f); SaveImportedMap(); }
            TickMapThings(point);
        }
        private void TickMapThings(Vector3 playerPosition)
        {
            foreach (var pair in new List<KeyValuePair<DoomEnemy, int>>(mapActors))
            {
                if (pair.Key.Dead) { consumedMapThings.Add(pair.Value); mapActors.Remove(pair.Key); }
                else if ((pair.Key.Position.World - playerPosition).sqrMagnitude > 230 * 230)
                { pair.Key.Dispose(); enemies.Remove(pair.Key); mapActors.Remove(pair.Key); }
            }
            int skillFlag = 1 << Mathf.Clamp(mapSkill, 0, 2);
            for (int i = 0; i < mapWorld.Data.Things.Length; i++)
            {
                DoomMapData.Thing thing = mapWorld.Data.Things[i];
                if (consumedMapThings.Contains(i) || (thing.Flags & 16) != 0 || (thing.Flags & skillFlag) == 0) continue;
                Vector3 position = mapWorld.MapToWorld(thing.X, thing.Y);
                float distance = (position - playerPosition).sqrMagnitude;
                int type = MapMonsterType(thing.Type);
                if (type >= 0)
                {
                    if (distance > 170 * 170 || enemies.Count >= 64 || mapActors.ContainsValue(i)) continue;
                    MonsterSpec spec = MonsterSpec.All[type];
                    var enemy = new DoomEnemy(spec, worldBody, position + UpAt(position) * (spec.Flying ? 1.4f : .05f), spriteMaterial);
                    enemies.Add(enemy); mapActors[enemy] = i; continue;
                }
                string item, sprite;
                if (!MapItem(thing.Type, out item, out sprite)) continue;
                GameObject root;
                if (!mapLoot.TryGetValue(i, out root))
                {
                    root = new GameObject("KerbalDoom/Map item " + thing.Type);
                    SpriteRenderer renderer = root.AddComponent<SpriteRenderer>(); renderer.sharedMaterial = spriteMaterial; renderer.sprite = assets.Sprite(sprite);
                    root.transform.localScale = Vector3.one * .65f; mapLoot[i] = root;
                }
                Vector3 up = UpAt(position); root.transform.position = position + up * .05f;
                Vector3 away = Vector3.ProjectOnPlane(position - Aim.position, up);
                if (away.sqrMagnitude > .001f) root.transform.rotation = Quaternion.LookRotation(away, up);
                if (distance < 2.5f && DoomInventory.Give(Observer, item))
                { consumedMapThings.Add(i); Destroy(root); mapLoot.Remove(i); Play("DSITEMUP", .45f); }
            }
        }
        private static int MapMonsterType(int type)
        {
            switch (type)
            {
                case 3004: case 84: return 0;
                case 9: return 1; case 3001: return 2;
                case 3002: case 58: return 3;
                case 3005: case 71: case 3006: return 4;
                case 66: case 64: return 5;
                case 65: case 68: return 6;
                case 69: case 3003: case 67: return 7;
                case 16: case 7: return 8; default: return -1;
            }
        }
        private static bool MapItem(int type, out string item, out string sprite)
        {
            item = null; sprite = null;
            switch (type)
            {
                case 2001: item = "kdShotgun"; sprite = "SHOTA0"; break;
                case 82: item = "kdSuperShotgun"; sprite = "SGN2A0"; break;
                case 2002: item = "kdChaingun"; sprite = "MGUNA0"; break;
                case 2003: item = "kdRocketLauncher"; sprite = "LAUNA0"; break;
                case 2004: item = "kdPlasma"; sprite = "PLASA0"; break;
                case 2006: item = "kdBfg"; sprite = "BFUGA0"; break;
                case 2005: item = "kdChainsaw"; sprite = "CSAWA0"; break;
                case 2007: case 2048: item = "kdBullets"; sprite = type == 2007 ? "CLIPA0" : "AMMOA0"; break;
                case 2008: case 2049: item = "kdShells"; sprite = type == 2008 ? "SHELA0" : "SBOXA0"; break;
                case 2010: case 2046: item = "kdRockets"; sprite = type == 2010 ? "ROCKA0" : "BROKA0"; break;
                case 2047: case 17: item = "kdCells"; sprite = type == 17 ? "CELPA0" : "CELLA0"; break;
                case 2011: case 2012: case 2013: case 2014: item = "kdMedkit"; sprite = type == 2011 ? "STIMA0" : type == 2013 ? "SOULA0" : type == 2014 ? "BON1A0" : "MEDIA0"; break;
                case 2015: case 2018: case 2019: case 83: item = "kdArmor"; sprite = type == 2015 ? "BON2A0" : type == 2019 ? "ARM2A0" : type == 83 ? "MEGAA0" : "ARM1A0"; break;
            }
            return item != null;
        }
        public void SaveImportedMap()
        {
            if (mapWorld == null || mapAnchor == null || DoomScenario.Instance == null) return;
            ConfigNode node = new ConfigNode("DOOM_MAP"); Vector3 origin = mapAnchor.World;
            node.AddValue("map", mapName); node.AddValue("body", mapAnchor.Body.bodyName); node.AddValue("scale", SaveRules.Format(mapScale));
            node.AddValue("lat", mapAnchor.Body.GetLatitude(origin).ToString("R", CultureInfo.InvariantCulture));
            node.AddValue("lon", mapAnchor.Body.GetLongitude(origin).ToString("R", CultureInfo.InvariantCulture));
            node.AddValue("alt", mapAnchor.Body.GetAltitude(origin).ToString("R", CultureInfo.InvariantCulture));
            Vector3 localForward = mapAnchor.Body.transform.InverseTransformDirection(mapForward);
            node.AddValue("fx", SaveRules.Format(localForward.x)); node.AddValue("fy", SaveRules.Format(localForward.y)); node.AddValue("fz", SaveRules.Format(localForward.z));
            node.AddValue("state", mapWorld.CaptureState()); node.AddValue("skill", mapSkill);
            var used = new List<string>(); foreach (int id in consumedMapThings) used.Add(id.ToString(CultureInfo.InvariantCulture));
            foreach (var pair in mapActors) if (pair.Key.Dead && !consumedMapThings.Contains(pair.Value)) used.Add(pair.Value.ToString(CultureInfo.InvariantCulture));
            node.AddValue("used", string.Join(",", used.ToArray()));
            foreach (var pair in mapReturns)
            {
                ConfigNode ret = node.AddNode("RETURN"); ret.AddValue("id", pair.Key.ToString()); Vector3 p = pair.Value.Point.World;
                ret.AddValue("lat", pair.Value.Point.Body.GetLatitude(p).ToString("R", CultureInfo.InvariantCulture));
                ret.AddValue("lon", pair.Value.Point.Body.GetLongitude(p).ToString("R", CultureInfo.InvariantCulture));
                ret.AddValue("alt", pair.Value.Point.Body.GetAltitude(p).ToString("R", CultureInfo.InvariantCulture));
                Quaternion q = pair.Value.Rotation; ret.AddValue("rotation", q.x.ToString("R", CultureInfo.InvariantCulture) + "," + q.y.ToString("R", CultureInfo.InvariantCulture) + "," + q.z.ToString("R", CultureInfo.InvariantCulture) + "," + q.w.ToString("R", CultureInfo.InvariantCulture));
            }
            DoomScenario.Instance.ImportedMap = node;
        }
        private static double MapNumber(ConfigNode node, string key, double fallback)
        { double n; return double.TryParse(node.GetValue(key), NumberStyles.Float, CultureInfo.InvariantCulture, out n) && !double.IsNaN(n) && !double.IsInfinity(n) ? n : fallback; }
        private void RestoreImportedMap(ConfigNode node)
        {
            mapRestoreChecked = true;
            if (!LoadAssets()) return;
            try
            {
                mapName = node.GetValue("map") ?? "MAP01"; mapScale = Mathf.Clamp((float)MapNumber(node, "scale", .04), .04f, .08f);
                Vector3 origin = (Vector3)worldBody.GetWorldSurfacePosition(MapNumber(node, "lat", 0), MapNumber(node, "lon", 0), MapNumber(node, "alt", 100));
                mapAnchor = new BodyPoint(worldBody, origin);
                Vector3 localForward = new Vector3((float)MapNumber(node, "fx", 0), (float)MapNumber(node, "fy", 0), (float)MapNumber(node, "fz", 1));
                mapForward = Vector3.ProjectOnPlane(worldBody.transform.TransformDirection(localForward), UpAt(origin)).normalized;
                if (mapForward.sqrMagnitude < .01f) mapForward = Vector3.Cross(UpAt(origin), Vector3.right).normalized;
                mapWorld = new DoomMapWorld(wadPath, mapName, worldBody, origin, mapForward, mapScale);
                mapWorld.RestoreState(node.GetValue("state")); mapSkill = Mathf.Clamp((int)MapNumber(node, "skill", 1), 0, 2);
                consumedMapThings.Clear(); foreach (string value in (node.GetValue("used") ?? "").Split(',')) { int i; if (int.TryParse(value, out i)) consumedMapThings.Add(i); }
                foreach (ConfigNode ret in node.GetNodes("RETURN"))
                {
                    Guid id; if (!Guid.TryParse(ret.GetValue("id"), out id)) continue;
                    Vessel v = FlightGlobals.Vessels.Find(delegate(Vessel candidate) { return candidate.id == id; }); if (v == null) continue;
                    Vector3 pos = (Vector3)worldBody.GetWorldSurfacePosition(MapNumber(ret, "lat", 0), MapNumber(ret, "lon", 0), MapNumber(ret, "alt", 0));
                    string[] q = (ret.GetValue("rotation") ?? "0,0,0,1").Split(','); Quaternion rotation = v.transform.rotation;
                    if (q.Length == 4) { float x, y, z, w; if (float.TryParse(q[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) && float.TryParse(q[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) && float.TryParse(q[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z) && float.TryParse(q[3], NumberStyles.Float, CultureInfo.InvariantCulture, out w)) rotation = new Quaternion(x,y,z,w); }
                    mapReturns[id] = new MapReturn { Vessel = v, Point = new BodyPoint(worldBody, pos), Rotation = rotation };
                }
                Notice("Восстановлена карта " + mapName);
            }
            catch (Exception e) { EmergencyMapReturn(node); DisposeMap(); Notice("Не удалось восстановить карту: " + e.Message); Debug.LogException(e); }
        }
        private void EmergencyMapReturn(ConfigNode node)
        {
            foreach (ConfigNode ret in node.GetNodes("RETURN"))
            {
                if (Observer == null || ret.GetValue("id") != Observer.id.ToString()) continue;
                Vector3d point = worldBody.GetWorldSurfacePosition(MapNumber(ret, "lat", 0), MapNumber(ret, "lon", 0), MapNumber(ret, "alt", 0));
                if (active) StopMode(null);
                Observer.SetPosition(point); Observer.SetWorldVelocity(Vector3d.zero);
            }
        }
        private void DisposeMap()
        {
            foreach (GameObject obj in mapLoot.Values) if (obj != null) Destroy(obj); mapLoot.Clear();
            if (mapWorld != null) mapWorld.Dispose(); mapWorld = null; mapAnchor = null;
            mapActors.Clear(); consumedMapThings.Clear(); mapReturns.Clear();
        }
        private void DrawMapMenu()
        {
            GUILayout.Label("КАРТЫ ИЗ ТВОЕГО WAD", title);
            EnsureMapNames();
            if (mapNames.Length == 0) { GUILayout.Label("Классические карты MAPxx/EyMx не найдены.", text); return; }
            if (mapWorld == null)
            {
                selectedMap = GUILayout.SelectionGrid(selectedMap, mapNames, 8);
                mapSkill = GUILayout.Toolbar(mapSkill, new [] {"Легко", "Обычно", "Сложно"});
                GUILayout.Label("Масштаб: " + mapScale.ToString("F2") + " м/DOOM unit", small); mapScale = GUILayout.HorizontalSlider(mapScale, .04f, .08f);
                GUILayout.Label("Карта создаётся рядом, выше поверхности. Лучше выбирать ровное место. Аппарат остаётся в мире KSP.", small);
                if (GUILayout.Button("Загрузить карту рядом", button)) BuildImportedMap();
            }
            else
            {
                GUILayout.Label(mapName + (mapWorld.Completed ? " — ПРОЙДЕНА" : "") + "\nE — двери / переключатели. Ключи подбираются при подходе.", text);
                if (GUILayout.Button(Observer.isEVA ? "Войти кербалом" : "Перенести этот аппарат к старту", button)) EnterImportedMap();
                if (GUILayout.Button("Вернуть выбранного кербала / аппарат", button)) ReturnFromMap(false);
                if (GUILayout.Button("Вернуть всех и убрать карту", button)) RemoveImportedMap();
                GUILayout.Label(mapWorld.Status, small);
                GUILayout.Label("Существа адаптированы к девяти типам мода. Бег на карте — до 9,5 м/с. Крупные аппараты могут не поместиться в коридорах.", small);
                foreach (string warning in mapWorld.Warnings) GUILayout.Label(warning, small);
            }
            if (GUILayout.Button("Продолжить [F7]", button)) SetMenu(false);
        }
    }
}
