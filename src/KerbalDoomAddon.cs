using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

[assembly: KSPAssembly("KerbalDoom", 0, 4)]
[assembly: System.Reflection.AssemblyVersion("0.4.0.0")]

namespace KerbalDoom
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    [DefaultExecutionOrder(1000)]
    public sealed partial class KerbalDoomAddon : MonoBehaviour
    {
        private const string ModeLock = "KerbalDoom.Mode", MenuLock = "KerbalDoom.Menu";
        private readonly List<DoomEnemy> enemies = new List<DoomEnemy>();
        private readonly List<DoomProjectile> projectiles = new List<DoomProjectile>();
        private DoomAssets assets;
        private CameraBridge cameraBridge;
        private Material spriteMaterial, fireMaterial, plasmaMaterial, bfgMaterial;
        private AudioSource audioSource;
        private Vessel player;
        private CelestialBody worldBody;
        private readonly Dictionary<Part, PartDamageState> partHealth = new Dictionary<Part, PartDamageState>();
        private bool uiWasVisible, uiHidden, stockWasPaused;
        private bool ambientEnabled = true;
        private float nextAmbient = 5, graceUntil;
        private int ambientLimit = 12;
        private bool assetsFailed;
        private bool active, menu, oldMouseLook, captured, godMode, infiniteAmmo;
        private bool menuHadMouseLook;
        private bool hadFocus = true;
        private KeyCode toggleKey = KeyCode.F8, menuKey = KeyCode.F7;
        private string wadPath;
        private int health = 100, armor, weapon = 1, spawnCount = 1;
        private readonly int[] ammo = { 200, 50, 30, 200 };
        private float nextShot, shotAge = 100, hurtFlash, hitMarker, clock, spawnDistance = 15;
        private string status = "";
        private float statusUntil;
        private GUIStyle text, small, title, number, button;
        private Rect window = new Rect(20, 45, 520, 620);
        private Vector2 menuScroll;
        public int Kills;
        private Vessel Observer { get { return FlightGlobals.ActiveVessel; } }
        private Transform Aim { get { return active && cameraBridge != null && cameraBridge.AimTransform != null ? cameraBridge.AimTransform : FlightCamera.fetch.mainCamera.transform; } }
        private bool WorldReady { get { return Observer != null && Observer.loaded && !Observer.packed && Observer.mainBody != null && !MapView.MapIsEnabled; } }
        public Vector3 PlayerPosition { get { return Observer.transform.position + UpAt(Observer.transform.position) * .6f; } }
        private bool Suspended { get { return menu || FlightDriver.Pause || !hadFocus || MapView.MapIsEnabled; } }
        private string ConfigPath { get { return Path.Combine(KSPUtil.ApplicationRootPath, "GameData/KerbalDoom/PluginData/settings.cfg"); } }

        private void Start()
        {
            Instance = this;
            DoomGlobalSettings global = DoomGlobalSettings.Load(); DoomEasterEggs.Reset();
            wadPath = global.WadPath;
            toggleKey = (KeyCode)Enum.Parse(typeof(KeyCode), global.ToggleKey, true);
            menuKey = (KeyCode)Enum.Parse(typeof(KeyCode), global.MenuKey, true);
            ambientEnabled = global.AmbientSpawns; ambientLimit = global.AmbientLimit;
            audioSource = gameObject.AddComponent<AudioSource>(); audioSource.spatialBlend = 0;
            RestoreSettings();
            Debug.Log("[KerbalDoom] LOADED v0.4.0; native HUD, IVA/air, WAD effects/maps; " + toggleKey + " combat, " + menuKey + " menu");
        }
        private void Notice(string message)
        {
            status = message; statusUntil = Time.realtimeSinceStartup + 7;
            ScreenMessages.PostScreenMessage(message, 5, ScreenMessageStyle.UPPER_CENTER);
            Debug.Log("[KerbalDoom] " + message);
        }
        private bool LoadAssets()
        {
            if (assets != null) return true;
            if (string.IsNullOrEmpty(wadPath) || !File.Exists(wadPath))
            {
                Notice("Kerbal DOOM: укажи путь к DOOM2.WAD в GameData/KerbalDoom/PluginData/settings.cfg"); return false;
            }
            try
            {
                assets = new DoomAssets(wadPath); assets.Preload();
                Shader spriteShader = Shader.Find("Sprites/Default");
                Shader glowShader = Shader.Find("Unlit/Color");
                if (spriteShader == null || glowShader == null) throw new InvalidOperationException("Unity shaders unavailable");
                spriteMaterial = new Material(spriteShader);
                fireMaterial = new Material(glowShader); fireMaterial.color = new Color(1, .28f, .04f);
                plasmaMaterial = new Material(glowShader); plasmaMaterial.color = new Color(.2f, .65f, 1);
                bfgMaterial = new Material(glowShader); bfgMaterial.color = new Color(.2f, 1, .1f);
                Debug.Log("[KerbalDoom] WAD_READY " + Path.GetFileName(wadPath));
                return true;
            }
            catch (Exception e)
            {
                if (assets != null) assets.Dispose(); assets = null;
                Notice("Kerbal DOOM: ошибка WAD: " + e.Message); Debug.LogException(e); return false;
            }
        }
        private void Toggle()
        {
            if (active) { StopMode("Режим DOOM выключен"); return; }
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || v.packed || !v.loaded || MapView.MapIsEnabled)
            {
                Notice("DOOM: нужен загруженный аппарат или кербал; закрой карту и останови перемотку"); return;
            }
            cameraBridge = new CameraBridge();
            if (!cameraBridge.Connect()) { Notice("Камера KSP ещё не готова"); return; }
            if (!LoadAssets()) return;
            if (menu) SetMenu(false);
            player = v; oldMouseLook = CameraMouseLook.MouseLocked;
            try
            {
                active = true;
                cameraBridge.Sensitivity = sensitivity; cameraBridge.Speed = mapWorld == null ? movementSpeed : Mathf.Min(movementSpeed, 9.5f / 8.5f);
                cameraBridge.Enter(player);
                nextShot = clock; shotAge = 100; menu = false;
                TimeWarp.SetRate(0, true);
                InputLockManager.SetControlLock(ControlTypes.TIMEWARP | ControlTypes.CAMERAMODES, ModeLock);
                SetCapture(true);
                uiWasVisible = KSP.UI.UIMasterController.Instance != null && KSP.UI.UIMasterController.Instance.IsUIShowing;
                SetStockUi(false);
                Notice("DOOM: ЛКМ — огонь; 1–8 — оружие; " + (v.isEVA ? "R — патроны; H — аптечка; " : "патроны — автоматически; R — штатный RCS; ") + menuKey + " — меню");
                Debug.Log("[KerbalDoom] MODE_ENTER " + v.vesselName);
            }
            catch (Exception e) { StopMode("Не удалось включить камеру DOOM"); Debug.LogException(e); }
        }
        private void StopMode(string reason)
        {
            SaveMarine();
            bool wasActive = active;
            active = false; menu = false;
            InputLockManager.RemoveControlLock(MenuLock); InputLockManager.RemoveControlLock(ModeLock);
            if (wasActive)
            {
                try { if (cameraBridge != null) cameraBridge.Exit(player); }
                catch (Exception e) { Debug.LogWarning("[KerbalDoom] Camera restore: " + e.Message); }
                CameraMouseLook.SetMouseLook(oldMouseLook); captured = oldMouseLook;
                if (uiHidden) SetStockUi(uiWasVisible);
                uiHidden = false;
                if (!string.IsNullOrEmpty(reason)) Notice(reason);
                Debug.Log("[KerbalDoom] MODE_EXIT; encounters continue; camera/UI/controls restored");
            }
            player = null;
        }
        private void SetCapture(bool value)
        {
            if (active && cameraBridge != null && cameraBridge.Cockpit)
            {
                if (CameraMouseLook.MouseLocked) CameraMouseLook.SetMouseLook(false);
                Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !value; captured = value; return;
            }
            if (captured == value && CameraMouseLook.MouseLocked == value) return;
            CameraMouseLook.SetMouseLook(value); captured = value;
        }
        private void SetMenu(bool value)
        {
            if (value && !menu) menuHadMouseLook = CameraMouseLook.MouseLocked;
            menu = value;
            if (value) InputLockManager.SetControlLock(ControlTypes.EVA_INPUT | ControlTypes.CAMERACONTROLS, MenuLock);
            else InputLockManager.RemoveControlLock(MenuLock);
            SetCapture(!value && !FlightDriver.Pause && hadFocus && (active || menuHadMouseLook));
        }
        private void SetStockUi(bool visible)
        {
            if (KSP.UI.UIMasterController.Instance == null) return;
            // Use the events: direct HideUI does not update IsUIShowing in KSP 1.12.5.
            if (visible) GameEvents.onShowUI.Fire();
            else { GameEvents.onHideUI.Fire(); uiHidden = true; }
        }
        private void Update()
        {
            SyncMarine();
            if (WorldReady && worldBody != Observer.mainBody)
            {
                if (mapWorld != null) { SaveImportedMap(); DisposeMap(); mapRestoreChecked = false; }
                if (active) StopMode(null);
                ClearActors(); partHealth.Clear(); worldBody = Observer.mainBody;
                nextAmbient = landingGrace;
            }
            if (!FlightDriver.Pause && hadFocus && Input.GetKeyDown(toggleKey)) Toggle();
            if (active && (Observer != player || player == null || player.packed || !player.loaded || MapView.MapIsEnabled))
            { StopMode("DOOM: возврат к обычному управлению"); return; }
            if (WorldReady && !FlightDriver.Pause && Input.GetKeyDown(menuKey)) SetMenu(!menu);
            if (menu && Input.GetKeyDown(KeyCode.Escape)) SetMenu(false);
            if (active)
            {
                SetCapture(!Suspended);
                if (FlightDriver.Pause) stockWasPaused = true;
                if (!Suspended)
                {
                    MaintainCamera();
                    if (active && KSP.UI.UIMasterController.Instance != null && (stockWasPaused || KSP.UI.UIMasterController.Instance.IsUIShowing)) SetStockUi(false);
                    stockWasPaused = false;
                }
            }
            if (Suspended || !WorldReady) return;
            float dt = Mathf.Min(Time.deltaTime, .1f);
            clock += dt; shotAge += dt; hurtFlash = Mathf.Max(0, hurtFlash - dt); hitMarker = Mathf.Max(0, hitMarker - dt);
            TickExpansion(dt);
            DoomEasterEggs.Tick(active && !menu, ammo[WeaponSpec.All[weapon].AmmoSlot], Kills, Notice);
            TickMap(dt);
            if (!defending && mapWorld == null) TickAmbient(dt);
            if (!active) return;
            cameraBridge.Sensitivity = sensitivity; cameraBridge.Speed = mapWorld == null ? movementSpeed : Mathf.Min(movementSpeed, 9.5f / 8.5f);
            cameraBridge.Look(dt, true);
            MaintainCamera(); if (!active) return;
            for (int i = 0; i < WeaponSpec.All.Length; i++) if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i)))
            { if (HasWeapon(i)) { weapon = i; shotAge = 100; } else Notice("Оружие нужно положить в инвентарь кербала"); }
            if (player.isEVA && Input.GetKeyDown(KeyCode.R)) ReloadFromInventory();
            if (player.isEVA && Input.GetKeyDown(KeyCode.H)) UseSupply("kdMedkit");
            if (Input.GetMouseButton(0) && clock >= nextShot) Shoot();
        }
        private void FixedUpdate()
        {
            if (Suspended || !WorldReady || worldBody != Observer.mainBody || assets == null) return;
            float dt = Mathf.Min(Time.fixedDeltaTime, .1f);
            if (active) cameraBridge.Move();
            // Encounters run independently of the EVA weapon/camera toggle.
            DoomEnemy[] snapshot = enemies.ToArray();
            foreach (DoomEnemy e in snapshot)
            {
                e.Tick(dt, this);
                if ((e.Dead && e.DeathAge > 15) || (mapWorld == null && (e.Position.World - Observer.transform.position).sqrMagnitude > 650 * 650 &&
                    (!defending || defendedShip == null || (e.Position.World - defendedShip.transform.position).sqrMagnitude > 350 * 350)))
                { e.Dispose(); enemies.Remove(e); }
            }
            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                DoomProjectile p = projectiles[i]; p.Age += dt;
                Vector3 start = p.Position.World, end = start + p.Direction * p.Speed * dt;
                if (p.Homing && p.Target != null && p.Target.Part != null && p.Target.Vessel != null && !p.Target.Vessel.packed)
                { p.Direction = Vector3.RotateTowards(p.Direction, (TargetPosition(p.Target.Part, start) - start).normalized, dt * 1.5f, 0); end = start + p.Direction * p.Speed * dt; }
                RaycastHit hit;
                DoomEnemy target;
                bool collided = Cast(start, p.Direction, p.Speed * dt, out hit, out target, !p.Friendly, p.Friendly);
                if (collided || p.Age > 5)
                {
                    if (collided)
                    {
                        if (p.Friendly && target != null) { target.Damage(p.Damage, this); hitMarker = .12f; }
                        Part part = hit.collider == null ? null : hit.collider.GetComponentInParent<Part>();
                        if (part != null) DamagePart(part, p.Damage);
                        if (p.BlastRadius > 0)
                        {
                            Vector3 impact = hit.point - p.Direction * .2f;
                            foreach (DoomEnemy e in enemies.ToArray())
                            {
                                float d = Vector3.Distance(e.Position.World + UpAt(e.Position.World) * e.Spec.Height * .5f, impact);
                                if (!e.Dead && d < p.BlastRadius && ClearLine(impact, e.Position.World + UpAt(e.Position.World) * .8f))
                                    e.Damage(Mathf.CeilToInt(p.ExplosionDamage * (1 - d / p.BlastRadius)), this);
                            }
                            DamagePartsInBlast(impact, p.BlastRadius, p.ExplosionDamage);
                            ProjectileImpactEffect(impact, p.ImpactKind, p.BlastRadius);
                            Play("DSBAREXP", .65f);
                        }
                    }
                    if (collided && p.BlastRadius == 0) ProjectileImpactEffect(hit.point, p.ImpactKind, .4f);
                    p.Dispose(); projectiles.RemoveAt(i);
                }
                else { p.Position.Set(end); p.Visual.transform.position = end; }
            }
        }
        private void LateUpdate()
        {
            if (!WorldReady || assets == null || FlightCamera.fetch == null) return;
            if (active && !Suspended) MaintainCamera();
            Transform cam = Aim;
            foreach (DoomEnemy e in enemies) e.Draw(assets, cam, UpAt(e.Position.World));
            foreach (DoomProjectile p in projectiles) p.Draw(cam, UpAt(p.Position.World));
            DrawPickups(cam);
            foreach (DoomEffect effect in effects) effect.Draw(cam, UpAt(effect.WorldPosition));
        }
        private void MaintainCamera()
        {
            try { cameraBridge.Ensure(player); }
            catch (Exception e) { StopMode("DOOM: ошибка камеры, управление восстановлено"); Debug.LogException(e); }
        }
        private void OnApplicationFocus(bool value) { hadFocus = value; if (active && !value) SetCapture(false); }
        public Vector3 UpAt(Vector3 position) { return ((Vector3d)position - worldBody.position).normalized; }
        private DoomEnemy FindEnemy(Collider collider)
        {
            foreach (DoomEnemy e in enemies) if (e.Collider == collider) return e;
            return null;
        }
        // Nearest-hit selection includes KSP terrain, buildings and parts. Never shoot through walls.
        private bool Cast(Vector3 start, Vector3 direction, float range, out RaycastHit nearest, out DoomEnemy enemy, bool ignoreEnemies, bool ignorePlayer = true)
        {
            nearest = new RaycastHit(); enemy = null;
            float best = float.MaxValue;
            foreach (RaycastHit hit in Physics.RaycastAll(start, direction, range, Physics.DefaultRaycastLayers & ~(1 << 9) & ~(1 << 10), QueryTriggerInteraction.Collide))
            {
                if (hit.collider == null || hit.distance >= best) continue;
                DoomEnemy e = FindEnemy(hit.collider);
                if (hit.collider.isTrigger && e == null) continue;
                if (e != null && (ignoreEnemies || e.Dead)) continue;
                Part part = hit.collider.GetComponentInParent<Part>();
                if (ignorePlayer && player != null && part != null && part.vessel == player) continue;
                best = hit.distance; nearest = hit; enemy = e;
            }
            return best < float.MaxValue;
        }
        public bool WorldCast(Vector3 start, Vector3 direction, float range, out RaycastHit hit)
        { DoomEnemy ignored; return Cast(start, direction, range, out hit, out ignored, true, false); }
        public bool ClearLine(Vector3 start, Vector3 target)
        {
            RaycastHit hit;
            Vector3 delta = target - start;
            return !WorldCast(start, delta.normalized, Mathf.Max(0, delta.magnitude - .2f), out hit);
        }
        public bool Ground(Vector3 start, Vector3 up, float distance, out Vector3 point)
        {
            RaycastHit hit;
            if (WorldCast(start, -up, distance, out hit) && Vector3.Dot(hit.normal, up) > .55f)
            { point = hit.point; return true; }
            point = start; return false;
        }
        private void Shoot()
        {
            if (!HasWeapon(weapon)) { nextShot = clock + .5f; Notice("Нет оружия в инвентаре. Открой F7 → Снаряжение"); return; }
            WeaponSpec w = WeaponSpec.All[weapon];
            if (!infiniteAmmo && ammo[w.AmmoSlot] < w.Cost && !ReloadFromInventory()) { nextShot = clock + .3f; Play("DSNOWAY", .3f); return; }
            if (!infiniteAmmo) ammo[w.AmmoSlot] -= w.Cost;
            nextShot = clock + w.Interval; shotAge = 0; Play(w.Sound, .65f);
            cameraBridge.Recoil = weapon == 2 ? 5 : weapon == 1 || weapon == 4 ? 3 : .6f;
            Transform cam = Aim;
            if (w.Projectile)
            {
                SpawnProjectile(cam.position + cam.forward * .2f, cam.forward, true, weapon == 7 ? 300 : weapon == 4 ? 90 : 20,
                    weapon == 4 ? 45 : 65, weapon == 7 ? 10 : weapon == 4 ? 5 : 0);
                return;
            }
            for (int i = 0; i < w.Pellets; i++)
            {
                Vector3 direction = Quaternion.AngleAxis(UnityEngine.Random.Range(-w.Spread, w.Spread), cam.up) *
                    Quaternion.AngleAxis(UnityEngine.Random.Range(-w.Spread * .45f, w.Spread * .45f), cam.right) * cam.forward;
                RaycastHit hit; DoomEnemy target;
                if (Cast(cam.position, direction, weapon == 6 ? 2.7f : 200, out hit, out target, false))
                {
                    int damage = weapon == 6 ? UnityEngine.Random.Range(15, 30) : UnityEngine.Random.Range(1, 4) * 5;
                    if (target != null) { target.Damage(damage, this); hitMarker = .15f; }
                    else if (hit.collider != null) DamagePart(hit.collider.GetComponentInParent<Part>(), damage);
                    if (i == 0) { ShotEffect(cam.position, hit.point); ImpactEffect(hit.point, target != null); }
                }
                else if (i == 0 && weapon != 6) ShotEffect(cam.position, cam.position + direction * 100);
            }
        }
        public void SpawnProjectile(Vector3 pos, Vector3 dir, bool friendly, int damage, float speed, float blastRadius, bool homing = false, DoomTarget target = null, string kind = null)
        {
            if (projectiles.Count >= 128) return;
            var projectile = new DoomProjectile(worldBody, pos, dir, friendly, damage, speed, blastRadius, friendly && damage >= 300 ? bfgMaterial : friendly && blastRadius == 0 ? plasmaMaterial : fireMaterial);
            projectile.Homing = homing; projectile.Target = target; projectile.ExplosionDamage = damage >= 300 ? 500 : damage >= 40 && !friendly ? 90 : 110;
            projectile.Configure(assets, kind ?? (friendly ? damage >= 300 ? "bfg" : blastRadius > 0 ? "rocket" : "plasma" : blastRadius > 0 ? "rocket" : homing ? "revenant" : "imp"), spriteMaterial);
            projectiles.Add(projectile);
        }
        public void HurtPlayer(int damage)
        {
            if (godMode || clock < graceUntil) return;
            int absorb = Mathf.Min(armor, damage / 3); armor -= absorb; health -= damage - absorb;
            hurtFlash = .25f; Play("DSPLPAIN", .5f);
            if (health <= 0)
            {
                Play("DSPDIEHI", .65f);
                if (active) StopMode("Поражение! Кербал жив; 10 секунд на отступление");
                else Notice("DOOM: поражение! 10 секунд на отступление");
                health = 100; graceUntil = clock + 10;
            }
        }
        public void Play(string name, float volume)
        {
            if (assets == null || audioSource == null || string.IsNullOrEmpty(name)) return;
            AudioClip sound = assets.Sound(name);
            if (sound != null) audioSource.PlayOneShot(sound, volume * GameSettings.SHIP_VOLUME);
        }
        private void TickAmbient(float dt)
        {
            if (!ambientEnabled || assetsFailed) return;
            Vessel v = Observer;
            if (!CombatRules.AmbientAllowed(worldBody.bodyName, worldBody == FlightGlobals.GetHomeBody(), worldBody.pqsController != null,
                v.Landed, v.Splashed, v.heightFromTerrain)) return;
            nextAmbient -= dt;
            if (nextAmbient > 0 || AliveCount() >= ambientLimit) return;
            nextAmbient = encounterInterval * UnityEngine.Random.Range(.8f, 1.2f);
            if (!LoadAssets()) { assetsFailed = true; return; }
            int count = 0;
            Vector3 origin = v.transform.position, up = UpAt(origin);
            Vector3 forward = Vector3.ProjectOnPlane(v.transform.forward, up).normalized;
            if (forward.sqrMagnitude < .01f) forward = Vector3.Cross(up, Vector3.right).normalized;
            if (forward.sqrMagnitude < .01f) forward = Vector3.Cross(up, Vector3.forward).normalized;
            for (int attempt = 0; attempt < 20 && count < 3 && AliveCount() < ambientLimit; attempt++)
            {
                Vector3 direction = Quaternion.AngleAxis(UnityEngine.Random.Range(0, 360), up) * forward;
                int type = UnityEngine.Random.Range(0, MonsterSpec.All.Length - 1);
                if (TrySpawnAt(type, origin + direction * UnityEngine.Random.Range(45, 90), up)) count++;
            }
            Debug.Log("[KerbalDoom] AMBIENT " + worldBody.bodyName + " spawned=" + count + " alive=" + AliveCount() + " combat=" + active);
        }
        private bool TrySpawnAt(int type, Vector3 position, Vector3 up)
        {
            if (enemies.Count >= 64) return false;
            Vector3 ground;
            if (!Ground(position + up * 45, up, 110, out ground)) return false;
            MonsterSpec spec = MonsterSpec.All[type];
            Vector3 foot = ground + up * (spec.Flying ? 1.5f : .08f);
            foreach (DoomEnemy e in enemies)
                if (!e.Dead && Vector3.Distance(e.Position.World, foot) < e.Spec.Radius + spec.Radius + .2f) return false;
            foreach (Collider col in Physics.OverlapCapsule(foot + up * spec.Radius, foot + up * (spec.Height - spec.Radius), spec.Radius,
                Physics.DefaultRaycastLayers & ~(1 << 9) & ~(1 << 10), QueryTriggerInteraction.Ignore))
                if (col != null) return false;
            enemies.Add(new DoomEnemy(spec, worldBody, foot, spriteMaterial)); return true;
        }
        public DoomTarget FindTarget(Vector3 position)
        {
            if (defending && defendedShip != null && defendedShip.loaded && !defendedShip.packed && defendedShip.mainBody == worldBody && defendedShip.rootPart != null)
                return new DoomTarget { Vessel = defendedShip, Part = defendedShip.rootPart, Position = TargetPosition(defendedShip.rootPart, position) };
            DoomTarget found = null;
            float best = 260 * 260;
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v == null || v.packed || !v.loaded || v.mainBody != worldBody || (mapWorld == null && !v.Landed && !v.isEVA)) continue;
                if ((v.transform.position - position).sqrMagnitude > 350 * 350) continue;
                foreach (Part part in v.parts)
                {
                    if (part == null || part.State == PartStates.DEAD) continue;
                    Vector3 point = part.rb != null ? part.rb.worldCenterOfMass : part.transform.position;
                    float score = (point - position).sqrMagnitude * (v.isEVA ? .65f : 1);
                    if (score >= best) continue;
                    best = score; found = new DoomTarget { Vessel = v, Part = part, Position = point };
                }
            }
            if (found != null) found.Position = TargetPosition(found.Part, position);
            return found;
        }
        public Vector3 TargetPosition(Part part, Vector3 from)
        {
            Vector3 point = part.rb != null ? part.rb.worldCenterOfMass : part.transform.position;
            float best = float.MaxValue;
            foreach (Collider col in part.GetComponentsInChildren<Collider>())
            {
                if (!col.enabled || col.isTrigger || col.GetComponentInParent<Part>() != part) continue;
                Vector3 closest = col.ClosestPoint(from);
                float d = (closest - from).sqrMagnitude;
                if (d < best) { point = closest; best = d; }
            }
            return point;
        }
        public bool CanHitTarget(Vector3 start, DoomTarget target)
        {
            Vector3 delta = target.Position - start;
            RaycastHit hit;
            if (!WorldCast(start, delta.normalized, delta.magnitude + .4f, out hit)) return false;
            Part part = hit.collider == null ? null : hit.collider.GetComponentInParent<Part>();
            if (part == null || part.vessel != target.Vessel) return false;
            // Damage the exposed part, never the selected part through another part of the same ship.
            target.Part = part; target.Position = hit.point;
            return true;
        }
        public void AttackTarget(DoomTarget target, int damage)
        {
            if (target != null && target.Part != null) DamagePart(target.Part, damage);
        }
        private void DamagePart(Part part, int damage)
        {
            if (part == null || part.State == PartStates.DEAD || part.vessel == null || part.vessel.packed || damage <= 0) return;
            if (part.vessel == Observer && Observer.isEVA) { HurtPlayer(damage); return; }
            damage = Mathf.RoundToInt(damage * shipDamage);
            if (damage <= 0) return;
            PartDamageState state;
            if (!partHealth.TryGetValue(part, out state)) { state = new PartDamageState(part.mass); partHealth[part] = state; }
            bool destroyed = state.Apply(damage);
            if (active) hitMarker = .15f;
            if (!destroyed) return;
            partHealth.Remove(part);
            Debug.Log("[KerbalDoom] PART_DESTROYED " + part.name + " vessel=" + part.vessel.vesselName);
            part.explode();
        }
        private void DamagePartsInBlast(Vector3 impact, float radius, int damage)
        {
            var targets = new List<Part>();
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
                if (v != null && v.loaded && !v.packed && v.mainBody == worldBody) targets.AddRange(v.parts);
            foreach (Part part in targets)
            {
                if (part == null) continue;
                Vector3 closest = TargetPosition(part, impact);
                float distance = Vector3.Distance(closest, impact);
                if (distance >= radius) continue;
                Vector3 delta = closest - impact;
                RaycastHit hit;
                bool blocked = WorldCast(impact, delta.normalized, Mathf.Max(0, distance - .05f), out hit);
                if (blocked && (hit.collider == null || hit.collider.GetComponentInParent<Part>() != part)) continue;
                DamagePart(part, Mathf.CeilToInt(damage * (1 - distance / radius)));
            }
        }
        private void Spawn(int type)
        {
            if (!WorldReady || !LoadAssets()) return;
            int spawned = 0;
            Vector3 pos = Observer.transform.position, up = UpAt(pos);
            Vector3 forward = Vector3.ProjectOnPlane(FlightCamera.fetch.mainCamera.transform.forward, up).normalized;
            if (forward.sqrMagnitude < .01f) forward = Vector3.ProjectOnPlane(Observer.transform.forward, up).normalized;
            for (int i = 0; i < spawnCount && enemies.Count < 64; i++)
            {
                float angle = spawnCount == 1 ? 0 : -55 + 110f * i / Math.Max(1, spawnCount - 1);
                Vector3 direction = Quaternion.AngleAxis(angle, up) * forward;
                if (TrySpawnAt(type, pos + direction * spawnDistance, up)) spawned++;
            }
            status = spawned > 0 ? "Добавлено: " + spawned + " — " + MonsterSpec.All[type].Name : "Нет свободного места. Измени дистанцию или направление.";
            statusUntil = Time.realtimeSinceStartup + 6;
            Debug.Log("[KerbalDoom] SPAWN " + MonsterSpec.All[type].Prefix + " count=" + spawned + " total=" + enemies.Count);
        }
        private void ClearActors()
        {
            foreach (DoomEnemy e in enemies) e.Dispose(); enemies.Clear();
            foreach (DoomProjectile p in projectiles) p.Dispose(); projectiles.Clear();
            ClearExpansionActors();
        }
        private void OnDestroy()
        {
            SaveMarine(); PersistSettings();
            StopMode(null);
            DisposeMap();
            ClearActors(); partHealth.Clear();
            if (assets != null) assets.Dispose();
            if (spriteMaterial != null) Destroy(spriteMaterial);
            if (fireMaterial != null) Destroy(fireMaterial);
            if (plasmaMaterial != null) Destroy(plasmaMaterial);
            if (bfgMaterial != null) Destroy(bfgMaterial);
            if (Instance == this) Instance = null;
        }
        private void Styles()
        {
            if (text != null) return;
            text = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            text.normal.textColor = new Color(.87f, .84f, .73f);
            small = new GUIStyle(text) { fontSize = 12 };
            title = new GUIStyle(text) { fontSize = 23, fontStyle = FontStyle.Bold };
            title.normal.textColor = new Color(1, .32f, .16f);
            number = new GUIStyle(text) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            number.normal.textColor = new Color(1, .25f, .12f);
            button = new GUIStyle(GUI.skin.button) { fontSize = 14, fixedHeight = 29 };
        }
        private void OnGUI()
        {
            if (menu && assets == null && !assetsFailed) assetsFailed = !LoadAssets();
            DoomUi.Begin(assets);
            try { DrawModUi(); }
            finally { DoomUi.End(); }
        }
        private void DrawModUi()
        {
            Styles();
            if (menu) button = new GUIStyle(GUI.skin.button) { fontSize = 14, fixedHeight = 29 };
            Matrix4x4 oldMatrix = GUI.matrix;
            float scale = Mathf.Max(.65f, Screen.height / 720f);
            float width = Screen.width / scale, height = Screen.height / scale;
            window.x = Mathf.Clamp(window.x, 4, Mathf.Max(4, width - window.width - 4));
            window.y = Mathf.Clamp(window.y, 4, Mathf.Max(4, height - window.height - 4));
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            if (!active)
            {
                Vessel v = FlightGlobals.ActiveVessel;
                if (v != null && !MapView.MapIsEnabled)
                    if (GUI.Button(new Rect(width - 190, 90, 175, 34), "DOOM  [" + toggleKey + "]", button)) Toggle();
                if (menu && WorldReady) window = GUI.Window(618204, window, DrawMenu, "KERBAL DOOM — СПАВН", GUI.skin.window);
                GUI.matrix = oldMatrix; return;
            }
            GUI.depth = -20;
            if (!FlightDriver.Pause)
            {
                DrawWeapon(width, height);
                if (hurtFlash > 0)
                {
                    GUI.color = new Color(1, 0, 0, hurtFlash * .8f);
                    GUI.DrawTexture(new Rect(0, 0, width, height), Texture2D.whiteTexture); GUI.color = Color.white;
                }
                float x = width * .5f, y = height * .5f;
                GUI.color = hitMarker > 0 ? Color.red : new Color(.75f, 1, .7f, .8f);
                GUI.DrawTexture(new Rect(x - 7, y, 15, 1), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(x, y - 7, 1, 15), Texture2D.whiteTexture); GUI.color = Color.white;
                DrawHud(width, height);
            }
            if (menu) window = GUI.Window(618204, window, DrawMenu, "KERBAL DOOM — ВТОРЖЕНИЕ", GUI.skin.window);
            GUI.matrix = oldMatrix;
        }
        private void DrawWeapon(float width, float height)
        {
            if (!HasWeapon(weapon)) return;
            WeaponSpec w = WeaponSpec.All[weapon];
            char frame = 'A';
            if (shotAge < w.Interval)
            {
                string sequence = w.FireFrames;
                frame = sequence[Mathf.Min(sequence.Length - 1, (int)(shotAge / w.Interval * sequence.Length))];
            }
            string name = w.Prefix + frame + "0";
            if (!assets.Wad.Contains(name)) name = w.Prefix + "A0";
            float pixel = Mathf.Min(height / 240f, width / 320f) * weaponSize;
            float barHeight = DoomHud.BarHeight(width, height);
            DrawPatch(name, width * .5f, height - barHeight, pixel, true);
            if (shotAge < .09f && !string.IsNullOrEmpty(w.Flash)) DrawPatch(w.Flash + "A0", width * .5f, height - barHeight, pixel, true);
        }
        private void DrawPatch(string name, float x, float y, float pixel, bool weaponPatch)
        {
            Texture2D tex = assets.Texture(name);
            if (tex == null) return;
            WadArchive.Patch patch = assets.Info(name);
            WadArchive.Patch ready = assets.Info(WeaponSpec.All[weapon].Prefix + "A0");
            float barHeight = DoomHud.BarHeight(x * 2, Screen.height / Mathf.Max(.65f, Screen.height / 720f));
            CombatRules.Placement placement = CombatRules.WeaponPlacement(patch, ready, x * 2, y + barHeight, barHeight, pixel);
            Rect r = weaponPatch ? new Rect(placement.X, placement.Y, placement.Width, placement.Height) : new Rect(x, y, tex.width * pixel, tex.height * pixel);
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, true);
        }
        private void DrawHud(float width, float height)
        {
            bool[] owned = new bool[WeaponSpec.All.Length];
            for (int i = 0; i < owned.Length; i++) owned[i] = HasWeapon(i);
            DoomHud.Draw(assets, width, height, health, armor, weapon, ammo, infiniteAmmo, Kills, hurtFlash, owned,
                mapWorld == null ? 0 : mapWorld.Keys);
            if (Time.realtimeSinceStartup < statusUntil)
                DoomUi.DrawText(assets, status, new Rect(24, 18, width - 48, 70), 2);
            DrawTargetInfo(width, height);
        }
        private int AliveCount() { int n = 0; foreach (DoomEnemy e in enemies) if (!e.Dead) n++; return n; }
        private void DrawMenu(int id)
        {
            Rect banner = GUILayoutUtility.GetRect(200, 62, GUILayout.ExpandWidth(true)); DoomUi.DrawTitle(assets, banner);
            menuTab = GUILayout.Toolbar(menuTab, new [] {"Спавн", "Снаряжение", "Волны", "Настройки", "Карты DOOM"});
            if (menuTab == 4) { DrawMapMenu(); GUI.DragWindow(new Rect(0, 0, 420, 23)); return; }
            if (menuTab != 0) { DrawExpansionMenu(); GUI.DragWindow(new Rect(0, 0, 420, 23)); return; }
            GUILayout.Label("НЕЖИТЬ НА ПОВЕРХНОСТИ", title);
            GUILayout.Label("Бой приостановлен, пока открыто это меню.", small);
            GUILayout.Label("Автоспавн: другие миры, кроме Кербина. F8 не удаляет врагов.", small);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Количество: " + spawnCount, text, GUILayout.Width(145));
            if (GUILayout.Button("1", button)) spawnCount = 1;
            if (GUILayout.Button("5", button)) spawnCount = 5;
            if (GUILayout.Button("10", button)) spawnCount = 10;
            GUILayout.EndHorizontal();
            GUILayout.Label("Дистанция: " + Mathf.RoundToInt(spawnDistance) + " м", text);
            spawnDistance = GUILayout.HorizontalSlider(spawnDistance, 6, 50);
            menuScroll = GUILayout.BeginScrollView(menuScroll, GUILayout.Height(195));
            for (int i = 0; i < MonsterSpec.All.Length; i++)
                if (GUILayout.Button("+  " + MonsterSpec.All[i].Name + "  [" + MonsterSpec.All[i].Health + " HP]", button)) Spawn(i);
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            godMode = GUILayout.Toggle(godMode, "Бессмертие"); infiniteAmmo = GUILayout.Toggle(infiniteAmmo, "Бесконечные патроны");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Тест: HP + боезапас", button)) { health = 100; armor = 100; ammo[0] = 400; ammo[1] = 100; ammo[2] = 50; ammo[3] = 400; }
            if (GUILayout.Button("Убрать всех", button)) { StopDefense(); ClearActors(); }
            GUILayout.EndHorizontal();
            GUILayout.Label(Time.realtimeSinceStartup < statusUntil ? status : "Живых: " + AliveCount() + " / 64. Спавн перед направлением взгляда.", small, GUILayout.Height(36));
            if (GUILayout.Button("В БОЙ  [" + menuKey + "]", button)) SetMenu(false);
            GUI.DragWindow(new Rect(0, 0, 420, 23));
        }
    }
}
