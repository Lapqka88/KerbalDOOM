using System;
using UnityEngine;

namespace KerbalDoom
{
    public sealed class DoomTarget
    {
        public Vessel Vessel;
        public Part Part;
        public Vector3 Position;
    }
    public sealed class BodyPoint
    {
        public readonly CelestialBody Body;
        private double latitude, longitude, altitude;
        public BodyPoint(CelestialBody body, Vector3 world) { Body = body; Set(world); }
        public void Set(Vector3 world)
        {
            Vector3d p = world;
            latitude = Body.GetLatitude(p); longitude = Body.GetLongitude(p); altitude = Body.GetAltitude(p);
        }
        public Vector3 World { get { return (Vector3)Body.GetWorldSurfacePosition(latitude, longitude, altitude); } }
    }
    public sealed class DoomEnemy
    {
        public readonly MonsterSpec Spec;
        public readonly GameObject Root;
        public readonly CapsuleCollider Collider;
        public readonly BodyPoint Position;
        public readonly SpriteRenderer Renderer;
        public int Health;
        public bool Dead { get { return Health <= 0; } }
        public float Age, DeathAge, NextAttack, AttackAnimation, HurtAnimation;
        private Vector3 facing;
        private DoomTarget target;
        private float targetAge;
        private float burstTimer;
        private int burstLeft;
        private float steerTimer;
        private Vector3 steer;
        public DoomEnemy(MonsterSpec spec, CelestialBody body, Vector3 position, Material material)
        {
            Spec = spec; Health = spec.Health; Position = new BodyPoint(body, position);
            Root = new GameObject("KerbalDoom/" + spec.Prefix);
            Root.layer = 0; Root.transform.position = position;
            Collider = Root.AddComponent<CapsuleCollider>();
            Collider.isTrigger = true; Collider.height = spec.Height; Collider.radius = spec.Radius;
            Collider.center = Vector3.up * spec.Height * .5f;
            GameObject visual = new GameObject("Billboard"); visual.transform.SetParent(Root.transform, false);
            Renderer = visual.AddComponent<SpriteRenderer>(); Renderer.sharedMaterial = material;
            Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            NextAttack = UnityEngine.Random.Range(.8f, 2f);
        }
        public void Damage(int amount, KerbalDoomAddon owner)
        {
            if (Dead) return;
            Health -= amount; HurtAnimation = .15f;
            if (Dead)
            {
                Collider.enabled = false; owner.Kills++;
                owner.PlayAt(Spec.DeathSound, Position.World, .6f); owner.OnEnemyKilled(this); Debug.Log("[KerbalDoom] KILL " + Spec.Prefix);
            }
        }
        public void Tick(float dt, KerbalDoomAddon owner)
        {
            Age += dt; AttackAnimation -= dt; HurtAnimation -= dt; NextAttack -= dt;
            Vector3 pos = Position.World, up = owner.UpAt(pos);
            Root.transform.position = pos; Root.transform.rotation = Quaternion.FromToRotation(Vector3.up, up);
            if (Dead) { DeathAge += dt; return; }
            targetAge -= dt;
            if (targetAge <= 0 || target == null || target.Part == null || target.Part.State == PartStates.DEAD)
            { target = owner.FindTarget(pos); targetAge = .6f; }
            if (target == null || target.Part == null || target.Vessel == null || target.Vessel.packed) return;
            target.Position = owner.TargetPosition(target.Part, pos + up * Spec.Height * .5f);
            Vector3 aim = target.Position;
            Vector3 delta = aim - (pos + up * Spec.Height * .65f);
            float distance = delta.magnitude;
            if (burstLeft > 0)
            {
                burstTimer -= dt;
                if (burstTimer <= 0)
                {
                    owner.SpawnProjectile(pos + up * Spec.Height * .65f, delta.normalized, false, Spec.Damage, 26, 4);
                    owner.PlayAt(Spec.AttackSound, pos, .65f); burstLeft--; burstTimer = .25f;
                }
            }
            Vector3 direction = Vector3.ProjectOnPlane(delta, up).normalized;
            if (direction.sqrMagnitude > .01f) facing = direction;
            if (AttackAnimation <= 0 && distance > (Spec.Range < 3 ? 1.25f : 7f))
            {
                float speed = Spec.Speed * (Spec.Prefix == "SARG" && distance < 12 ? 1.5f : 1);
                Vector3 step = direction * speed * dt;
                RaycastHit hit;
                if (owner.WorldCast(pos + up * .55f, direction, Spec.Radius + .7f, out hit))
                {
                    steerTimer -= dt;
                    if (steerTimer <= 0)
                    { steer = owner.OpenDirection(pos, direction, up, Spec.Radius, Spec.Height, Root.GetInstanceID() % 2 == 0); steerTimer = .3f; }
                    step = steer * Spec.Speed * dt;
                }
                Vector3 next = pos + step, ground;
                if (owner.Ground(next + up * 3f, up, 8, out ground))
                {
                    Vector3 goal = ground + up * (Spec.Flying ? 1.5f + .2f * Mathf.Sin(Age * 2) : .03f);
                    if (Vector3.Dot(goal - pos, up) < 1.2f || Spec.Flying)
                        Position.Set(Vector3.MoveTowards(next, goal, Spec.Speed * dt + .1f));
                }
            }
            float range = Spec.Range < 3 ? Mathf.Max(Spec.Range, Spec.Radius + 1) : Spec.Range;
            if (NextAttack <= 0 && distance <= range && owner.CanHitTarget(pos + up * Spec.Height * .65f, target))
            {
                NextAttack = Spec.Cooldown + UnityEngine.Random.Range(0, .4f); AttackAnimation = .4f;
                owner.PlayAt(Spec.AttackSound, pos, .5f);
                if (Spec.Boss) { burstLeft = 3; burstTimer = 0; }
                else if (Spec.Hitscan)
                {
                    if (UnityEngine.Random.value < Mathf.Lerp(.85f, .22f, distance / Spec.Range)) owner.AttackTarget(target, Spec.Damage);
                }
                else if (Spec.Range < 3) owner.AttackTarget(target, Spec.Damage);
                else owner.SpawnProjectile(pos + up * Spec.Height * .65f, delta.normalized, false, Spec.Damage, Spec.Prefix == "SKEL" ? 18 : 12, 0, Spec.Prefix == "SKEL", target,
                    Spec.Prefix == "HEAD" ? "caco" : Spec.Prefix == "BOS2" || Spec.Prefix == "BOSS" ? "baron" : Spec.Prefix == "SKEL" ? "revenant" : "imp");
            }
        }
        public void Draw(DoomAssets assets, Transform camera, Vector3 up)
        {
            Vector3 pos = Position.World;
            Root.transform.position = pos;
            Root.transform.rotation = Quaternion.FromToRotation(Vector3.up, up);
            Vector3 view = Vector3.ProjectOnPlane(camera.position - pos, up);
            if (view.sqrMagnitude < .001f) view = -camera.forward;
            // Sprite local +Z points away from camera, same orientation as the camera.
            Renderer.transform.rotation = Quaternion.LookRotation(-view.normalized, up);
            float angle = Vector3.SignedAngle(facing.sqrMagnitude > .01f ? facing : view, view, up);
            int rotation = ((Mathf.RoundToInt((angle < 0 ? angle + 360 : angle) / 45f)) % 8) + 1;
            char frame = Dead ? (char)Math.Min(Spec.DeathEnd, Spec.DeathStart + (int)(DeathAge / .14f)) :
                AttackAnimation > 0 ? Spec.AttackFrame : (char)('A' + (int)(Age * 7) % (Spec.Flying ? 1 : 4));
            bool flip;
            string lump = assets.Wad.FindSprite(Spec.Prefix, frame, rotation, out flip);
            if (lump == null) lump = assets.Wad.FindSprite(Spec.Prefix, frame, 1, out flip);
            if (lump == null) lump = assets.Wad.FindSprite(Spec.Prefix, 'A', 1, out flip);
            Renderer.sprite = assets.Sprite(lump); Renderer.flipX = flip;
            string ready = assets.Wad.FindSprite(Spec.Prefix, 'A', 1, out flip);
            WadArchive.Patch reference = assets.Info(ready);
            Renderer.transform.localScale = Vector3.one * (Spec.Height * 32f / Mathf.Max(1, reference.Height));
            Renderer.color = HurtAnimation > 0 ? new Color(1, .35f, .35f) : Color.white;
        }
        public void Dispose() { UnityEngine.Object.Destroy(Root); }
    }
    public sealed class DoomProjectile
    {
        public readonly BodyPoint Position;
        public readonly GameObject Visual;
        public Vector3 Direction;
        public readonly bool Friendly;
        public readonly int Damage;
        public readonly float Speed, BlastRadius;
        public float Age;
        public bool Homing;
        public DoomTarget Target;
        public int ExplosionDamage = 110;
        public string Kind { get; private set; }
        public string ImpactKind { get { return Kind ?? (BlastRadius > 0 ? "rocket" : "plasma"); } }
        private DoomAssets assets;
        private DoomWorldSprite sprite;
        private string spritePrefix, animation;
        private bool directional;
        private float spriteScale = 1;
        public DoomProjectile(CelestialBody body, Vector3 pos, Vector3 direction, bool friendly, int damage, float speed, float blastRadius, Material material)
        {
            Position = new BodyPoint(body, pos); Direction = direction.normalized; Friendly = friendly;
            Damage = damage; Speed = speed; BlastRadius = blastRadius;
            Visual = new GameObject("KerbalDoom/Projectile"); Visual.transform.position = pos;
        }
        public void Configure(DoomAssets source, string kind, Material spriteMaterial = null)
        {
            if (source == null) return;
            if (sprite != null) sprite.Dispose();
            assets = source; Kind = (kind ?? (Friendly ? (Damage >= 300 ? "bfg" : BlastRadius > 0 ? "rocket" : "plasma") : "imp")).ToLowerInvariant();
            directional = false;
            switch (Kind)
            {
                case "rocket": spritePrefix = "MISL"; animation = "A"; directional = true; spriteScale = .6f; break;
                case "revenant": spritePrefix = "FATB"; animation = "AB"; directional = true; spriteScale = .55f; break;
                case "plasma": spritePrefix = "PLSS"; animation = "AB"; spriteScale = .55f; break;
                case "bfg": spritePrefix = "BFS1"; animation = "AB"; spriteScale = .75f; break;
                case "caco": spritePrefix = "BAL2"; animation = "AB"; spriteScale = .6f; break;
                case "baron": spritePrefix = "BAL7"; animation = "AB"; directional = true; spriteScale = .65f; break;
                default: spritePrefix = "BAL1"; animation = "AB"; spriteScale = .6f; break;
            }
            sprite = new DoomWorldSprite(Visual, source, spriteMaterial);
            if (FlightCamera.fetch != null && FlightCamera.fetch.mainCamera != null)
                Draw(FlightCamera.fetch.mainCamera.transform, ((Vector3d)Position.World - Position.Body.position).normalized);
        }
        public void Draw(Transform camera, Vector3 up)
        {
            if (Visual == null) return;
            Visual.transform.position = Position.World;
            if (sprite == null || camera == null) return;
            int rotation = 1;
            if (directional)
            {
                Vector3 forward = Vector3.ProjectOnPlane(Direction, up), view = Vector3.ProjectOnPlane(camera.position - Position.World, up);
                if (forward.sqrMagnitude > .0001f && view.sqrMagnitude > .0001f)
                {
                    float angle = Vector3.SignedAngle(forward, view, up);
                    rotation = Mathf.RoundToInt((angle < 0 ? angle + 360 : angle) / 45f) % 8 + 1;
                }
            }
            char frame = animation[(int)(Age * 8) % animation.Length];
            bool flip;
            string name = assets.Wad.FindSprite(spritePrefix, frame, rotation, out flip);
            if (name == null) name = assets.Wad.FindSprite(spritePrefix, frame, 1, out flip);
            sprite.SetFrame(name, flip, spriteScale); sprite.Face(camera, up);
        }
        public void Dispose() { if (sprite != null) sprite.Dispose(); UnityEngine.Object.Destroy(Visual); }
    }
}
