using System;
using System.Collections.Generic;
using System.Linq;

namespace BlueCafe.Core
{
    // Portable v2 geometry. Named areas classify floor space; they never create walls.
    public static class Geometry
    {
        const float E = .0001f;
        public static readonly string[] WallIds = { "left", "back", "right", "front" };
        public static Position[] Rectangle(float minX, float minZ, float maxX, float maxZ) => new[] { new Position(minX,0,minZ), new Position(maxX,0,minZ), new Position(maxX,0,maxZ), new Position(minX,0,maxZ) };
        public static Position[] Extent(Room r) => Rectangle(r.centerX-r.width/2,r.centerZ-r.depth/2,r.centerX+r.width/2,r.centerZ+r.depth/2);
        public static int WallTurns(string wallId) { var n = Array.IndexOf(WallIds,wallId); Placement.Require(n >= 0,"UNKNOWN_WALL","Unknown wall: " + wallId); return n; }
        public static float WallLength(Room r,string id) => WallTurns(id)%2 == 0 ? r.depth : r.width;
        public static Position WallPosition(Room r,string id,float along,float elevation)
        {
            switch(WallTurns(id)) {
                case 0: return new Position(r.centerX-r.width/2,elevation,r.centerZ-r.depth/2+along);
                case 1: return new Position(r.centerX-r.width/2+along,elevation,r.centerZ+r.depth/2);
                case 2: return new Position(r.centerX+r.width/2,elevation,r.centerZ+r.depth/2-along);
                default: return new Position(r.centerX+r.width/2-along,elevation,r.centerZ-r.depth/2);
            }
        }
        public static float Along(Room r,string id,Position p) { switch(WallTurns(id)) { case 0:return p.z-r.centerZ+r.depth/2; case 1:return p.x-r.centerX+r.width/2; case 2:return r.centerZ+r.depth/2-p.z; default:return r.centerX+r.width/2-p.x; } }
        public static bool IsRectangle(Position[] p) => p != null && p.Length == 4 && Enumerable.Range(0,4).All(i => { var a=p[i];var b=p[(i+1)%4];return (Math.Abs(a.x-b.x)<E) != (Math.Abs(a.z-b.z)<E); });
        static float Cross(Position a,Position b,Position c) => (b.x-a.x)*(c.z-a.z)-(b.z-a.z)*(c.x-a.x);
        static bool On(Position a,Position b,Position p) => Math.Abs(Cross(a,b,p))<E && p.x>=Math.Min(a.x,b.x)-E && p.x<=Math.Max(a.x,b.x)+E && p.z>=Math.Min(a.z,b.z)-E && p.z<=Math.Max(a.z,b.z)+E;
        static bool Intersect(Position a,Position b,Position c,Position d,bool proper = false)
        {
            float ab=Cross(a,b,c),ad=Cross(a,b,d),ca=Cross(c,d,a),cb=Cross(c,d,b);
            if (((ab>E && ad< -E)||(ab< -E && ad>E)) && ((ca>E && cb< -E)||(ca< -E && cb>E))) return true;
            return !proper && (On(a,b,c)||On(a,b,d)||On(c,d,a)||On(c,d,b));
        }
        public static bool Contains(Position[] polygon,Position p,bool boundary = true)
        {
            bool inside=false;
            for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++) { var a=polygon[j];var b=polygon[i]; if(On(a,b,p)) return boundary; if((a.z>p.z)!=(b.z>p.z) && p.x<(b.x-a.x)*(p.z-a.z)/(b.z-a.z)+a.x) inside=!inside; }
            return inside;
        }
        public static void Validate(Layout l)
        {
            Placement.ValidateRoom(l.room);
            Placement.Require(Placement.Finite(l.room.centerX)&&Placement.Finite(l.room.centerZ),"INVALID_ROOM","Cafe center must be finite.");
            if(l.version == 1) { Placement.Require(l.room.centerX==0 && l.room.centerZ==0 && (l.areas==null||l.areas.Count==0) && (l.openings==null||l.openings.Count==0),"INVALID_LAYOUT","Geometry requires layout version 2.");return; }
            Placement.Require(l.version==2 && l.areas!=null && l.openings!=null,"INVALID_LAYOUT","Version 2 requires areas and openings arrays.");
            var ids=new HashSet<string>(); var extent=Extent(l.room);
            foreach(var a in l.areas) {
                Placement.Require(a!=null && !string.IsNullOrWhiteSpace(a.id)&&ids.Add(a.id)&&!string.IsNullOrWhiteSpace(a.name),"INVALID_AREA","Areas require unique ids and names.");
                var p=a.vertices; Placement.Require(p!=null&&p.Length>=3&&p.Length<=256,"INVALID_POLYGON","Area needs 3–256 vertices.");
                float signed=0;
                for(int i=0;i<p.Length;i++) {
                    var v=p[i];var next=p[(i+1)%p.Length];
                    Placement.Require(Placement.Finite(v.x)&&Placement.Finite(v.y)&&Placement.Finite(v.z)&&v.y==0,"INVALID_POLYGON","Vertices must be finite floor coordinates.");
                    Placement.Require(Contains(extent,v),"AREA_OUTSIDE_EXTENT","Area lies outside cafe: "+a.id);
                    Placement.Require(Math.Abs(v.x-next.x)+Math.Abs(v.z-next.z)>E,"INVALID_POLYGON","Duplicate adjacent vertices.");
                    var previous=p[(i+p.Length-1)%p.Length];
                    Placement.Require(Math.Abs(Cross(previous,v,next))>E || (v.x-previous.x)*(next.x-v.x)+(v.z-previous.z)*(next.z-v.z)>0,"INVALID_POLYGON","Adjacent edges cannot backtrack.");
                    signed+=v.x*next.z-next.x*v.z;
                    for(int j=i+1;j<p.Length;j++) if(j!=i+1 && !(i==0&&j==p.Length-1)) Placement.Require(!Intersect(v,next,p[j],p[(j+1)%p.Length]),"INVALID_POLYGON","Area must be a simple polygon: "+a.id);
                }
                Placement.Require(Math.Abs(signed)>E,"INVALID_POLYGON","Area needs nonzero area.");
            }
            for(int i=0;i<l.areas.Count;i++) for(int j=i+1;j<l.areas.Count;j++) {
                var a=l.areas[i].vertices;var b=l.areas[j].vertices; bool overlap=a.Any(v=>Contains(b,v,false))||b.Any(v=>Contains(a,v,false));
                for(int m=0;m<a.Length;m++) for(int n=0;n<b.Length;n++) overlap |= Intersect(a[m],a[(m+1)%a.Length],b[n],b[(n+1)%b.Length],true);
                // Coincident polygons/collinear shared edges need an interior probe.
                overlap |= InteriorProbeInside(a,b)||InteriorProbeInside(b,a);
                if(overlap) throw new ValidationException("AREA_OVERLAP","Named areas overlap.",l.areas[i].id,l.areas[j].id);
            }
            ids.Clear();
            foreach(var o in l.openings) {
                Placement.Require(o!=null&&!string.IsNullOrWhiteSpace(o.id)&&ids.Add(o.id),"INVALID_OPENING","Openings need unique ids.");
                Placement.Require(Placement.Finite(o.along)&&Placement.Finite(o.width)&&Placement.Finite(o.bottom)&&Placement.Finite(o.height)&&o.width>0&&o.height>0&&o.along-o.width/2>=-E&&o.along+o.width/2<=WallLength(l.room,o.wallId)+E&&o.bottom>=0&&o.bottom+o.height<=l.room.wallHeight+E,"INVALID_OPENING","Opening must fit its wall.");
            }
        }
        static bool InteriorProbeInside(Position[] a,Position[] b)
        {
            float signed=0;for(int i=0;i<a.Length;i++) signed+=a[i].x*a[(i+1)%a.Length].z-a[(i+1)%a.Length].x*a[i].z;
            for(int i=0;i<a.Length;i++) { var p=a[i];var q=a[(i+1)%a.Length];float dx=q.x-p.x,dz=q.z-p.z,len=(float)Math.Sqrt(dx*dx+dz*dz);float e=.0005f*Math.Sign(signed);var probe=new Position((p.x+q.x)/2-dz/len*e,0,(p.z+q.z)/2+dx/len*e);if(Contains(a,probe,false)&&Contains(b,probe,false)) return true; }
            return false;
        }
        public static int[] Triangulate(Position[] polygon)
        {
            var indices=Enumerable.Range(0,polygon.Length).ToList();float area=0;
            for(int i=0;i<polygon.Length;i++) area+=polygon[i].x*polygon[(i+1)%polygon.Length].z-polygon[(i+1)%polygon.Length].x*polygon[i].z;
            if(area<0) indices.Reverse();var triangles=new List<int>();int guard=polygon.Length*polygon.Length;
            while(indices.Count>3 && guard-->0) {
                bool cut=false;
                for(int i=0;i<indices.Count;i++) {int a=indices[(i+indices.Count-1)%indices.Count],b=indices[i],c=indices[(i+1)%indices.Count];
                    if(Cross(polygon[a],polygon[b],polygon[c])<=E) continue;
                    var tri=new[]{polygon[a],polygon[b],polygon[c]};
                    if(indices.Any(n=>n!=a&&n!=b&&n!=c&&Contains(tri,polygon[n]))) continue;
                    triangles.AddRange(new[]{a,b,c});indices.RemoveAt(i);cut=true;break;
                }
                Placement.Require(cut,"INVALID_POLYGON","Cannot triangulate area.");
            }
            Placement.Require(indices.Count==3,"INVALID_POLYGON","Cannot triangulate area.");triangles.AddRange(indices);return triangles.ToArray();
        }
        public static void Upgrade(Layout l) { if(l.version==1) {l.version=2;l.areas=l.areas??new List<Area>();l.openings=l.openings??new List<WallOpening>();} }
        public static bool Equal(Layout a,Layout b)
        {
            if(a.room.centerX!=b.room.centerX||a.room.centerZ!=b.room.centerZ) return false;
            var aa=a.areas??new List<Area>();var ba=b.areas??new List<Area>();var ao=a.openings??new List<WallOpening>();var bo=b.openings??new List<WallOpening>();
            if(aa.Count!=ba.Count||ao.Count!=bo.Count) return false;
            for(int i=0;i<aa.Count;i++) {var x=aa[i];var y=ba[i];if(x.id!=y.id||x.name!=y.name||(x.color??"")!=(y.color??"")||(x.flooringId??"")!=(y.flooringId??"")||(x.wallpaperId??"")!=(y.wallpaperId??"")||!x.vertices.SequenceEqual(y.vertices)) return false;}
            for(int i=0;i<ao.Count;i++) {var x=ao[i];var y=bo[i];if(x.id!=y.id||x.wallId!=y.wallId||x.along!=y.along||x.width!=y.width||x.bottom!=y.bottom||x.height!=y.height) return false;}
            return true;
        }
    }
}
