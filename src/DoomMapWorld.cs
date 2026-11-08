using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace KerbalDoom
{
    // Imported native geometry. The caller owns vessel placement, enemies and campaign UI.
    public sealed class DoomMapWorld : IDisposable
    {
        public readonly DoomMapData Data;
        public readonly GameObject Root;
        public readonly float Scale;
        public readonly List<string> Warnings = new List<string>();
        public bool Completed { get; private set; }
        public int Keys { get; private set; }
        public string Status { get; private set; }
        public readonly int MinimumFloor, MaximumCeiling;
        public Vector3 PlayerStartWorld { get { return MapToWorld(Data.PlayerStart.X, Data.PlayerStart.Y) + Root.transform.up * .15f; } }
        public Vector3 PlayerForward { get { float a = Data.PlayerStart.Angle * Mathf.Deg2Rad; return Root.transform.right * Mathf.Cos(a) + Root.transform.forward * Mathf.Sin(a); } }
        public Vector3 EntryPadWorld { get { return Root.transform.TransformPoint(new Vector3(0, -.5f, -18)); } }
        public Vector3 Min { get { return Local(Data.MinX, Data.MinY, MinimumFloor); } }
        public Vector3 Max { get { return Local(Data.MaxX, Data.MaxY, MaximumCeiling); } }
        private readonly CelestialBody body;
        private readonly BodyPoint anchor;
        private readonly Vector3 bodyForward;
        private readonly double startFloor;
        private readonly MapTextures textures;
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly Dictionary<int, Motion> motions = new Dictionary<int, Motion>();
        private readonly List<KeyMarker> markers = new List<KeyMarker>();
        private Vector3 previous;
        private bool hadPrevious;
        private sealed class Motion
        {
            public int Sector; public bool Door, Repeat; public float Current, Target, Lowest, Highest, Wait;
            public GameObject Object;
        }
        private sealed class KeyMarker { public int Bit; public GameObject Object; public float X, Y, Height; }
        public DoomMapWorld(string wadPath, string mapName, CelestialBody body, Vector3 origin, Vector3 forward, float unitsPerDoomUnit = .04f)
        {
            if (body == null || unitsPerDoomUnit < .015f || unitsPerDoomUnit > .2f) throw new ArgumentException("Invalid map anchor/scale");
            this.body = body; Scale = unitsPerDoomUnit;
            var archive = new DoomMapArchive(wadPath); Data = archive.Load(mapName);
            Warnings.AddRange(Data.Warnings);
            textures = new MapTextures(new WadArchive(archive.Bytes), Warnings);
            startFloor = Data.MapFloorAt(Data.PlayerStart.X, Data.PlayerStart.Y);
            int min = int.MaxValue, max = int.MinValue;
            foreach (var s in Data.Sectors) { min = Math.Min(min, s.Floor); max = Math.Max(max, s.Ceiling); }
            MinimumFloor = min; MaximumCeiling = max;
            anchor = new BodyPoint(body, origin); bodyForward = Quaternion.Inverse(body.transform.rotation) * forward;
            Root = new GameObject("KerbalDoom/Map/" + mapName); UpdateAnchor();
            try { DiscoverMotions(); BuildSurfaces(); BuildWalls(); BuildMotions(); BuildKeys(); BuildEntry(); }
            catch { Dispose(); throw; }
            Status = mapName + ": E — двери/кнопки, ключи подбираются при подходе";
            var unsupported = new SortedSet<int>();
            foreach (var line in Data.Lines) if (line.Special != 0 && !Supported(line.Special)) unsupported.Add(line.Special);
            if (unsupported.Count != 0) Warnings.Add("Частичная поддержка специальных линий: " + string.Join(", ", new List<int>(unsupported).ConvertAll(n => n.ToString()).ToArray()));
        }
        private Vector3 Local(double x, double y, double h)
        { return new Vector3((float)(x - Data.PlayerStart.X) * Scale, (float)(h - startFloor) * Scale, (float)(y - Data.PlayerStart.Y) * Scale); }
        public Vector3 MapToWorld(double x, double y, double height) { return Root.transform.TransformPoint(Local(x, y, height)); }
        public Vector3 MapToWorld(double x, double y) { return MapToWorld(x, y, MapFloorAt(x, y)); }
        public double MapFloorAt(double x, double y)
        {
            int sector = Data.SectorAt(x, y); Motion m;
            return Data.Sectors[sector].Floor + (motions.TryGetValue(sector, out m) && !m.Door ? m.Current : 0);
        }
        private void UpdateAnchor()
        {
            if (Root == null) return;
            Vector3 origin = anchor.World, up = ((Vector3d)origin - body.position).normalized;
            Vector3 forward = Vector3.ProjectOnPlane(body.transform.rotation * bodyForward, up).normalized;
            if (forward.sqrMagnitude < .001f) forward = Vector3.Cross(up, Vector3.right).normalized;
            Root.transform.position = origin; Root.transform.rotation = Quaternion.LookRotation(forward, up);
        }
        private IEnumerable<int> Neighbours(int sector)
        {
            var found = new HashSet<int>();
            foreach (var line in Data.Lines)
            {
                if (line.Right == 65535 || line.Left == 65535) continue;
                int a = Data.Sides[line.Right].Sector, b = Data.Sides[line.Left].Sector;
                if (a == sector && b != sector) found.Add(b); if (b == sector && a != sector) found.Add(a);
            }
            return found;
        }
        private static bool ManualDoor(int t) { return t == 1 || t == 26 || t == 27 || t == 28 || t == 31 || t == 32 || t == 33 || t == 34 || t == 117 || t == 118; }
        private static bool TaggedDoor(int t) { return t == 2 || t == 4 || t == 29 || t == 46 || t == 61 || t == 63 || t == 90 || t == 103 || t == 105 || t == 106 || t == 108 || t == 109 || t == 111 || t == 112 || t == 114 || t == 115 || t == 133 || t == 134 || t == 135 || t == 136 || t == 137 || t == 99; }
        private static bool Lift(int t) { return t == 10 || t == 21 || t == 62 || t == 88; }
        private static bool LowerFloor(int t) { return t == 19 || t == 23 || t == 36 || t == 37 || t == 38 || t == 60 || t == 70 || t == 71 || t == 82 || t == 83 || t == 84 || t == 98 || t == 102; }
        private static bool IsExit(int t) { return t == 11 || t == 51 || t == 52 || t == 124 || t == 197 || t == 198; }
        private static bool Supported(int t) { return ManualDoor(t) || TaggedDoor(t) || Lift(t) || LowerFloor(t) || IsExit(t); }
        private IEnumerable<int> Targets(DoomMapData.Line line)
        {
            if (ManualDoor(line.Special))
            {
                int side = line.Left != 65535 ? line.Left : line.Right;
                if (side != 65535) yield return Data.Sides[side].Sector;
            }
            else if (line.Tag != 0) for (int i = 0; i < Data.Sectors.Length; i++) if (Data.Sectors[i].Tag == line.Tag) yield return i;
        }
        private void DiscoverMotions()
        {
            foreach (var line in Data.Lines)
            {
                bool door = ManualDoor(line.Special) || TaggedDoor(line.Special);
                if (!door && !Lift(line.Special) && !LowerFloor(line.Special)) continue;
                foreach (int sector in Targets(line))
                {
                    if (motions.ContainsKey(sector)) continue;
                    var d = Data.Sectors[sector]; int lowest = d.Floor, top = d.Ceiling;
                    if (door)
                    {
                        top = int.MaxValue;
                        foreach (int other in Neighbours(sector)) top = Math.Min(top, Data.Sectors[other].Ceiling - 4);
                        if (top == int.MaxValue) top = d.Floor + 128; top = Math.Max(top, d.Ceiling);
                    }
                    else foreach (int other in Neighbours(sector)) lowest = Math.Min(lowest, Data.Sectors[other].Floor);
                    motions[sector] = new Motion { Sector = sector, Door = door, Repeat = Lift(line.Special), Lowest = door ? 0 : lowest - d.Floor,
                        Highest = door ? top - d.Floor : 0, Current = door ? d.Ceiling - d.Floor : 0, Target = door ? d.Ceiling - d.Floor : 0 };
                }
            }
        }
        private int FloorBase(int s) { Motion m; return Data.Sectors[s].Floor + (motions.TryGetValue(s, out m) && !m.Door ? (int)m.Lowest : 0); }
        private int CeilingBase(int s) { Motion m; return motions.TryGetValue(s, out m) && m.Door ? Data.Sectors[s].Floor + (int)m.Highest : Data.Sectors[s].Ceiling; }
        private GameObject MeshObject(string name, List<Vector3> v, List<int> t, List<Vector2> uv, Material material, Transform parent, bool convex, bool solid = true)
        {
            if (t.Count == 0) return null;
            Mesh mesh = new Mesh(); mesh.name = name; mesh.vertices = v.ToArray(); mesh.triangles = t.ToArray(); mesh.uv = uv.ToArray(); mesh.RecalculateNormals(); mesh.RecalculateBounds(); meshes.Add(mesh);
            GameObject obj = new GameObject(name); obj.layer = 0; obj.transform.SetParent(parent, false);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = obj.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (solid)
            {
                GameObject contact = new GameObject("NativeGroundContact"); contact.layer = 19; contact.transform.SetParent(obj.transform, false);
                MeshCollider collider = contact.AddComponent<MeshCollider>(); collider.sharedMesh = mesh; collider.convex = convex;
            }
            return obj;
        }
        private void AddPlane(List<Vector3> v, List<int> tris, List<Vector2> uv, DoomMapData.Cell cell, int height, bool ceiling)
        {
            int start = v.Count;
            foreach (var p in cell.Polygon) { v.Add(Local(p.X, p.Y, height)); uv.Add(new Vector2((float)p.X / 64, (float)p.Y / 64)); }
            for (int i = 1; i < cell.Polygon.Count - 1; i++) { tris.Add(start); tris.Add(start + (ceiling ? i + 1 : i)); tris.Add(start + (ceiling ? i : i + 1)); }
        }
        private void BuildSurfaces()
        {
            for (int i = 0; i < Data.Sectors.Length; i++)
            {
                var s = Data.Sectors[i]; Motion m; bool lift = motions.TryGetValue(i, out m) && !m.Door;
                foreach (bool ceiling in new [] {false, true})
                {
                    if (!ceiling && lift || ceiling && s.CeilingTexture == "F_SKY1") continue;
                    var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
                    foreach (var cell in Data.Cells) if (cell.Sector == i) AddPlane(v, t, uv, cell, ceiling ? CeilingBase(i) : s.Floor, ceiling);
                    MeshObject((ceiling ? "Ceiling/" : "Floor/") + i, v, t, uv, textures.Material(ceiling ? s.CeilingTexture : s.FloorTexture, true, s.Light), Root.transform, false);
                }
            }
        }
        private void Wall(DoomMapData.Point a, DoomMapData.Point b, int low, int high, string texture, DoomMapData.Side side, string name, bool solid = true)
        {
            if (high <= low || string.IsNullOrEmpty(texture) || texture == "-") return;
            Texture2D image = textures.Texture(texture, false); float w = image.width, h = image.height;
            float length = (float)Math.Sqrt((b.X-a.X)*(b.X-a.X)+(b.Y-a.Y)*(b.Y-a.Y));
            var v = new List<Vector3> {Local(a.X,a.Y,low),Local(b.X,b.Y,low),Local(b.X,b.Y,high),Local(a.X,a.Y,high)};
            float x = side.XOffset/w, y = -side.YOffset/h;
            var uv = new List<Vector2> {new Vector2(x,y-(high-low)/h),new Vector2(x+length/w,y-(high-low)/h),new Vector2(x+length/w,y),new Vector2(x,y)};
            MeshObject(name,v,new List<int>{0,2,1,0,3,2},uv,textures.Material(texture,false,Data.Sectors[side.Sector].Light),Root.transform,false,solid);
        }
        private void BuildWalls()
        {
            for (int i=0;i<Data.Lines.Length;i++)
            {
                var line=Data.Lines[i];
                foreach(bool reverse in new[]{false,true})
                {
                    int index=reverse?line.Left:line.Right, otherIndex=reverse?line.Right:line.Left;
                    if(index==65535)continue;
                    var side=Data.Sides[index];int floor=FloorBase(side.Sector),ceiling=CeilingBase(side.Sector);
                    var a=Data.Vertices[reverse?line.B:line.A];var b=Data.Vertices[reverse?line.A:line.B];
                    if(otherIndex==65535)Wall(a,b,floor,ceiling,side.Middle,side,"Wall/"+i);
                    else
                    {
                        int other=Data.Sides[otherIndex].Sector,of=FloorBase(other),oc=CeilingBase(other);
                        if(of>floor)Wall(a,b,floor,Math.Min(of,ceiling),side.Lower,side,"Lower/"+i);
                        if(oc<ceiling&&!(Data.Sectors[other].CeilingTexture=="F_SKY1"&&Data.Sectors[side.Sector].CeilingTexture=="F_SKY1"))Wall(a,b,Math.Max(floor,oc),ceiling,side.Upper,side,"Upper/"+i);
                        if(side.Middle!="-")Wall(a,b,Math.Max(floor,of),Math.Min(ceiling,oc),side.Middle,side,"Masked/"+i,(line.Flags&1)!=0);
                    }
                }
            }
        }
        private string MotionTexture(int sector,bool door)
        {
            foreach(var line in Data.Lines)
            {
                if(line.Right==65535||line.Left==65535)continue;
                int a=Data.Sides[line.Right].Sector,b=Data.Sides[line.Left].Sector;if(a!=sector&&b!=sector)continue;
                var side=Data.Sides[a==sector?line.Left:line.Right];string name=door?side.Upper:side.Lower;
                if(name!="-"&&name.Length>0)return name;
            }
            return door?"BIGDOOR2":"SUPPORT2";
        }
        private void BuildMotions()
        {
            foreach(Motion m in motions.Values)
            {
                var d=Data.Sectors[m.Sector];m.Object=new GameObject((m.Door?"Door/":"Lift/")+m.Sector);m.Object.transform.SetParent(Root.transform,false);
                Rigidbody rb=m.Object.AddComponent<Rigidbody>();rb.isKinematic=true;rb.useGravity=false;
                foreach(var cell in Data.Cells)
                {
                    if(cell.Sector!=m.Sector)continue;
                    var v=new List<Vector3>();var t=new List<int>();var uv=new List<Vector2>();
                    int low=m.Door?d.Floor:d.Floor+(int)m.Lowest-8,high=m.Door?d.Floor+(int)m.Highest:d.Floor;
                    AddPlane(v,t,uv,cell,high,false);AddPlane(v,t,uv,cell,low,true);
                    for(int i=0;i<cell.Polygon.Count;i++)
                    {
                        var a=cell.Polygon[i];var b=cell.Polygon[(i+1)%cell.Polygon.Count];int n=v.Count;
                        v.Add(Local(a.X,a.Y,low));v.Add(Local(b.X,b.Y,low));v.Add(Local(b.X,b.Y,high));v.Add(Local(a.X,a.Y,high));
                        uv.Add(new Vector2(0,0));uv.Add(new Vector2(1,0));uv.Add(new Vector2(1,1));uv.Add(new Vector2(0,1));
                        t.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
                    }
                    MeshObject("MoverCell/"+cell.Subsector,v,t,uv,textures.Material(MotionTexture(m.Sector,m.Door),false,d.Light),m.Object.transform,true);
                    if (!m.Door)
                    {
                        var top = new List<Vector3>(); var topTris = new List<int>(); var topUv = new List<Vector2>();
                        AddPlane(top,topTris,topUv,cell,d.Floor,false);
                        for(int j=0;j<top.Count;j++)top[j]+=Vector3.up*.002f;
                        MeshObject("LiftFlat/"+cell.Subsector,top,topTris,topUv,textures.Material(d.FloorTexture,true,d.Light),m.Object.transform,false,false);
                    }
                }
                m.Object.transform.localPosition=Vector3.up*m.Current*Scale;
            }
        }
        private void BuildKeys()
        {
            foreach(var thing in Data.Things)
            {
                int bit=thing.Type==5||thing.Type==40?1:thing.Type==6||thing.Type==39?2:thing.Type==13||thing.Type==38?4:0;
                if(bit==0||(thing.Flags&16)!=0)continue;
                var obj=new GameObject("MapKey/"+bit);obj.transform.SetParent(Root.transform,false);
                var renderer=obj.AddComponent<SpriteRenderer>();renderer.sprite=textures.Sprite(bit==1?"BKEYA0":bit==2?"YKEYA0":"RKEYA0",Scale);renderer.sharedMaterial=textures.SpriteMaterial;
                markers.Add(new KeyMarker{Bit=bit,Object=obj,X=thing.X,Y=thing.Y,Height=(float)MapFloorAt(thing.X,thing.Y)});
            }
        }
        private void BuildEntry()
        {
            GameObject pad=GameObject.CreatePrimitive(PrimitiveType.Cube);pad.name="KerbalDoom/MapLandingPad";pad.layer=0;pad.transform.SetParent(Root.transform,false);
            Collider original = pad.GetComponent<Collider>(); original.enabled = false; UnityEngine.Object.Destroy(original);
            GameObject contact = new GameObject("NativeGroundContact"); contact.layer = 19; contact.transform.SetParent(pad.transform,false); contact.AddComponent<BoxCollider>();
            pad.transform.localPosition=new Vector3(0,-1,-18);pad.transform.localScale=new Vector3(20,1,20);
            pad.GetComponent<Renderer>().sharedMaterial=textures.Material("FLAT23",true,200);
        }
        public void Tick(float dt,Vector3 playerWorld,bool playerPresent,bool usePressed)
        {
            if(Root==null)return;UpdateAnchor();
            foreach(Motion m in motions.Values)
            {
                if(m.Wait>0){m.Wait-=dt;if(m.Wait<=0)m.Target=0;}
                float old=m.Current;m.Current=Mathf.MoveTowards(m.Current,m.Target,dt*(m.Door?70:40));
                if(!m.Door&&m.Repeat&&m.Current==m.Lowest&&old!=m.Current)m.Wait=5;
                m.Object.transform.localPosition=Vector3.up*m.Current*Scale;
            }
            Vector3 local=Root.transform.InverseTransformPoint(playerWorld);double x=local.x/Scale+Data.PlayerStart.X,y=local.z/Scale+Data.PlayerStart.Y;
            Camera camera=FlightCamera.fetch==null?Camera.main:FlightCamera.fetch.mainCamera;
            foreach(var marker in markers)
            {
                if(marker.Object==null)continue;bool collected=(Keys&marker.Bit)!=0;marker.Object.SetActive(!collected);if(collected)continue;
                marker.Object.transform.localPosition=Local(marker.X,marker.Y,marker.Height+6);
                if(camera!=null)marker.Object.transform.rotation=Quaternion.LookRotation(marker.Object.transform.position-camera.transform.position,Root.transform.up);
                if(playerPresent&&(MapToWorld(marker.X,marker.Y,marker.Height+6)-playerWorld).sqrMagnitude<2.56f)
                {Keys|=marker.Bit;marker.Object.SetActive(false);Status=marker.Bit==1?"Подобран синий ключ":marker.Bit==2?"Подобран жёлтый ключ":"Подобран красный ключ";}
            }
            if(!playerPresent||!Data.Contains(x,y)){hadPrevious=false;return;}
            var point=new DoomMapData.Point(x,y);
            if(usePressed)
            {
                double best=3/Scale;int chosen=-1;
                for(int i=0;i<Data.Lines.Length;i++){var l=Data.Lines[i];if(l.Special==0||!Supported(l.Special)||DoomMapData.Cross(Data.Vertices[l.A],Data.Vertices[l.B],point)>.001)continue;double distance=Distance(point,Data.Vertices[l.A],Data.Vertices[l.B]);if(distance<best){best=distance;chosen=i;}}
                if(chosen>=0)Activate(chosen);else Status="Подойди ближе к двери или кнопке и нажми E";
            }
            if(hadPrevious&&(local-previous).sqrMagnitude<36)
            {
                var before=new DoomMapData.Point(previous.x/Scale+Data.PlayerStart.X,previous.z/Scale+Data.PlayerStart.Y);
                for(int i=0;i<Data.Lines.Length;i++)
                {
                    var l=Data.Lines[i];int s=l.Special;
                    if(!(s==2||s==4||s==10||s==19||s==37||s==38||s==52||s==82||s==83||s==84||s==88||s==90||s==98||s==105||s==106||s==108||s==109||s==124))continue;
                    if(Crossed(before,point,Data.Vertices[l.A],Data.Vertices[l.B]))Activate(i);
                }
            }
            previous=local;hadPrevious=true;
        }
        private void Activate(int index)
        {
            var line=Data.Lines[index];int type=line.Special;
            if(IsExit(type)){Completed=true;Status=Data.Name+": выход достигнут! Аппарат KSP сохранён в мире.";return;}
            int key=type==26||type==32||type==99||type==133?1:type==27||type==34||type==136||type==137?2:type==28||type==33||type==134||type==135?4:0;
            if(key!=0&&(Keys&key)==0){Status=key==1?"Нужен синий ключ":key==2?"Нужен жёлтый ключ":"Нужен красный ключ";return;}
            bool moved=false;
            foreach(int sector in Targets(line)){Motion m;if(!motions.TryGetValue(sector,out m))continue;m.Wait=0;m.Target=m.Door?m.Highest:m.Repeat&&Mathf.Abs(m.Current-m.Lowest)<.01f?0:m.Lowest;moved=true;}
            Status=moved?(ManualDoor(type)||TaggedDoor(type)?"Дверь открывается":"Платформа движется"):"Этот механизм пока не поддерживается";
        }
        private static double Distance(DoomMapData.Point p,DoomMapData.Point a,DoomMapData.Point b)
        {
            double dx=b.X-a.X,dy=b.Y-a.Y,length=dx*dx+dy*dy,t=length==0?0:Math.Max(0,Math.Min(1,((p.X-a.X)*dx+(p.Y-a.Y)*dy)/length));
            dx=p.X-a.X-t*dx;dy=p.Y-a.Y-t*dy;return Math.Sqrt(dx*dx+dy*dy);
        }
        private static bool Crossed(DoomMapData.Point p,DoomMapData.Point q,DoomMapData.Point a,DoomMapData.Point b)
        {return DoomMapData.Cross(a,b,p)*DoomMapData.Cross(a,b,q)<0&&DoomMapData.Cross(p,q,a)*DoomMapData.Cross(p,q,b)<=0;}
        public void SetKeys(int keys){Keys=keys&7;}
        public string CaptureState()
        {
            var records=new List<string>();
            foreach(var m in motions.Values)records.Add(m.Sector+":"+m.Current.ToString("R",CultureInfo.InvariantCulture)+":"+m.Target.ToString("R",CultureInfo.InvariantCulture)+":"+m.Wait.ToString("R",CultureInfo.InvariantCulture));
            return "v1|"+Keys+"|"+(Completed?"1":"0")+"|"+string.Join(";",records.ToArray());
        }
        public void RestoreState(string state)
        {
            if(string.IsNullOrEmpty(state))return;string[] p=state.Split('|');if(p.Length!=4||p[0]!="v1")return;
            int keys;if(int.TryParse(p[1],out keys))SetKeys(keys);Completed=p[2]=="1";
            foreach(string record in p[3].Split(';'))
            {
                string[] v=record.Split(':');int sector;Motion m;float current,target,wait;
                if(v.Length!=4||!int.TryParse(v[0],out sector)||!motions.TryGetValue(sector,out m))continue;
                if(!float.TryParse(v[1],NumberStyles.Float,CultureInfo.InvariantCulture,out current)||!float.TryParse(v[2],NumberStyles.Float,CultureInfo.InvariantCulture,out target)||!float.TryParse(v[3],NumberStyles.Float,CultureInfo.InvariantCulture,out wait))continue;
                if(float.IsNaN(current)||float.IsInfinity(current)||float.IsNaN(target)||float.IsInfinity(target)||float.IsNaN(wait)||float.IsInfinity(wait))continue;
                m.Current=Mathf.Clamp(current,m.Lowest,m.Highest);m.Target=Mathf.Clamp(target,m.Lowest,m.Highest);m.Wait=Mathf.Clamp(wait,0,5);m.Object.transform.localPosition=Vector3.up*m.Current*Scale;
            }
        }
        public void Dispose()
        {
            if(Root!=null)UnityEngine.Object.Destroy(Root);
            foreach(Mesh mesh in meshes)UnityEngine.Object.Destroy(mesh);meshes.Clear();if(textures!=null)textures.Dispose();
        }
        private sealed class MapTextures : IDisposable
        {
            private readonly WadArchive wad;
            private readonly byte[] palette;
            private readonly Dictionary<string,Texture2D> textures=new Dictionary<string,Texture2D>();
            private readonly Dictionary<string,Material> materials=new Dictionary<string,Material>();
            private readonly Dictionary<string,Sprite> sprites=new Dictionary<string,Sprite>();
            private readonly Dictionary<string,Definition> definitions=new Dictionary<string,Definition>();
            private readonly List<string> warnings;
            private readonly string[] patchNames;
            private sealed class Definition{public int Width,Height;public List<Placement>Patches=new List<Placement>();}
            private sealed class Placement{public int X,Y,Patch;}
            public readonly Material SpriteMaterial;
            public MapTextures(WadArchive wad,List<string>warnings)
            {
                this.wad=wad;this.warnings=warnings;palette=wad.Read("PLAYPAL");if(palette==null||palette.Length<768)throw new InvalidDataException("Map palette missing");
                byte[] names=wad.Read("PNAMES");if(names==null||names.Length<4)throw new InvalidDataException("PNAMES missing");
                int count=BitConverter.ToInt32(names,0);if(count<0||count>100000||4L+count*8L>names.Length)throw new InvalidDataException("Invalid PNAMES");
                patchNames=new string[count];for(int i=0;i<count;i++)patchNames[i]=Encoding.ASCII.GetString(names,4+i*8,8).TrimEnd('\0');
                foreach(string name in new[]{"TEXTURE1","TEXTURE2"})ReadDefinitions(wad.Read(name));
                SpriteMaterial=new Material(Shader.Find("Sprites/Default"));
            }
            private void ReadDefinitions(byte[] raw)
            {
                if(raw==null)return;if(raw.Length<4)throw new InvalidDataException("Invalid texture table");
                int count=BitConverter.ToInt32(raw,0);if(count<0||count>10000||4L+count*4L>raw.Length)throw new InvalidDataException("Invalid texture directory");
                for(int i=0;i<count;i++)
                {
                    int p=BitConverter.ToInt32(raw,4+i*4);if(p<0||p+22L>raw.Length)throw new InvalidDataException("Invalid texture offset");
                    string name=Encoding.ASCII.GetString(raw,p,8).TrimEnd('\0');int patches=BitConverter.ToUInt16(raw,p+20);
                    var d=new Definition{Width=BitConverter.ToUInt16(raw,p+12),Height=BitConverter.ToUInt16(raw,p+14)};
                    if(d.Width<1||d.Height<1||d.Width>2048||d.Height>2048||patches>4096||p+22L+patches*10L>raw.Length)throw new InvalidDataException("Invalid texture dimensions");
                    for(int j=0;j<patches;j++){int q=p+22+j*10,patch=BitConverter.ToUInt16(raw,q+4);if(patch>=patchNames.Length)throw new InvalidDataException("Invalid texture patch index");d.Patches.Add(new Placement{X=BitConverter.ToInt16(raw,q),Y=BitConverter.ToInt16(raw,q+2),Patch=patch});}
                    definitions[name]=d;
                }
            }
            public Texture2D Texture(string name,bool flat)
            {
                string key=(flat?"F/":"W/")+name;Texture2D texture;if(textures.TryGetValue(key,out texture))return texture;
                int width=64,height=64;byte[] rgba;Definition d;
                if(!flat&&definitions.TryGetValue(name,out d))
                {
                    width=d.Width;height=d.Height;rgba=new byte[width*height*4];
                    foreach(Placement place in d.Patches)
                    {
                        WadArchive.Patch patch=wad.DecodePatch(patchNames[place.Patch]);if(patch==null)continue;
                        for(int y=0;y<patch.Height;y++)for(int x=0;x<patch.Width;x++)
                        {int xx=x+place.X,yy=y+place.Y,src=(y*patch.Width+x)*4;if(xx<0||yy<0||xx>=width||yy>=height||patch.Pixels[src+3]==0)continue;Buffer.BlockCopy(patch.Pixels,src,rgba,(yy*width+xx)*4,4);}
                    }
                }
                else
                {
                    byte[] indices=flat?wad.Read(name):null;
                    if(indices!=null&&indices.Length==4096)
                    {
                        rgba=new byte[64*64*4];for(int i=0;i<indices.Length;i++){int c=indices[i]*3;rgba[i*4]=palette[c];rgba[i*4+1]=palette[c+1];rgba[i*4+2]=palette[c+2];rgba[i*4+3]=255;}
                    }
                    else
                    {
                        WadArchive.Patch patch=null;try{patch=wad.DecodePatch(name);}catch(InvalidDataException){}
                        if(patch!=null){width=patch.Width;height=patch.Height;rgba=patch.Pixels;}
                        else
                        {
                            if(!warnings.Contains("Нет текстуры: "+name))warnings.Add("Нет текстуры: "+name);rgba=new byte[width*height*4];
                            for(int i=0;i<width*height;i++){bool dark=(i%width/8+i/width/8)%2==0;rgba[i*4]=dark?(byte)70:(byte)100;rgba[i*4+1]=dark?(byte)60:(byte)85;rgba[i*4+2]=dark?(byte)45:(byte)70;rgba[i*4+3]=255;}
                        }
                    }
                }
                Color32[] pixels=new Color32[width*height];for(int y=0;y<height;y++)for(int x=0;x<width;x++){int p=(y*width+x)*4;pixels[(height-1-y)*width+x]=new Color32(rgba[p],rgba[p+1],rgba[p+2],rgba[p+3]);}
                texture=new Texture2D(width,height,TextureFormat.RGBA32,false);texture.name="MapTexture/"+name;texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Repeat;texture.SetPixels32(pixels);texture.Apply(false,true);textures[key]=texture;return texture;
            }
            public Material Material(string name,bool flat,int light)
            {
                string key=(flat?"F/":"W/")+name+"/"+light;Material m;if(materials.TryGetValue(key,out m))return m;
                Shader shader=Shader.Find("Unlit/Transparent Cutout")??Shader.Find("Unlit/Texture")??Shader.Find("Sprites/Default");
                m=new Material(shader);m.mainTexture=Texture(name,flat);if(m.HasProperty("_Color")){float brightness=Mathf.Lerp(.35f,1,Mathf.Clamp01(light/255f));m.color=new Color(brightness,brightness,brightness,1);}materials[key]=m;return m;
            }
            public Sprite Sprite(string name,float scale)
            {
                Sprite sprite;if(sprites.TryGetValue(name,out sprite))return sprite;Texture2D texture=Texture(name,false);
                sprite=UnityEngine.Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,0),1/scale);sprites[name]=sprite;return sprite;
            }
            public void Dispose()
            {
                foreach(var m in materials.Values)UnityEngine.Object.Destroy(m);foreach(var t in textures.Values)UnityEngine.Object.Destroy(t);foreach(var s in sprites.Values)UnityEngine.Object.Destroy(s);UnityEngine.Object.Destroy(SpriteMaterial);
                materials.Clear();textures.Clear();sprites.Clear();
            }
        }
    }
}
