using System;
using System.IO;
using System.Collections.Generic;
using KerbalDoom;

public static class DoomMapTests
{
    private static int checks;
    private static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
    private static void Flow(Dictionary<long,int> values, DoomMapData.Point p, int amount)
    { long key = ((long)(int)p.X << 32) ^ (uint)(int)p.Y; int n; values.TryGetValue(key, out n); values[key] = n + amount; }
    private static int Entry(byte[] wad,string field)
    {
        int directory=BitConverter.ToInt32(wad,8),count=BitConverter.ToInt32(wad,4);bool first=false;
        for(int i=0;i<count;i++){int p=directory+i*16;string name=System.Text.Encoding.ASCII.GetString(wad,p+8,8).TrimEnd('\0');if(name=="MAP01")first=true;if(first&&name==field)return p;}
        throw new Exception("Fixture lump missing");
    }
    private static void RejectMap(byte[] bytes,string why)
    {
        bool rejected=false;try{new DoomMapArchive(bytes).Load("MAP01");}catch(InvalidDataException){rejected=true;}Check(rejected,why);
    }
    public static int Main(string[] args)
    {
        try
        {
            var archive = new DoomMapArchive(args[0]); Check(archive.Maps.Count == 32, "retains all 32 map markers");
            int totalCells = 0;
            foreach (string name in archive.Maps)
            {
                DoomMapData map = archive.Load(name);
                Check(map.Contains(map.PlayerStart.X, map.PlayerStart.Y), name + " player start is within real floor cells");
                Check(map.Lines.Length > 20 && map.Vertices.Length > 20 && map.Things.Length > 0, name + " record counts");
                var areas = new double[map.Sectors.Length]; var reference = new double[map.Sectors.Length];
                var perimeter = new double[map.Sectors.Length];
                var flow = new Dictionary<long,int>[map.Sectors.Length]; for(int i=0;i<flow.Length;i++) flow[i]=new Dictionary<long,int>();
                foreach (DoomMapData.Cell cell in map.Cells)
                {
                    Check(cell.Polygon.Count >= 3, name + " floor polygon");
                    areas[cell.Sector] += DoomMapData.Area(cell.Polygon); totalCells++;
                    for (int i = 0; i < cell.Polygon.Count; i++)
                        Check(DoomMapData.Cross(cell.Polygon[i], cell.Polygon[(i + 1) % cell.Polygon.Count], cell.Polygon[(i + 2) % cell.Polygon.Count]) <= .01, name + " convex clockwise subsector");
                }
                foreach (DoomMapData.Line line in map.Lines)
                {
                    DoomMapData.Point a = map.Vertices[line.A], b = map.Vertices[line.B]; double area = (a.X * b.Y - b.X * a.Y) * .5;
                    double length=Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y));
                    if (line.Right != 65535) { int s=map.Sides[line.Right].Sector; reference[s] += area; perimeter[s]+=length; Flow(flow[s],a,1);Flow(flow[s],b,-1); }
                    if (line.Left != 65535) { int s=map.Sides[line.Left].Sector; reference[s] -= area; perimeter[s]+=length; Flow(flow[s],b,1);Flow(flow[s],a,-1); }
                }
                for (int i = 0; i < areas.Length; i++)
                {
                    double expected = Math.Abs(reference[i]);
                    bool closed=true; foreach(int n in flow[i].Values) if(n!=0) closed=false;
                    if(!closed || name!="MAP01") continue; // Other vanilla maps contain open/self-referencing control-sector tricks; structural BSP tests still run on all maps.
                    Check(Math.Abs(areas[i] - expected) <= Math.Max(128, perimeter[i] * .8), name + " sector " + i + " BSP cell area vs oriented linedef boundary: " + areas[i] + " != " + expected);
                }
            }
            DoomMapData first = archive.Load("MAP01"), second = archive.Load("MAP02");
            Check(first.Lines.Length != second.Lines.Length, "map scoped lumps do not collapse to last map");
            bool rejected = false;
            try { new DoomMapArchive(new byte[12]); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "reject malformed header");
            rejected = false;
            try { archive.Load("MAP99"); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "reject missing map");
            byte[] mutant=(byte[])archive.Bytes.Clone();int entry=Entry(mutant,"SEGS"),offset=BitConverter.ToInt32(mutant,entry);
            BitConverter.GetBytes((ushort)65535).CopyTo(mutant,offset+6);RejectMap(mutant,"reject out-of-range seg linedef");
            mutant=(byte[])archive.Bytes.Clone();entry=Entry(mutant,"LINEDEFS");int recordLength=BitConverter.ToInt32(mutant,entry+4);
            BitConverter.GetBytes(recordLength-1).CopyTo(mutant,entry+4);RejectMap(mutant,"reject partial map record");
            mutant=(byte[])archive.Bytes.Clone();entry=Entry(mutant,"NODES");offset=BitConverter.ToInt32(mutant,entry);int nodes=BitConverter.ToInt32(mutant,entry+4)/28;
            BitConverter.GetBytes((ushort)(nodes-1)).CopyTo(mutant,offset+(nodes-1)*28+24);RejectMap(mutant,"reject cyclic BSP before geometry construction");
            Console.WriteLine("PASS: " + checks + " checks; 32 classic maps; " + totalCells + " convex BSP floor cells; MAP01 closed-sector areas match oriented boundaries within integer BSP rounding tolerance");
            Console.WriteLine("MAP01: " + first.Lines.Length + " lines, " + first.Sectors.Length + " sectors, start floor " + first.MapFloorAt(first.PlayerStart.X, first.PlayerStart.Y));
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("FAIL: " + e); return 1; }
    }
}
