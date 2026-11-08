using System;
using System.Collections.Generic;
using UnityEngine;

namespace KerbalDoom
{
    public sealed class DoomAssets : IDisposable
    {
        public readonly WadArchive Wad;
        private readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, WadArchive.Patch> patches = new Dictionary<string, WadArchive.Patch>();
        private readonly Dictionary<string, int> groundRows = new Dictionary<string, int>();
        private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        private readonly Dictionary<string, AudioClip> sounds = new Dictionary<string, AudioClip>();
        private readonly HashSet<string> unavailableTextures = new HashSet<string>();
        public DoomAssets(string path) { Wad = new WadArchive(path); }
        public Texture2D Texture(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Texture2D tex;
            if (textures.TryGetValue(name, out tex)) return tex;
            WadArchive.Patch p = Info(name);
            if (p == null) return null;
            Color32[] pixels = new Color32[p.Width * p.Height];
            for (int y = 0; y < p.Height; y++) for (int x = 0; x < p.Width; x++)
            {
                int i = (y * p.Width + x) * 4;
                pixels[(p.Height - 1 - y) * p.Width + x] = new Color32(p.Pixels[i], p.Pixels[i + 1], p.Pixels[i + 2], p.Pixels[i + 3]);
            }
            tex = new Texture2D(p.Width, p.Height, TextureFormat.RGBA32, false);
            tex.name = "KerbalDoom/" + name; tex.filterMode = FilterMode.Point; tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixels32(pixels); tex.Apply(false, true);
            textures[name] = tex;
            p.Pixels = null;
            return tex;
        }
        // GUI assets are optional in custom WADs; cache failures rather than throwing every OnGUI frame.
        public Texture2D TryTexture(string name)
        {
            if (string.IsNullOrEmpty(name) || unavailableTextures.Contains(name)) return null;
            try
            {
                Texture2D result = Texture(name);
                if (result == null) unavailableTextures.Add(name);
                return result;
            }
            catch (Exception e)
            {
                unavailableTextures.Add(name);
                Debug.LogWarning("[KerbalDoom] Optional UI patch unavailable: " + name + " (" + e.Message + ")");
                return null;
            }
        }
        // DOOM flats are raw 64x64 palette indices, unlike column-encoded picture patches.
        public Texture2D Flat(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string key = "flat:" + name;
            Texture2D tex;
            if (textures.TryGetValue(key, out tex)) return tex;
            if (unavailableTextures.Contains(key)) return null;
            byte[] indices = Wad.Read(name), palette = Wad.Read("PLAYPAL");
            if (indices == null || indices.Length != 4096 || palette == null || palette.Length < 768)
            { unavailableTextures.Add(key); return null; }
            Color32[] pixels = new Color32[4096];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                int p = indices[y * 64 + x] * 3;
                pixels[(63 - y) * 64 + x] = new Color32(palette[p], palette[p + 1], palette[p + 2], 255);
            }
            tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            tex.name = "KerbalDoom/Flat/" + name; tex.filterMode = FilterMode.Point; tex.wrapMode = TextureWrapMode.Repeat;
            tex.SetPixels32(pixels); tex.Apply(false, true); textures[key] = tex;
            return tex;
        }
        public WadArchive.Patch Info(string name)
        {
            WadArchive.Patch patch;
            if (!patches.TryGetValue(name, out patch))
            {
                patch = Wad.DecodePatch(name);
                if (patch != null)
                {
                    patches[name] = patch;
                    groundRows[name] = CombatRules.BottomOpaqueRow(patch);
                }
            }
            return patch;
        }
        public Sprite Sprite(string name)
        {
            if (name == null) return null;
            Sprite sprite;
            if (sprites.TryGetValue(name, out sprite)) return sprite;
            Texture2D tex = Texture(name);
            if (tex == null) return null;
            WadArchive.Patch patch = Info(name);
            // DOOM's origin may be below the visible soles. Anchor visible feet, not that hidden origin.
            Vector2 pivot = new Vector2(Mathf.Clamp01(patch.Left / (float)patch.Width),
                (patch.Height - 1 - groundRows[name]) / (float)patch.Height);
            sprite = UnityEngine.Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, 32f, 0, SpriteMeshType.FullRect);
            sprites[name] = sprite;
            return sprite;
        }
        public AudioClip Sound(string name)
        {
            AudioClip clip;
            if (sounds.TryGetValue(name, out clip)) return clip;
            WadArchive.Sound s = Wad.DecodeSound(name);
            if (s == null) return null;
            clip = AudioClip.Create("KerbalDoom/" + name, s.Samples.Length, 1, s.Rate, false);
            clip.SetData(s.Samples, 0); sounds[name] = clip;
            return clip;
        }
        public void Preload()
        {
            foreach (MonsterSpec spec in MonsterSpec.All)
            {
                bool flip;
                string n = Wad.FindSprite(spec.Prefix, 'A', 1, out flip);
                if (n == null) throw new System.IO.InvalidDataException("DOOM II sprite missing: " + spec.Prefix);
                Sprite(n);
            }
            foreach (string n in new [] { "PISGA0", "SHTGA0", "SHT2A0", "CHGGA0", "MISGA0", "PLSGA0", "SAWGA0", "BFGGA0", "STBAR" }) Texture(n);
            foreach (string n in new [] { "DSPISTOL", "DSSHOTGN", "DSDSHTGN", "DSRLAUNC", "DSPLASMA", "DSBAREXP", "DSPLPAIN", "DSPDIEHI" }) Sound(n);
        }
        public void Dispose()
        {
            foreach (Sprite x in sprites.Values) UnityEngine.Object.Destroy(x);
            foreach (Texture2D x in textures.Values) UnityEngine.Object.Destroy(x);
            foreach (AudioClip x in sounds.Values) UnityEngine.Object.Destroy(x);
            sprites.Clear(); textures.Clear(); sounds.Clear(); patches.Clear(); groundRows.Clear(); unavailableTextures.Clear();
        }
    }
}
