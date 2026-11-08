using System;
using System.Collections.Generic;
using System.IO;
using KSP.UI.Screens;
using UnityEngine;
using UnityEngine.UI;

namespace KerbalDoom
{
    // EditorPartIcon is also the stock inventory-slot icon, not just the editor catalogue entry.
    // Override only its UI thumbnail and icon-instance renderers: never change a physical part/model.
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    [DefaultExecutionOrder(3000)]
    public sealed class DoomInventoryIcons : MonoBehaviour
    {
        private static readonly Dictionary<string, string> itemSprites = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            {"kdPistol", "PISGA0"}, {"kdShotgun", "SHOTA0"}, {"kdSuperShotgun", "SGN2A0"},
            {"kdChaingun", "MGUNA0"}, {"kdRocketLauncher", "LAUNA0"}, {"kdPlasma", "PLASA0"},
            {"kdChainsaw", "CSAWA0"}, {"kdBfg", "BFUGA0"}, {"kdBullets", "CLIPA0"},
            {"kdShells", "SHELA0"}, {"kdRockets", "ROCKA0"}, {"kdCells", "CELLA0"},
            {"kdMedkit", "MEDIA0"}, {"kdArmor", "ARM1A0"}
        };
        private sealed class Override
        {
            public EditorPartIcon Icon;
            public string Item;
            public RawImage Image;
            public Texture OriginalTexture;
            public Texture AppliedTexture;
            public Color OriginalColor;
            public Rect OriginalUv;
            public bool OriginalActive, OriginalEnabled, Created;
            public Transform Model;
            public readonly Dictionary<Renderer, bool> Renderers = new Dictionary<Renderer, bool>();
        }
        private readonly Dictionary<EditorPartIcon, Override> applied = new Dictionary<EditorPartIcon, Override>();
        private readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private WadArchive archive;
        private string attemptedPath;
        private float nextScan;
        private static DoomInventoryIcons instance;
        // Optional access for our own equipment menu; native inventory replacement needs no root hook.
        public static Texture2D GetTexture(string item) { return instance == null ? null : instance.TextureFor(item); }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this; DontDestroyOnLoad(gameObject);
        }
        private void LateUpdate()
        {
            if (instance != this) return;
            if (Time.realtimeSinceStartup >= nextScan)
            {
                nextScan = Time.realtimeSinceStartup + .25f;
                try { Scan(); }
                catch (Exception e) { Debug.LogWarning("[KerbalDoom] Inventory icon scan: " + e.Message); }
            }
            // Native asynchronous thumbnail loading can finish after Create; retain our image after it.
            var stale = new List<EditorPartIcon>();
            foreach (var pair in applied)
            {
                Override entry = pair.Value;
                if (entry.Icon == null || entry.Image == null || entry.Icon.AvailPart == null || entry.Icon.AvailPart.name != entry.Item)
                { Restore(entry); stale.Add(pair.Key); continue; }
                Apply(entry);
            }
            foreach (EditorPartIcon icon in stale) applied.Remove(icon);
        }
        private void Scan()
        {
            string path = DoomGlobalSettings.Current.WadPath ?? "";
            if (!string.Equals(path, attemptedPath, StringComparison.OrdinalIgnoreCase))
            {
                ResetAssets(); attemptedPath = path;
                if (File.Exists(path))
                {
                    try { archive = new WadArchive(path); Debug.Log("[KerbalDoom] Native inventory WAD icons ready"); }
                    catch (Exception e) { Debug.LogWarning("[KerbalDoom] Inventory WAD icons unavailable: " + e.Message); }
                }
            }
            if (archive == null || (!HighLogic.LoadedSceneIsEditor && !HighLogic.LoadedSceneIsFlight)) return;
            foreach (EditorPartIcon icon in Resources.FindObjectsOfTypeAll<EditorPartIcon>())
            {
                // Do not mutate pooled prefab assets or invisible catalogue templates.
                if (icon == null || !icon.gameObject.scene.IsValid() || !icon.gameObject.activeInHierarchy || applied.ContainsKey(icon)) continue;
                AvailablePart part = icon.AvailPart;
                if (part == null || !itemSprites.ContainsKey(part.name) || TextureFor(part.name) == null) continue;
                RawImage image = icon.inventoryItemThumbnail;
                bool created = false;
                if (image == null)
                {
                    // RawImage's RequireComponent adds CanvasRenderer; no direct UIModule dependency needed here.
                    GameObject overlay = new GameObject("KerbalDoom/WADInventoryIcon", typeof(RectTransform), typeof(RawImage));
                    overlay.layer = icon.gameObject.layer; overlay.transform.SetParent(icon.transform, false);
                    RectTransform rect = (RectTransform)overlay.transform;
                    rect.anchorMin = new Vector2(.1f, .1f); rect.anchorMax = new Vector2(.9f, .9f);
                    rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
                    // Catalogue backgrounds may be child images: keep the transparent art above them.
                    // Padding leaves corner badges readable; raycastTarget=false preserves native clicks.
                    rect.SetAsLastSibling();
                    image = overlay.GetComponent<RawImage>(); image.raycastTarget = false; created = true;
                }
                var entry = new Override
                {
                    Icon = icon, Item = part.name, Image = image, Created = created,
                    OriginalTexture = image.texture, OriginalUv = image.uvRect, OriginalColor = image.color,
                    OriginalActive = image.gameObject.activeSelf, OriginalEnabled = image.enabled
                };
                applied.Add(icon, entry); Apply(entry);
            }
        }
        private void Apply(Override entry)
        {
            Texture2D texture = TextureFor(entry.Item); if (texture == null) return;
            entry.AppliedTexture = texture;
            entry.Image.texture = texture; entry.Image.uvRect = new Rect(0, 0, 1, 1);
            entry.Image.color = entry.Icon.isGrey ? new Color(.45f, .45f, .45f, 1) : Color.white;
            entry.Image.enabled = true;
            if (!entry.Image.gameObject.activeSelf) entry.Image.gameObject.SetActive(true);
            Transform model = entry.Icon.PartIcon;
            if (model != entry.Model)
            {
                RestoreRenderers(entry); entry.Model = model;
                if (model != null)
                    foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                        entry.Renderers[renderer] = renderer.forceRenderingOff;
            }
            foreach (Renderer renderer in entry.Renderers.Keys) if (renderer != null) renderer.forceRenderingOff = true;
        }
        private Texture2D TextureFor(string item)
        {
            Texture2D texture;
            if (textures.TryGetValue(item, out texture)) return texture;
            string lump; if (archive == null || !itemSprites.TryGetValue(item, out lump)) return null;
            WadArchive.Patch patch = archive.DecodePatch(lump); if (patch == null) return null;
            // Square padding preserves aspect ratio in native RawImage widgets and avoids UV flips from RenderTextures.
            const int side = 128, padding = 8;
            float scale = Math.Min((side - padding * 2f) / patch.Width, (side - padding * 2f) / patch.Height);
            int width = Math.Max(1, (int)Math.Round(patch.Width * scale)), height = Math.Max(1, (int)Math.Round(patch.Height * scale));
            int left = (side - width) / 2, bottom = (side - height) / 2;
            Color32[] pixels = new Color32[side * side];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                int sx = Math.Min(patch.Width - 1, (int)(x / scale)), sy = Math.Min(patch.Height - 1, (int)(y / scale));
                int source = ((patch.Height - 1 - sy) * patch.Width + sx) * 4;
                pixels[(bottom + y) * side + left + x] = new Color32(patch.Pixels[source], patch.Pixels[source + 1], patch.Pixels[source + 2], patch.Pixels[source + 3]);
            }
            texture = new Texture2D(side, side, TextureFormat.RGBA32, false);
            texture.name = "KerbalDoom/Inventory/" + item; texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(pixels); texture.Apply(false, true); textures[item] = texture; return texture;
        }
        private static void RestoreRenderers(Override entry)
        {
            foreach (var pair in entry.Renderers) if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
            entry.Renderers.Clear();
        }
        private static void Restore(Override entry)
        {
            RestoreRenderers(entry);
            if (entry.Image == null) return;
            if (entry.Created) { Destroy(entry.Image.gameObject); return; }
            // Native slots/catalogue widgets are pooled. Do not overwrite a new stock thumbnail
            // that was assigned while the component was recycled for a different part.
            if (entry.Image.texture != entry.AppliedTexture) return;
            if (entry.Icon != null && entry.Icon.AvailPart != null && entry.Icon.AvailPart.name != entry.Item)
            { entry.Image.texture = null; entry.Image.gameObject.SetActive(false); return; }
            entry.Image.texture = entry.OriginalTexture; entry.Image.uvRect = entry.OriginalUv; entry.Image.color = entry.OriginalColor;
            entry.Image.enabled = entry.OriginalEnabled; entry.Image.gameObject.SetActive(entry.OriginalActive);
        }
        private void ResetAssets()
        {
            foreach (var pair in applied) Restore(pair.Value); applied.Clear();
            foreach (Texture2D texture in textures.Values) if (texture != null) Destroy(texture); textures.Clear(); archive = null;
        }
        private void OnDestroy()
        {
            ResetAssets(); if (instance == this) instance = null;
        }
    }
}
