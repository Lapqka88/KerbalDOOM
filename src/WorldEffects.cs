using System.Collections.Generic;
using UnityEngine;

namespace KerbalDoom
{
    public sealed class DoomPickup
    {
        public readonly GameObject Root;
        public readonly BodyPoint Position;
        public readonly string Item;
        public float Age;
        public DoomPickup(CelestialBody body, Vector3 position, string item, DoomAssets assets, Material material)
        {
            Position = new BodyPoint(body, position); Item = item; Root = new GameObject("KerbalDoom/Loot/" + item);
            SpriteRenderer sprite = Root.AddComponent<SpriteRenderer>(); sprite.sharedMaterial = material;
            string lump = item == "kdMedkit" ? "MEDIA0" : item == "kdArmor" ? "ARM1A0" : item == "kdShells" ? "SHELA0" : item == "kdCells" ? "CELLA0" : "CLIPA0";
            sprite.sprite = assets.Sprite(lump); Root.transform.localScale = Vector3.one * .6f;
        }
        public void Draw(Transform camera, Vector3 up)
        {
            if (Root == null) return;
            Root.transform.position = Position.World + up * (.1f * Mathf.Sin(Age * 3));
            Vector3 away = Vector3.ProjectOnPlane(Root.transform.position - camera.position, up);
            if (away.sqrMagnitude > .001f) Root.transform.rotation = Quaternion.LookRotation(away, up);
        }
        public void Dispose() { if (Root != null) Object.Destroy(Root); }
    }
    // Centre/origin anchored sprites are intentionally separate from monster sprites,
    // whose cache uses a visible-sole pivot for standing on terrain.
    internal sealed class DoomWorldSprite
    {
        private readonly GameObject root;
        private readonly SpriteRenderer renderer;
        private readonly Dictionary<string, Sprite> frames = new Dictionary<string, Sprite>();
        private readonly DoomAssets assets;
        private readonly Material material;
        private readonly bool ownsMaterial;
        public DoomWorldSprite(GameObject parent, DoomAssets source, Material sharedMaterial)
        {
            assets = source;
            root = new GameObject("WAD sprite"); root.transform.SetParent(parent.transform, false);
            renderer = root.AddComponent<SpriteRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ownsMaterial = sharedMaterial == null;
            material = sharedMaterial != null ? sharedMaterial : new Material(Shader.Find("Sprites/Default"));
            renderer.sharedMaterial = material;
        }
        public bool SetFrame(string name, bool flip, float scale)
        {
            if (string.IsNullOrEmpty(name)) { renderer.enabled = false; return false; }
            Sprite frame;
            if (!frames.TryGetValue(name, out frame))
            {
                Texture2D texture = assets.TryTexture(name);
                if (texture == null) { renderer.enabled = false; return false; }
                WadArchive.Patch patch = assets.Info(name);
                Vector2 pivot = new Vector2(Mathf.Clamp01(patch.Left / (float)patch.Width),
                    Mathf.Clamp01((patch.Height - patch.Top) / (float)patch.Height));
                frame = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), pivot, 32f, 0, SpriteMeshType.FullRect);
                frames[name] = frame;
            }
            renderer.sprite = frame; renderer.flipX = flip; renderer.enabled = true;
            root.transform.localScale = Vector3.one * scale;
            return true;
        }
        public void Face(Transform camera, Vector3 up)
        {
            if (camera == null || root == null) return;
            // Use a full view-facing billboard for airborne fireballs as well as surface effects.
            Vector3 away = root.transform.position - camera.position;
            if (away.sqrMagnitude < .0001f) away = camera.forward;
            if (Mathf.Abs(Vector3.Dot(away.normalized, up)) > .98f) up = camera.up;
            root.transform.rotation = Quaternion.LookRotation(away, up);
        }
        public void Dispose()
        {
            foreach (Sprite frame in frames.Values) UnityEngine.Object.Destroy(frame);
            frames.Clear(); if (ownsMaterial) UnityEngine.Object.Destroy(material);
            if (root != null) UnityEngine.Object.Destroy(root);
        }
    }
    public sealed class DoomEffect
    {
        private readonly GameObject root;
        private readonly BodyPoint start, end;
        private Material material;
        private readonly LineRenderer line;
        private readonly float life, radius;
        private DoomWorldSprite sprite;
        private DoomAssets assets;
        private string prefix, animation;
        private float spriteScale = 1;
        private float age;
        public Vector3 WorldPosition { get { return start.World; } }
        public string Kind { get; private set; }
        // Compatibility overload: hitscan traces can remain lines; sized effects are
        // invisible until configured with owned WAD artwork, never primitive spheres.
        public DoomEffect(CelestialBody body, Vector3 from, Vector3 to, float lifetime, float size, Color color)
        {
            start = new BodyPoint(body, from); end = new BodyPoint(body, to); life = Mathf.Max(.01f, lifetime); radius = size;
            root = new GameObject("KerbalDoom/Effect");
            if (size <= 0)
            {
                material = new Material(Shader.Find("Unlit/Color")); material.color = color;
                line = root.AddComponent<LineRenderer>();
                line.sharedMaterial = material; line.positionCount = 2; line.startWidth = .018f; line.endWidth = .008f;
            }
            Tick(0);
        }
        public DoomEffect(CelestialBody body, Vector3 from, Vector3 to, float lifetime, float size, Color color,
            DoomAssets source, string kind, Material spriteMaterial = null) : this(body, from, to, lifetime, size, color)
        {
            Configure(source, kind, spriteMaterial);
        }
        public void Configure(DoomAssets source, string kind, Material spriteMaterial = null)
        {
            if (source == null) return;
            if (sprite != null) sprite.Dispose();
            assets = source; Kind = (kind ?? "puff").ToLowerInvariant();
            switch (Kind)
            {
                case "rocket": case "revenant": prefix = "MISL"; animation = "BCD"; break;
                case "plasma": prefix = "PLSE"; animation = "ABCDE"; break;
                case "bfg": prefix = "BFE1"; animation = "ABCDEF"; break;
                case "bfgspray": prefix = "BFE2"; animation = "ABCD"; break;
                case "imp": prefix = "BAL1"; animation = "CDE"; break;
                case "caco": prefix = "BAL2"; animation = "CDE"; break;
                case "baron": prefix = "BAL7"; animation = "CDE"; break;
                case "blood": prefix = "BLUD"; animation = "ABC"; break;
                default: prefix = "PUFF"; animation = "ABCD"; break;
            }
            if (line != null) line.enabled = false;
            sprite = new DoomWorldSprite(root, source, spriteMaterial);
            bool flip;
            string first = source.Wad.FindSprite(prefix, animation[0], 1, out flip);
            WadArchive.Patch initial = first == null ? null : source.Info(first);
            if (initial != null)
                spriteScale = Mathf.Max(.08f, (radius > 0 ? radius * 2 : .35f) * 32 / Mathf.Max(initial.Width, initial.Height));
            Tick(0);
        }
        public bool Tick(float dt)
        {
            age += dt; if (age >= life) return false;
            root.transform.position = start.World;
            if (sprite != null)
            {
                char frame = animation[Mathf.Min(animation.Length - 1, (int)(age / life * animation.Length))];
                bool flip;
                string name = assets.Wad.FindSprite(prefix, frame, 1, out flip);
                sprite.SetFrame(name, flip, spriteScale);
                if (FlightCamera.fetch != null && FlightCamera.fetch.mainCamera != null)
                    Draw(FlightCamera.fetch.mainCamera.transform, ((Vector3d)start.World - start.Body.position).normalized);
            }
            else if (line != null) { line.SetPosition(0, start.World); line.SetPosition(1, end.World); }
            return true;
        }
        public void Draw(Transform camera, Vector3 up)
        { if (sprite != null) { root.transform.position = start.World; sprite.Face(camera, up); } }
        public void Dispose()
        {
            if (sprite != null) sprite.Dispose();
            UnityEngine.Object.Destroy(root); if (material != null) UnityEngine.Object.Destroy(material);
        }
    }
}
