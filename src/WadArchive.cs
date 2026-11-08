using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KerbalDoom
{
    // Independent of Unity so binary decoding can be checked without starting KSP.
    public sealed class WadArchive
    {
        private readonly byte[] data;
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> spriteNames = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> flippedSprites = new HashSet<string>(StringComparer.Ordinal);
        private struct Entry { public int Offset, Length; }
        public IEnumerable<string> Names { get { return entries.Keys; } }
        public WadArchive(string path) : this(File.ReadAllBytes(path)) { }
        public WadArchive(byte[] bytes)
        {
            data = bytes;
            if (data == null || data.Length < 12) throw new InvalidDataException("Truncated WAD header");
            string magic = Encoding.ASCII.GetString(data, 0, 4);
            if (magic != "IWAD" && magic != "PWAD") throw new InvalidDataException("Not a WAD file");
            int count = I32(4), directory = I32(8);
            if (count < 0 || count > 100000 || directory < 12 || (long)directory + count * 16L > data.Length)
                throw new InvalidDataException("Invalid WAD directory");
            for (int i = 0; i < count; i++)
            {
                int p = directory + i * 16, start = I32(p), size = I32(p + 4);
                if (start < 0 || size < 0 || (long)start + size > data.Length) throw new InvalidDataException("Lump outside WAD");
                string name = Encoding.ASCII.GetString(data, p + 8, 8).TrimEnd('\0');
                entries[name] = new Entry { Offset = start, Length = size };
            }
            foreach (string name in entries.Keys)
            {
                if (name.Length != 6 && name.Length != 8) continue;
                if (name[5] < '0' || name[5] > '8') continue;
                string key = name.Substring(0, 6);
                spriteNames[key] = name; flippedSprites.Remove(key);
                if (name.Length == 8 && name[7] >= '0' && name[7] <= '8')
                {
                    key = name.Substring(0, 4) + name.Substring(6, 2);
                    spriteNames[key] = name; flippedSprites.Add(key);
                }
            }
        }
        private int I32(int p) { return BitConverter.ToInt32(data, p); }
        public bool Contains(string name) { return entries.ContainsKey(name); }
        public byte[] Read(string name)
        {
            Entry e;
            if (!entries.TryGetValue(name, out e)) return null;
            byte[] result = new byte[e.Length];
            Buffer.BlockCopy(data, e.Offset, result, 0, e.Length);
            return result;
        }
        public sealed class Patch
        {
            public int Width, Height, Left, Top;
            // RGBA, top row first. Transparent pixels have alpha zero.
            public byte[] Pixels;
        }
        public Patch DecodePatch(string name)
        {
            byte[] raw = Read(name), palette = Read("PLAYPAL");
            if (raw == null) return null;
            if (palette == null || palette.Length < 768 || raw.Length < 8) throw new InvalidDataException("Invalid palette/patch " + name);
            int w = BitConverter.ToUInt16(raw, 0), h = BitConverter.ToUInt16(raw, 2);
            if (w < 1 || h < 1 || w > 2048 || h > 2048 || raw.Length < 8 + 4 * w) throw new InvalidDataException("Invalid patch dimensions " + name);
            Patch patch = new Patch { Width = w, Height = h, Left = BitConverter.ToInt16(raw, 4), Top = BitConverter.ToInt16(raw, 6), Pixels = new byte[w * h * 4] };
            for (int x = 0; x < w; x++)
            {
                int p = BitConverter.ToInt32(raw, 8 + x * 4), previous = -1, posts = 0;
                if (p < 8 + w * 4 || p >= raw.Length) throw new InvalidDataException("Invalid column " + name);
                while (true)
                {
                    if (p >= raw.Length || ++posts > h + 256) throw new InvalidDataException("Unterminated column " + name);
                    int top = raw[p++];
                    if (top == 255) break;
                    if (p + 2 > raw.Length) throw new InvalidDataException("Truncated post " + name);
                    int length = raw[p++]; p++; // unused leading byte
                    if (p + length >= raw.Length) throw new InvalidDataException("Truncated pixels " + name);
                    if (top <= previous) top += previous; // tall-patch relative offset convention
                    previous = top;
                    for (int y = 0; y < length; y++)
                    {
                        int row = top + y, color = raw[p + y] * 3;
                        if (row >= h) continue;
                        int dst = (row * w + x) * 4;
                        patch.Pixels[dst] = palette[color]; patch.Pixels[dst + 1] = palette[color + 1];
                        patch.Pixels[dst + 2] = palette[color + 2]; patch.Pixels[dst + 3] = 255;
                    }
                    p += length + 1; // unused trailing byte
                }
            }
            return patch;
        }
        public sealed class Sound { public int Rate; public float[] Samples; }
        public Sound DecodeSound(string name)
        {
            byte[] raw = Read(name);
            if (raw == null) return null;
            if (raw.Length < 8 || BitConverter.ToUInt16(raw, 0) != 3) throw new InvalidDataException("Not a DMX sound: " + name);
            int rate = BitConverter.ToUInt16(raw, 2), count = BitConverter.ToInt32(raw, 4);
            if (rate < 1000 || rate > 96000 || count < 33 || count > raw.Length - 8) throw new InvalidDataException("Invalid sound length: " + name);
            // DMX has 16 padding samples at each end.
            float[] samples = new float[count - 32];
            for (int i = 0; i < samples.Length; i++) samples[i] = (raw[24 + i] - 128) / 128f;
            return new Sound { Rate = rate, Samples = samples };
        }
        public string FindSprite(string prefix, char frame, int rotation, out bool flip)
        {
            flip = false;
            string zero = prefix + frame + "0";
            if (Contains(zero)) return zero;
            string key = prefix + frame + (char)('0' + rotation), found;
            if (!spriteNames.TryGetValue(key, out found)) return null;
            flip = flippedSprites.Contains(key); return found;
        }
    }
}
