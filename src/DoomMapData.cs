using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace KerbalDoom
{
    // Classic binary Doom maps. No Unity dependency: parser and geometry are tested offline.
    public sealed class DoomMapArchive
    {
        public readonly byte[] Bytes;
        private sealed class Lump { public string Name; public int Offset, Length; }
        private readonly List<Lump> lumps = new List<Lump>();
        public readonly List<string> Maps = new List<string>();
        public DoomMapArchive(string path) : this(File.ReadAllBytes(path)) { }
        public DoomMapArchive(byte[] bytes)
        {
            Bytes = bytes;
            if (bytes == null || bytes.Length < 12) throw new InvalidDataException("Truncated WAD");
            string magic = Encoding.ASCII.GetString(bytes, 0, 4);
            if (magic != "IWAD" && magic != "PWAD") throw new InvalidDataException("Not a WAD");
            int count = BitConverter.ToInt32(bytes, 4), directory = BitConverter.ToInt32(bytes, 8);
            if (count < 0 || count > 100000 || directory < 12 || (long)directory + count * 16L > bytes.Length) throw new InvalidDataException("Invalid WAD directory");
            for (int i = 0; i < count; i++)
            {
                int p = directory + i * 16;
                var lump = new Lump { Name = Encoding.ASCII.GetString(bytes, p + 8, 8).TrimEnd('\0'), Offset = BitConverter.ToInt32(bytes, p), Length = BitConverter.ToInt32(bytes, p + 4) };
                if (lump.Offset < 0 || lump.Length < 0 || (long)lump.Offset + lump.Length > bytes.Length) throw new InvalidDataException("Lump outside WAD");
                lumps.Add(lump);
                if (IsMap(lump.Name)) Maps.Add(lump.Name);
            }
        }
        private static bool IsMap(string name) { return Regex.IsMatch(name, "^(MAP[0-9][0-9]|E[0-9]M[0-9])$"); }
        public DoomMapData Load(string name)
        {
            int start = -1, end = lumps.Count;
            for (int i = 0; i < lumps.Count; i++) if (lumps[i].Name == name) start = i;
            if (start < 0) throw new InvalidDataException("Map not found: " + name);
            for (int i = start + 1; i < lumps.Count; i++) if (IsMap(lumps[i].Name)) { end = i; break; }
            var fields = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            for (int i = start + 1; i < end; i++)
            {
                Lump l = lumps[i];
                if (l.Name == "BEHAVIOR" || l.Name == "TEXTMAP") throw new InvalidDataException("Hexen/UDMF maps are not supported");
                if (l.Name != "THINGS" && l.Name != "LINEDEFS" && l.Name != "SIDEDEFS" && l.Name != "VERTEXES" && l.Name != "SEGS" && l.Name != "SSECTORS" && l.Name != "NODES" && l.Name != "SECTORS") continue;
                byte[] data = new byte[l.Length]; Buffer.BlockCopy(Bytes, l.Offset, data, 0, l.Length); fields[l.Name] = data;
            }
            return new DoomMapData(name, fields);
        }
    }
    public sealed class DoomMapData
    {
        public struct Point { public double X, Y; public Point(double x, double y) { X = x; Y = y; } }
        public sealed class Thing { public int X, Y, Angle, Type, Flags; }
        public sealed class Line { public int A, B, Flags, Special, Tag, Right, Left; }
        public sealed class Side { public int XOffset, YOffset, Sector; public string Upper, Lower, Middle; }
        public sealed class Sector { public int Floor, Ceiling, Light, Special, Tag; public string FloorTexture, CeilingTexture; }
        public sealed class Seg { public int A, B, Line, Direction, Offset; }
        public sealed class Subsector { public int First, Count; }
        public sealed class Node { public int X, Y, DX, DY, Right, Left; }
        public sealed class Cell { public int Sector, Subsector; public List<Point> Polygon; }
        public readonly string Name;
        public readonly Point[] Vertices;
        public readonly Thing[] Things;
        public readonly Line[] Lines;
        public readonly Side[] Sides;
        public readonly Sector[] Sectors;
        public readonly Seg[] Segs;
        public readonly Subsector[] Subsectors;
        public readonly Node[] Nodes;
        public readonly List<Cell> Cells = new List<Cell>();
        public readonly List<string> Warnings = new List<string>();
        public readonly Thing PlayerStart;
        public readonly double MinX, MaxX, MinY, MaxY;

        public DoomMapData(string name, Dictionary<string, byte[]> fields)
        {
            Name = name;
            byte[] raw = Required(fields, "VERTEXES", 4); Vertices = new Point[raw.Length / 4];
            double minX = double.MaxValue, minY = minX, maxX = double.MinValue, maxY = maxX;
            for (int i = 0; i < Vertices.Length; i++)
            {
                Vertices[i] = new Point(S(raw, i * 4), S(raw, i * 4 + 2));
                minX = Math.Min(minX, Vertices[i].X); maxX = Math.Max(maxX, Vertices[i].X); minY = Math.Min(minY, Vertices[i].Y); maxY = Math.Max(maxY, Vertices[i].Y);
            }
            if (Vertices.Length < 3) throw new InvalidDataException("Map has too few vertices");
            MinX = minX; MaxX = maxX; MinY = minY; MaxY = maxY;
            raw = Required(fields, "SECTORS", 26); Sectors = new Sector[raw.Length / 26];
            for (int i = 0; i < Sectors.Length; i++)
            {
                int p = i * 26;
                Sectors[i] = new Sector { Floor = S(raw, p), Ceiling = S(raw, p + 2), FloorTexture = N(raw, p + 4), CeilingTexture = N(raw, p + 12), Light = S(raw, p + 20), Special = S(raw, p + 22), Tag = U(raw, p + 24) };
                if (Sectors[i].Ceiling < Sectors[i].Floor) throw new InvalidDataException("Sector ceiling below floor");
            }
            if (Sectors.Length == 0) throw new InvalidDataException("No sectors");
            raw = Required(fields, "SIDEDEFS", 30); Sides = new Side[raw.Length / 30];
            for (int i = 0; i < Sides.Length; i++)
            {
                int p = i * 30; Sides[i] = new Side { XOffset = S(raw, p), YOffset = S(raw, p + 2), Upper = N(raw, p + 4), Lower = N(raw, p + 12), Middle = N(raw, p + 20), Sector = U(raw, p + 28) };
                Index(Sides[i].Sector, Sectors.Length, "sidedef sector");
            }
            raw = Required(fields, "LINEDEFS", 14); Lines = new Line[raw.Length / 14];
            for (int i = 0; i < Lines.Length; i++)
            {
                int p = i * 14; Lines[i] = new Line { A = U(raw, p), B = U(raw, p + 2), Flags = U(raw, p + 4), Special = U(raw, p + 6), Tag = U(raw, p + 8), Right = U(raw, p + 10), Left = U(raw, p + 12) };
                Index(Lines[i].A, Vertices.Length, "line vertex"); Index(Lines[i].B, Vertices.Length, "line vertex");
                if (Lines[i].Right != 65535) Index(Lines[i].Right, Sides.Length, "line side");
                if (Lines[i].Left != 65535) Index(Lines[i].Left, Sides.Length, "line side");
            }
            raw = Required(fields, "SEGS", 12); Segs = new Seg[raw.Length / 12];
            for (int i = 0; i < Segs.Length; i++)
            {
                int p = i * 12; Segs[i] = new Seg { A = U(raw, p), B = U(raw, p + 2), Line = U(raw, p + 6), Direction = U(raw, p + 8), Offset = U(raw, p + 10) };
                Index(Segs[i].A, Vertices.Length, "seg vertex"); Index(Segs[i].B, Vertices.Length, "seg vertex"); Index(Segs[i].Line, Lines.Length, "seg line");
                if (Segs[i].Direction > 1) throw new InvalidDataException("Invalid seg direction");
            }
            raw = Required(fields, "SSECTORS", 4); Subsectors = new Subsector[raw.Length / 4];
            for (int i = 0; i < Subsectors.Length; i++)
            {
                int p = i * 4; Subsectors[i] = new Subsector { Count = U(raw, p), First = U(raw, p + 2) };
                if (Subsectors[i].Count < 1 || (long)Subsectors[i].First + Subsectors[i].Count > Segs.Length) throw new InvalidDataException("Subsector outside seg table");
            }
            raw = Required(fields, "NODES", 28); Nodes = new Node[raw.Length / 28];
            for (int i = 0; i < Nodes.Length; i++)
            {
                int p = i * 28; Nodes[i] = new Node { X = S(raw, p), Y = S(raw, p + 2), DX = S(raw, p + 4), DY = S(raw, p + 6), Right = U(raw, p + 24), Left = U(raw, p + 26) };
                if (Nodes[i].DX == 0 && Nodes[i].DY == 0) throw new InvalidDataException("Zero BSP partition");
                CheckChild(Nodes[i].Right); CheckChild(Nodes[i].Left);
            }
            raw = Required(fields, "THINGS", 10); Things = new Thing[raw.Length / 10];
            for (int i = 0; i < Things.Length; i++)
            {
                int p = i * 10; Things[i] = new Thing { X = S(raw, p), Y = S(raw, p + 2), Angle = S(raw, p + 4), Type = U(raw, p + 6), Flags = U(raw, p + 8) };
                if (Things[i].Type == 1 && PlayerStart == null) PlayerStart = Things[i];
            }
            if (PlayerStart == null) throw new InvalidDataException("Map has no player 1 start");
            BuildCells();
        }
        private static short S(byte[] b, int p) { return BitConverter.ToInt16(b, p); }
        private static int U(byte[] b, int p) { return BitConverter.ToUInt16(b, p); }
        private static string N(byte[] b, int p) { return Encoding.ASCII.GetString(b, p, 8).TrimEnd('\0'); }
        private static void Index(int index, int count, string what) { if (index < 0 || index >= count) throw new InvalidDataException("Invalid " + what + " index"); }
        private void CheckChild(int child) { Index(child & 32767, (child & 32768) != 0 ? Subsectors.Length : Nodes.Length, "BSP child"); }
        private static byte[] Required(Dictionary<string, byte[]> fields, string name, int record)
        {
            byte[] raw; if (!fields.TryGetValue(name, out raw)) throw new InvalidDataException("Missing map lump: " + name);
            if (raw.Length % record != 0 || raw.Length / record > 65535) throw new InvalidDataException("Invalid map records: " + name); return raw;
        }
        private sealed class Work { public int Child, Depth; public List<Point> Polygon; }
        private void BuildCells()
        {
            if (Subsectors.Length < 1 || Nodes.Length == 0 && Subsectors.Length != 1) throw new InvalidDataException("Invalid empty BSP");
            var stack = new Stack<Work>();
            stack.Push(new Work { Child = Nodes.Length == 0 ? 32768 : Nodes.Length - 1, Polygon = new List<Point> {new Point(MinX, MinY), new Point(MinX, MaxY), new Point(MaxX, MaxY), new Point(MaxX, MinY)} });
            var seen = new bool[Nodes.Length]; var leaves = new bool[Subsectors.Length];
            while (stack.Count != 0)
            {
                Work work = stack.Pop();
                if (work.Depth > 256) throw new InvalidDataException("BSP depth limit");
                if ((work.Child & 32768) == 0)
                {
                    int i = work.Child; if (seen[i]) throw new InvalidDataException("Cyclic/shared BSP node"); seen[i] = true;
                    Node node = Nodes[i]; Point a = new Point(node.X, node.Y), b = new Point(node.X + node.DX, node.Y + node.DY);
                    stack.Push(new Work { Child = node.Left, Depth = work.Depth + 1, Polygon = Clip(work.Polygon, a, b, false) });
                    stack.Push(new Work { Child = node.Right, Depth = work.Depth + 1, Polygon = Clip(work.Polygon, a, b, true) });
                    continue;
                }
                int leaf = work.Child & 32767;
                if (leaves[leaf]) throw new InvalidDataException("Repeated BSP leaf"); leaves[leaf] = true;
                Subsector sub = Subsectors[leaf]; int sector = -1;
                for (int j = sub.First; j < sub.First + sub.Count; j++)
                {
                    Seg seg = Segs[j]; Line line = Lines[seg.Line]; int side = seg.Direction == 0 ? line.Right : line.Left;
                    Index(side, Sides.Length, "subsector side");
                    if (sector < 0) sector = Sides[side].Sector;
                    else if (sector != Sides[side].Sector && Warnings.Count == 0)
                        Warnings.Add("Нестандартные BSP-секторы: контрольные эффекты карты могут отличаться");
                    work.Polygon = Clip(work.Polygon, Vertices[seg.Direction == 0 ? line.A : line.B], Vertices[seg.Direction == 0 ? line.B : line.A], true);
                }
                Clean(work.Polygon);
                if (work.Polygon.Count >= 3 && Area(work.Polygon) > .000001) Cells.Add(new Cell { Sector = sector, Subsector = leaf, Polygon = work.Polygon });
            }
            if (Cells.Count == 0) throw new InvalidDataException("Map has no floor cells");
        }
        public static double Cross(Point a, Point b, Point p) { return (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X); }
        private static List<Point> Clip(List<Point> input, Point a, Point b, bool right)
        {
            var output = new List<Point>(); if (input.Count == 0) return output;
            Point previous = input[input.Count - 1]; double prev = Cross(a, b, previous) * (right ? 1 : -1);
            foreach (Point current in input)
            {
                double next = Cross(a, b, current) * (right ? 1 : -1);
                bool inside = next <= .000001, wasInside = prev <= .000001;
                if (inside != wasInside)
                {
                    double t = prev / (prev - next);
                    output.Add(new Point(previous.X + (current.X - previous.X) * t, previous.Y + (current.Y - previous.Y) * t));
                }
                if (inside) output.Add(current);
                previous = current; prev = next;
            }
            return output;
        }
        private static void Clean(List<Point> polygon)
        {
            for (int i = polygon.Count - 1; i >= 0 && polygon.Count > 1; i--)
            {
                Point a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                if (Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) < .00001) polygon.RemoveAt(i);
            }
        }
        public static double Area(List<Point> polygon)
        {
            double area = 0; for (int i = 0; i < polygon.Count; i++) { Point a = polygon[i], b = polygon[(i + 1) % polygon.Count]; area += a.X * b.Y - b.X * a.Y; } return Math.Abs(area) * .5;
        }
        public bool Contains(double x, double y)
        {
            if (x < MinX || x > MaxX || y < MinY || y > MaxY) return false;
            Point point = new Point(x, y);
            foreach (Cell cell in Cells)
            {
                bool inside = true;
                for (int i = 0; i < cell.Polygon.Count; i++) if (Cross(cell.Polygon[i], cell.Polygon[(i + 1) % cell.Polygon.Count], point) > .001) { inside = false; break; }
                if (inside) return true;
            }
            return false;
        }
        public int SectorAt(double x, double y)
        {
            if (Nodes.Length == 0)
            { Seg only = Segs[Subsectors[0].First]; Line boundary = Lines[only.Line]; return Sides[only.Direction == 0 ? boundary.Right : boundary.Left].Sector; }
            int child = Nodes.Length - 1, guard = 0; Point p = new Point(x, y);
            while ((child & 32768) == 0)
            {
                if (++guard > Nodes.Length) throw new InvalidDataException("Cyclic BSP");
                Node node = Nodes[child]; child = Cross(new Point(node.X, node.Y), new Point(node.X + node.DX, node.Y + node.DY), p) <= 0 ? node.Right : node.Left;
            }
            Seg seg = Segs[Subsectors[child & 32767].First]; Line line = Lines[seg.Line]; return Sides[seg.Direction == 0 ? line.Right : line.Left].Sector;
        }
        public double MapFloorAt(double x, double y) { return Sectors[SectorAt(x, y)].Floor; }
    }
}
