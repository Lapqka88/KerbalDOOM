using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using KerbalDoom;

public static class EffectsWadTests
{
    public static int Main(string[] args)
    {
        try
        {
            WadArchive wad = new WadArchive(args[0]);
            string source = File.ReadAllText(args[1]) + File.ReadAllText(args[2]);
            MatchCollection mappings = Regex.Matches(source, "(?:spritePrefix|prefix)\\s*=\\s*\"([A-Z0-9]{4})\";\\s*animation\\s*=\\s*\"([A-Z]+)\"");
            if (mappings.Count < 15) throw new Exception("Missing projectile/effect animation mappings");
            int lookups = 0; var decoded = new HashSet<string>();
            foreach (Match mapping in mappings)
                foreach (char frame in mapping.Groups[2].Value)
                    for (int rotation = 1; rotation <= 8; rotation++)
                    {
                        bool flip; string name = wad.FindSprite(mapping.Groups[1].Value, frame, rotation, out flip);
                        if (name == null) throw new Exception("Missing frame " + mapping.Groups[1].Value + frame + rotation);
                        if (decoded.Add(name))
                        {
                            WadArchive.Patch p = wad.DecodePatch(name);
                            bool opaque = false;
                            for (int i = 3; i < p.Pixels.Length; i += 4) if (p.Pixels[i] != 0) { opaque = true; break; }
                            if (!opaque) throw new Exception("Invisible effect " + name);
                        }
                        lookups++;
                    }
            Console.WriteLine("PASS: " + mappings.Count + " actual projectile/effect mappings, " + lookups + " frame/rotation lookups, " + decoded.Count + " decoded real WAD patches.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("FAIL: " + e); return 1; }
    }
}
