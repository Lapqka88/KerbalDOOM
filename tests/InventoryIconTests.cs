using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using KerbalDoom;

internal static class InventoryIconTests
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new Exception("Pass DOOM2.WAD and DoomInventoryIcons.cs");
            WadArchive wad = new WadArchive(args[0]);
            string source = File.ReadAllText(args[1]);
            MatchCollection mappings = Regex.Matches(source, @"\{""(kd[A-Za-z]+)"",\s*""([A-Z0-9]+)""\}");
            HashSet<string> items = new HashSet<string>();
            foreach (Match match in mappings)
            {
                string item = match.Groups[1].Value, lump = match.Groups[2].Value;
                if (!items.Add(item)) throw new Exception("Duplicate cargo mapping: " + item);
                WadArchive.Patch patch = wad.DecodePatch(lump);
                if (patch == null || patch.Width < 1 || patch.Height < 1) throw new Exception("Missing icon: " + item + " / " + lump);
                bool visible = false;
                for (int i = 3; i < patch.Pixels.Length; i += 4) if (patch.Pixels[i] != 0) { visible = true; break; }
                if (!visible) throw new Exception("Empty icon: " + item);
            }
            if (items.Count != 14) throw new Exception("Expected 14 cargo icons, got " + items.Count);
            Console.WriteLine("PASS: 14 actual source cargo mappings resolve to visible original WAD patches.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("FAIL: " + e.Message); return 1; }
    }
}
