using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KerbalDoom;

internal static class WadTests
{
    private static int assertions;
    private static void Check(bool condition, string message)
    { assertions++; if (!condition) throw new Exception(message); }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (InvalidDataException) { assertions++; return; }
        throw new Exception("Did not reject: " + message);
    }
    private static byte[] MakeWad(Dictionary<string, byte[]> lumps)
    {
        using (MemoryStream memory = new MemoryStream()) using (BinaryWriter w = new BinaryWriter(memory))
        {
            w.Write(Encoding.ASCII.GetBytes("IWAD")); w.Write(lumps.Count); w.Write(0);
            List<int> offsets = new List<int>();
            foreach (byte[] bytes in lumps.Values) { offsets.Add((int)memory.Position); w.Write(bytes); }
            int directory = (int)memory.Position, i = 0;
            foreach (KeyValuePair<string, byte[]> lump in lumps)
            {
                w.Write(offsets[i++]); w.Write(lump.Value.Length);
                byte[] name = new byte[8]; Encoding.ASCII.GetBytes(lump.Key).CopyTo(name, 0); w.Write(name);
            }
            memory.Position = 8; w.Write(directory); return memory.ToArray();
        }
    }
    public static int Main(string[] args)
    {
        try
        {
            Reject(delegate { new WadArchive(new byte[0]); }, "empty WAD");
            Reject(delegate { new WadArchive(Encoding.ASCII.GetBytes("NOPE12345678")); }, "bad magic");
            byte[] palette = new byte[768]; palette[3] = 10; palette[4] = 20; palette[5] = 30;
            byte[] patch;
            using (MemoryStream m = new MemoryStream()) using (BinaryWriter w = new BinaryWriter(m))
            {
                w.Write((short)1); w.Write((short)3); w.Write((short)-2); w.Write((short)3); w.Write(12);
                w.Write((byte)1); w.Write((byte)1); w.Write((byte)0); w.Write((byte)1); w.Write((byte)0); w.Write((byte)255);
                patch = m.ToArray();
            }
            var fixture = new Dictionary<string, byte[]> { {"PLAYPAL", palette}, {"TESTA2A8", patch} };
            WadArchive wad = new WadArchive(MakeWad(fixture));
            WadArchive.Patch p = wad.DecodePatch("TESTA2A8");
            Check(p.Width == 1 && p.Height == 3 && p.Left == -2 && p.Top == 3, "patch header and signed offsets");
            Check(p.Pixels[3] == 0 && p.Pixels[7] == 255 && p.Pixels[11] == 0, "transparent gaps");
            Check(p.Pixels[4] == 10 && p.Pixels[5] == 20 && p.Pixels[6] == 30, "palette mapping");
            Check(CombatRules.BottomOpaqueRow(p) == 1, "visible sole excludes transparent bottom padding");
            Check(!CombatRules.AmbientAllowed("Kerbin", false, true, true, false, 1), "no auto spawn on Kerbin even with modified home world");
            Check(!CombatRules.AmbientAllowed("OtherHome", true, true, true, false, 1), "no auto spawn on home world");
            Check(CombatRules.AmbientAllowed("Duna", false, true, true, false, 1), "Duna encounter without combat mode");
            Check(CombatRules.AmbientAllowed("Mun", false, true, true, false, 1), "Mun encounter");
            Check(!CombatRules.AmbientAllowed("Jool", false, false, false, false, 1), "no terrain on gas giant");
            Check(!CombatRules.AmbientAllowed("Laythe", false, true, true, true, 0), "no spawn in ocean");
            Check(!CombatRules.AmbientAllowed("Duna", false, true, false, false, 1000), "no orbital spawn");
            var hull = new PartDamageState(1);
            Check(hull.Health == 180 && !hull.Apply(90) && hull.Health == 90, "rocket damages hull without instant destruction");
            Check(!hull.Apply(-20) && hull.Health == 90, "negative damage cannot heal hull");
            Check(hull.Apply(90) && hull.Health == 0, "accumulated damage destroys hull");
            Check(CombatRules.PartDurability(-1) == 60 && CombatRules.PartDurability(100) == 1000, "hull durability limits");
            Check(WaveRules.Size(1) == 5 && WaveRules.Size(2) == 7 && WaveRules.Size(100) == 30 && WaveRules.Size(int.MaxValue) == 30, "wave size and overflow cap");
            Check(!WaveRules.Boss(0) && WaveRules.Boss(5) && WaveRules.Boss(10) && !WaveRules.Boss(6), "boss cadence");
            Check(WaveRules.ResumeWave(4, true) == 4 && WaveRules.ResumeWave(4, false) == 3 && WaveRules.ResumeWave(0, false) == 0, "reload replays unfinished waves only");
            Check(WaveRules.ReloadAmount(380, 400, 100) == 20 && WaveRules.ReloadAmount(400, 400, 100) == 0, "ammo pack cap and no consumption when full");
            Check(SaveRules.Read("NaN", 2, 0, 6) == 2 && SaveRules.Read("Infinity", 2, 0, 6) == 2 && SaveRules.Read("corrupt", 2, 0, 6) == 2, "save corrupt numeric fallback");
            Check(SaveRules.Read("-9", 2, 0, 6) == 0 && SaveRules.Read("99", 2, 0, 6) == 6, "save bounds");
            var culture = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ru-RU");
                Check(SaveRules.Format(1.75f) == "1.75" && SaveRules.Read(SaveRules.Format(1.75f), 1, 0, 6) == 1.75f, "Russian locale settings round trip");
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = culture; }
            bool flip;
            Check(wad.FindSprite("TEST", 'A', 2, out flip) == "TESTA2A8" && !flip, "unmirrored rotation");
            Check(wad.FindSprite("TEST", 'A', 8, out flip) == "TESTA2A8" && flip, "mirrored second rotation");
            patch[12] = 254;
            byte[] truncated = new byte[patch.Length - 1]; Array.Copy(patch, truncated, truncated.Length);
            fixture["TESTA2A8"] = truncated;
            Reject(delegate { new WadArchive(MakeWad(fixture)).DecodePatch("TESTA2A8"); }, "unterminated column");
            byte[] broken = MakeWad(fixture); BitConverter.GetBytes(int.MaxValue).CopyTo(broken, 8);
            Reject(delegate { new WadArchive(broken); }, "directory overflow");
            byte[] dmx = new byte[43]; BitConverter.GetBytes((short)3).CopyTo(dmx, 0); BitConverter.GetBytes((short)11025).CopyTo(dmx, 2);
            BitConverter.GetBytes(35).CopyTo(dmx, 4); dmx[24] = 0; dmx[25] = 128; dmx[26] = 255;
            fixture["DSSOUND"] = dmx;
            var sound = new WadArchive(MakeWad(fixture)).DecodeSound("DSSOUND");
            Check(sound.Rate == 11025 && sound.Samples.Length == 3 && sound.Samples[0] == -1 && sound.Samples[1] == 0 && sound.Samples[2] > .99f, "DMX padding/rate/PCM");
            dmx[0] = 1;
            Reject(delegate { new WadArchive(MakeWad(fixture)).DecodeSound("DSSOUND"); }, "non-DMX sound");

            if (args.Length == 0) throw new Exception("Pass path to real DOOM2.WAD");
            WadArchive real = new WadArchive(args[0]);
            foreach (string name in new [] {"STBAR", "STARMS", "STTPRCNT", "M_DOOM", "STFST00", "STFOUCH0", "STKEYS0", "STKEYS1", "STKEYS2", "STKEYS3", "STKEYS4", "STKEYS5"})
                Check(real.DecodePatch(name) != null, "original HUD patch " + name);
            for (int digit = 0; digit < 10; digit++)
            { Check(real.DecodePatch("STTNUM" + digit) != null, "HUD tall digit " + digit); Check(real.DecodePatch("STYSNUM" + digit) != null, "HUD small digit " + digit); }
            foreach (string flat in new [] {"FLOOR7_2", "FLOOR4_8"}) Check(real.Read(flat).Length == 4096, "menu flat size " + flat);
            foreach (WeaponSpec weapon in WeaponSpec.All)
            {
                foreach (char frame in weapon.FireFrames) Check(real.Contains(weapon.Prefix + frame + "0"), "actual weapon animation asset " + weapon.Prefix + frame);
                Check(real.Contains(weapon.Sound), "actual weapon sound " + weapon.Name);
                Check(weapon.AmmoSlot >= 0 && weapon.AmmoSlot < 4 && weapon.Cost >= 0, "weapon ammo definition " + weapon.Name);
            }
            foreach (MonsterSpec monster in MonsterSpec.All)
            {
                Check(real.Contains(monster.AttackSound) && real.Contains(monster.DeathSound), "actual monster audio " + monster.Prefix);
                Check(monster.Height >= monster.Radius * 2 && monster.Health > 0, "actual monster collider and health " + monster.Prefix);
                for (char frame = monster.DeathStart; frame <= monster.DeathEnd; frame++)
                {
                    bool mirrored;
                    Check(real.FindSprite(monster.Prefix, frame, 1, out mirrored) != null, "actual monster death asset " + monster.Prefix + frame);
                }
            }
            foreach (string prefix in new [] { "PISG", "SHTG", "SHT2", "CHGG", "MISG", "PLSG", "SAWG", "BFGG" })
            {
                var ready = real.DecodePatch(prefix + "A0");
                foreach (int height in new [] { 480, 720, 1080, 1440 })
                {
                    float width = height * 16f / 9, scale = height / 240f;
                    var placed = CombatRules.WeaponPlacement(ready, ready, width, height, 82, scale);
                    Check(Math.Abs(placed.X + placed.Width / 2 - width / 2) < .001f, "idle weapon screen centre " + prefix + height);
                    Check(Math.Abs(placed.Y + placed.Height - (height - 84)) < .001f, "idle weapon above HUD " + prefix + height);
                    var fire = real.DecodePatch(prefix + "B0");
                    if (fire != null)
                    {
                        var animated = CombatRules.WeaponPlacement(fire, ready, width, height, 82, scale);
                        Check(Math.Abs((animated.X - placed.X) - (ready.Left - fire.Left) * scale) < .001f, "horizontal fire animation origin " + prefix);
                        Check(Math.Abs((animated.Y - placed.Y) - (ready.Top - fire.Top) * scale) < .001f, "vertical fire animation origin " + prefix);
                    }
                }
            }
            int images = 0, sounds = 0;
            string[] prefixes = { "POSS", "SPOS", "TROO", "SARG", "HEAD", "SKEL", "CPOS", "BOS2", "CYBR", "PISG", "PISF", "SHTG", "SHTF", "SHT2", "CHGG", "CHGF", "MISG", "MISF", "PLSG", "PLSF", "SAWG", "BFGG", "BFGF", "CLIP", "SHEL", "CELL", "MEDI", "ARM1", "STFS" };
            foreach (string name in real.Names)
            {
                if (name.StartsWith("DS")) { Check(real.DecodeSound(name).Samples.Length > 0, "real sound " + name); sounds++; }
                bool wanted = name == "STBAR";
                foreach (string prefix in prefixes) if (name.StartsWith(prefix)) wanted = true;
                if (!wanted) continue;
                var image = real.DecodePatch(name);
                Check(image != null && image.Pixels.Length == image.Width * image.Height * 4, "real patch " + name); images++;
                bool visible = false;
                for (int i = 3; i < image.Pixels.Length; i += 4) if (image.Pixels[i] == 255) { visible = true; break; }
                Check(visible, "real sprite visible " + name);
            }
            foreach (string prefix in new [] { "POSS", "SPOS", "TROO", "SARG", "HEAD", "SKEL", "CPOS", "BOS2", "CYBR" })
            {
                for (int rotation = 1; rotation <= 8; rotation++)
                    Check(real.FindSprite(prefix, 'A', rotation, out flip) != null, "eight-view coverage " + prefix + rotation);
            }
            foreach (string name in new [] { "PISGA0", "SHTGA0", "SHT2A0", "CHGGA0", "MISGA0", "PLSGA0", "DSPISTOL", "DSSHOTGN", "DSDSHTGN", "DSRLAUNC", "DSPLASMA", "DSBAREXP", "DSPLPAIN", "DSPDIEHI", "DSPODTH1", "DSPODTH2", "DSBGDTH1", "DSSGTDTH", "DSCACDTH", "DSSKEDTH", "DSFIRSHT", "DSSGTATK", "DSSKEATK", "DSNOWAY" })
                Check(real.Contains(name), "required asset " + name);
            Check(images > 200 && sounds > 50, "substantial real-WAD coverage");
            foreach (string name in new [] {"SAWGA0", "BFGGA0", "BFGFA0", "CLIPA0", "SHELA0", "CELLA0", "MEDIA0", "ARM1A0", "DSBFG", "DSSAWHIT", "DSWPNUP", "DSITEMUP", "DSKNTDTH", "DSCYBDTH"})
                Check(real.Contains(name), "0.3 required asset " + name);
            Console.WriteLine("PASS: " + assertions + " assertions; " + images + " real patches; " + sounds + " real sounds; 9 monsters x 8 directions; 8 weapons; saves/waves/ammo.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("FAIL: " + e); return 1; }
    }
}
