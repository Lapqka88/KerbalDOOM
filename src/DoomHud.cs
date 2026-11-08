using System;
using System.Collections.Generic;
using UnityEngine;

namespace KerbalDoom
{
    // Reconstructs the original status bar layout from the owner's IWAD; no retail assets are shipped.
    public static class DoomHud
    {
        public const float NativeWidth = 320, NativeHeight = 32;
        public static float PixelScale(float width, float height)
        { return Mathf.Max(1, Mathf.Floor(Mathf.Min(width / NativeWidth, height / 200f))); }
        public static float BarHeight(float width, float height) { return NativeHeight * PixelScale(width, height); }
        public static void Draw(DoomAssets assets, float width, float height, int health, int armor, int weapon, int[] ammo,
            bool infinite, int kills, float hurt, bool[] owned = null, int keyMask = 0, int skullMask = 0)
        {
            if (assets == null) return;
            float s = PixelScale(width, height), barHeight = NativeHeight * s;
            float left = (width - NativeWidth * s) * .5f, top = height - barHeight;
            Color oldColor = GUI.color;
            GUI.color = Color.white;
            DoomUi.DrawPanel(assets, new Rect(0, top, width, barHeight));
            Patch(assets, "STBAR", left, top, 0, 0, s);
            Patch(assets, "STARMS", left, top, 104, 0, s);
            int ammoSlot = weapon >= 0 && weapon < WeaponSpec.All.Length ? WeaponSpec.All[weapon].AmmoSlot : 0;
            bool melee = weapon >= 0 && weapon < WeaponSpec.All.Length && WeaponSpec.All[weapon].Cost == 0;
            if (!melee)
            {
                if (infinite) DoomUi.DrawText(assets, "INF", new Rect(left + 3 * s, top + 3 * s, 40 * s, 16 * s), s);
                else Number(assets, Value(ammo, ammoSlot), 44, 3, left, top, s, true, 3);
            }
            Number(assets, Mathf.Max(0, health), 90, 3, left, top, s, true, 3);
            Patch(assets, "STTPRCNT", left, top, 90, 3, s);
            Number(assets, Mathf.Max(0, armor), 221, 3, left, top, s, true, 3);
            Patch(assets, "STTPRCNT", left, top, 221, 3, s);
            int pain = Mathf.Clamp((100 - Math.Min(100, health)) * 5 / 101, 0, 4);
            string face = health <= 0 ? "STFDEAD0" : hurt > .05f ? "STFOUCH" + pain : "STFST" + pain + ((int)(Time.realtimeSinceStartup * 1.5f) % 3);
            if (assets.TryTexture(face) == null) face = "STFST" + pain + "0";
            Patch(assets, face, left, top, 143, 0, s);
            int[] slots = {0, 1, 3, 4, 5, 7};
            for (int i = 0; i < slots.Length; i++)
            {
                bool selected = weapon == slots[i] || i == 1 && weapon == 2;
                bool have = owned == null ? selected : Has(owned, slots[i]) || i == 1 && Has(owned, 2);
                string lump = (have ? "STYSNUM" : "STGNUM") + (i + 2);
                bool fallbackGray = assets.TryTexture(lump) == null;
                if (fallbackGray) lump = "STYSNUM" + (i + 2);
                Color tint = GUI.color;
                if (!have && fallbackGray) GUI.color = new Color(.5f, .5f, .5f, 1);
                Patch(assets, lump, left, top, 111 + (i % 3) * 12, 4 + (i / 3) * 10, s);
                GUI.color = tint;
            }
            int[] y = {5, 11, 23, 17};
            int[] maximum = {400, 100, 50, 600};
            for (int i = 0; i < 4; i++)
            {
                Number(assets, Value(ammo, i), 288, y[i], left, top, s, false, 3);
                Number(assets, maximum[i], 314, y[i], left, top, s, false, 3);
            }
            for (int i = 0; i < 3; i++)
            {
                bool skull = (skullMask & (1 << i)) != 0;
                if (skull || (keyMask & (1 << i)) != 0)
                    Patch(assets, "STKEYS" + (i + (skull ? 3 : 0)), left, top, 239, 3 + i * 10, s);
            }
            if (left >= 54 * s)
            {
                DoomUi.DrawText(assets, "F7 MENU", new Rect(8 * s, top + 5 * s, left - 12 * s, 10 * s), s);
                DoomUi.DrawText(assets, "KILLS " + Math.Max(0, kills), new Rect(left + 320 * s + 7 * s, top + 5 * s, left - 12 * s, 20 * s), s);
            }
            GUI.color = oldColor;
        }
        private static bool Has(bool[] owned, int index) { return index >= 0 && index < owned.Length && owned[index]; }
        private static int Value(int[] values, int index) { return values != null && index >= 0 && index < values.Length ? Math.Max(0, values[index]) : 0; }
        private static void Number(DoomAssets assets, int value, float right, float y, float left, float top, float s, bool tall, int digits)
        {
            string text = Math.Min(value, (int)Math.Pow(10, digits) - 1).ToString();
            string prefix = tall ? "STTNUM" : "STYSNUM";
            float x = right;
            for (int i = text.Length - 1; i >= 0; i--)
            {
                Texture2D digit = assets.TryTexture(prefix + text[i]);
                if (digit == null) continue;
                x -= digit.width;
                GUI.DrawTexture(new Rect(left + x * s, top + y * s, digit.width * s, digit.height * s), digit, ScaleMode.StretchToFill, true);
            }
        }
        private static void Patch(DoomAssets assets, string name, float left, float top, float x, float y, float scale)
        {
            Texture2D image = assets.TryTexture(name); if (image == null) return;
            GUI.DrawTexture(new Rect(left + x * scale, top + y * scale, image.width * scale, image.height * scale), image, ScaleMode.StretchToFill, true);
        }
    }

    public static class DoomUi
    {
        private static readonly Stack<GUISkin> previousSkins = new Stack<GUISkin>();
        private static GUISkin skin;
        private static DoomAssets skinAssets;
        private static GUIStyle fallback;
        public static void Begin(DoomAssets assets)
        {
            previousSkins.Push(GUI.skin);
            if (assets == null) return;
            if (skin == null || skinAssets != assets)
            {
                if (skin != null) UnityEngine.Object.Destroy(skin);
                skin = UnityEngine.Object.Instantiate(GUI.skin); skinAssets = assets;
                Texture2D metal = assets.Flat("FLOOR7_2") ?? assets.Flat("CEIL5_2");
                Texture2D stone = assets.Flat("FLOOR4_8") ?? metal;
                skin.window.normal.background = stone;
                skin.window.onNormal.background = stone;
                skin.window.border = new RectOffset(5, 5, 5, 5);
                skin.window.padding = new RectOffset(15, 15, 30, 15);
                skin.window.normal.textColor = new Color(1, .35f, .18f);
                skin.window.fontStyle = FontStyle.Bold; skin.window.fontSize = 17;
                skin.label.normal.textColor = new Color(.9f, .84f, .66f);
                skin.label.fontSize = 15; skin.label.wordWrap = true;
                skin.button.normal.background = metal; skin.button.onNormal.background = metal;
                skin.button.hover.background = stone; skin.button.onHover.background = stone;
                skin.button.active.background = stone; skin.button.onActive.background = stone;
                skin.button.normal.textColor = new Color(.85f, .74f, .52f);
                skin.button.hover.textColor = new Color(1, .35f, .2f); skin.button.active.textColor = Color.yellow;
                skin.button.fontStyle = FontStyle.Bold; skin.button.fontSize = 15;
                skin.button.border = new RectOffset(3, 3, 3, 3); skin.button.padding = new RectOffset(9, 9, 7, 7);
                skin.toggle.normal.textColor = new Color(.9f, .84f, .66f); skin.toggle.fontSize = 15;
                skin.textField.normal.textColor = new Color(1, .9f, .65f);
                skin.textField.normal.background = metal; skin.textField.fontSize = 14;
                fallback = new GUIStyle(skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
            }
            GUI.skin = skin;
        }
        public static void End() { if (previousSkins.Count != 0) GUI.skin = previousSkins.Pop(); }
        public static void DrawPanel(DoomAssets assets, Rect rect, bool selected = false)
        {
            Color old = GUI.color;
            Texture2D flat = assets == null ? null : assets.Flat("FLOOR7_2") ?? assets.Flat("FLOOR4_8");
            if (flat != null) GUI.DrawTextureWithTexCoords(rect, flat, new Rect(0, 0, rect.width / 128f, rect.height / 128f));
            else { GUI.color = new Color(.12f, .1f, .08f, .98f); GUI.DrawTexture(rect, Texture2D.whiteTexture); }
            GUI.color = selected ? new Color(.8f, .2f, .08f) : new Color(.42f, .3f, .18f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 2, rect.width, 2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 2, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - 2, rect.y, 2, rect.height), Texture2D.whiteTexture);
            GUI.color = old;
        }
        public static void DrawTitle(DoomAssets assets, Rect rect)
        {
            Texture2D title = assets == null ? null : assets.TryTexture("M_DOOM");
            if (title != null) GUI.DrawTexture(rect, title, ScaleMode.ScaleToFit, true);
            else DrawText(assets, "KERBAL DOOM", rect, 3);
        }
        public static void DrawText(DoomAssets assets, string value, Rect rect, float pixelScale = 2)
        {
            if (string.IsNullOrEmpty(value)) return;
            string text = value.ToUpperInvariant();
            bool original = assets != null;
            foreach (char c in text)
                if (c != ' ' && c != '\n' && (c < 33 || c > 95 || assets == null || assets.TryTexture("STCFN" + ((int)c).ToString("D3")) == null)) { original = false; break; }
            if (!original)
            {
                GUIStyle style = fallback ?? new GUIStyle(GUI.skin.label) { wordWrap = true, fontStyle = FontStyle.Bold };
                int oldSize = style.fontSize; style.fontSize = Mathf.RoundToInt(Mathf.Max(12, pixelScale * 8));
                GUI.Label(rect, value, style); style.fontSize = oldSize; return;
            }
            float x = rect.x, y = rect.y;
            foreach (char c in text)
            {
                if (c == '\n') { x = rect.x; y += 9 * pixelScale; continue; }
                if (c == ' ') { x += 4 * pixelScale; continue; }
                Texture2D glyph = assets.TryTexture("STCFN" + ((int)c).ToString("D3"));
                if (x + glyph.width * pixelScale > rect.xMax) { x = rect.x; y += 9 * pixelScale; }
                if (y + glyph.height * pixelScale > rect.yMax) break;
                GUI.DrawTexture(new Rect(x, y, glyph.width * pixelScale, glyph.height * pixelScale), glyph, ScaleMode.StretchToFill, true);
                x += glyph.width * pixelScale;
            }
        }
    }
}
