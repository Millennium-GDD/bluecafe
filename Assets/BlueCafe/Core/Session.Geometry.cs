using System;
using System.Linq;

namespace BlueCafe.Core
{
    public sealed partial class Session
    {
        void Upgrade(Layout candidate)
        {
            Geometry.Upgrade(candidate);
            foreach(var i in candidate.instances) if(Placement.Find(catalog,i.furnitureId).placement=="wall" && string.IsNullOrEmpty(i.wallId)) {
                i.legacyWallFootprint=true;i.wallId=i.quarterTurns==0?"left":"back";i.wallAlong=Geometry.Along(candidate.room,i.wallId,i.position);
            }
        }
        static Area FindArea(Layout l,string id) => l.areas?.FirstOrDefault(a=>a.id==id) ?? throw new ValidationException("UNKNOWN_AREA","Unknown area: "+id,id);
        static float Coordinate(float value,Room r) {Placement.Require(Placement.Finite(value),"INVALID_TRANSFORM","Geometry coordinate must be finite.");return Placement.Snap(value,r.grid/2);}
        public CommandResult SetAreas(Area[] areas,long? expectedRevision=null) => Execute(expectedRevision,null,c=> { Upgrade(c);Placement.Require(areas!=null,"INVALID_AREA","Areas must be an array.");c.areas=areas.Select(Copies.Copy).ToList();return Array.Empty<string>(); });
        public CommandResult TranslateArea(string id,float dx,float dz,long? expectedRevision=null) => Execute(expectedRevision,null,c=> {
            Upgrade(c);var a=FindArea(c,id);dx=Coordinate(dx,c.room);dz=Coordinate(dz,c.room);a.vertices=a.vertices.Select(v=>new Position(v.x+dx,0,v.z+dz)).ToArray();return Array.Empty<string>();
        });
        public CommandResult SetAreaVertex(string id,int index,float x,float z,long? expectedRevision=null) => Execute(expectedRevision,null,c=> {
            Upgrade(c);var a=FindArea(c,id);Placement.Require(index>=0&&index<a.vertices.Length,"INVALID_VERTEX","Unknown vertex.");a.vertices[index]=new Position(Coordinate(x,c.room),0,Coordinate(z,c.room));return Array.Empty<string>();
        });
        public CommandResult ResizeAreaCorner(string id,int index,float x,float z,long? expectedRevision=null) => Execute(expectedRevision,null,c=> {
            Upgrade(c);var a=FindArea(c,id);Placement.Require(Geometry.IsRectangle(a.vertices)&&index>=0&&index<4,"NOT_RECTANGLE","Corner resize requires an axis-aligned rectangle.");
            var old=a.vertices[index];var anchor=a.vertices[(index+2)%4];x=Coordinate(x,c.room);z=Coordinate(z,c.room);
            Placement.Require((x-anchor.x)*(old.x-anchor.x)>0&&(z-anchor.z)*(old.z-anchor.z)>0,"INVALID_RECTANGLE","Corner cannot cross its opposite anchor.");
            for(int n=0;n<4;n++) {var p=a.vertices[n];a.vertices[n]=new Position(Math.Abs(p.x-old.x)<.0001f?x:p.x,0,Math.Abs(p.z-old.z)<.0001f?z:p.z);}
            return Array.Empty<string>();
        });
        public CommandResult SetExtent(float centerX,float centerZ,float width,float depth,long? expectedRevision=null) => Execute(expectedRevision,null,c=> {
            Upgrade(c);var old=Geometry.Extent(c.room);
            float minX=Coordinate(centerX-width/2,c.room),maxX=Coordinate(centerX+width/2,c.room),minZ=Coordinate(centerZ-depth/2,c.room),maxZ=Coordinate(centerZ+depth/2,c.room);
            c.room.centerX=(minX+maxX)/2;c.room.centerZ=(minZ+maxZ)/2;c.room.width=maxX-minX;c.room.depth=maxZ-minZ;
            foreach(var i in c.instances) if(!string.IsNullOrEmpty(i.wallId)) i.wallAlong=Geometry.Along(c.room,i.wallId,i.position);
            foreach(var a in c.areas) if(Geometry.IsRectangle(a.vertices)&&a.vertices.All(p=>old.Any(q=>Math.Abs(q.x-p.x)<.0001f&&Math.Abs(q.z-p.z)<.0001f))) a.vertices=Geometry.Extent(c.room);
            return Array.Empty<string>();
        });
        public CommandResult SetWallOpenings(WallOpening[] openings,long? expectedRevision=null) => Execute(expectedRevision,null,c=> {Upgrade(c);Placement.Require(openings!=null,"INVALID_OPENING","Openings must be an array.");c.openings=openings.Select(Copies.Copy).ToList();return Array.Empty<string>();});
        void Attach(Layout c,Instance i,string wallId,float along,float elevation)
        {
            var item=Placement.Find(catalog,i.furnitureId);Placement.Require(item.placement=="wall","INVALID_ATTACHMENT","Only wall decorations can attach to walls.");
            Placement.Require(Placement.Finite(along)&&Placement.Finite(elevation),"INVALID_ATTACHMENT","Wall coordinates must be finite.");
            i.legacyWallFootprint=false;i.wallId=wallId;i.wallAlong=Placement.Snap(along,c.room.grid);i.quarterTurns=Geometry.WallTurns(wallId);
            i.position=Geometry.WallPosition(c.room,wallId,i.wallAlong,Placement.Snap(elevation,c.room.grid));
        }
        public CommandResult SetWallAttachment(string id,string wallId,float along,float elevation,long? expectedRevision=null) => Execute(expectedRevision,new[]{id},c=> {
            Upgrade(c);var i=Selection(c,new[]{id})[0];Attach(c,i,wallId,along,elevation);return new[]{id};
        });
        public CommandResult PlaceOnWall(string furnitureId,string wallId,float along,float elevation,long? expectedRevision=null) => Execute(expectedRevision,null,c=> {
            Upgrade(c);var item=Placement.Find(catalog,furnitureId);Placement.Require(item.available,"ASSET_UNAVAILABLE","Original wall decoration is unavailable.");
            var i=new Instance{instanceId=Guid.NewGuid().ToString("N"),furnitureId=furnitureId};Attach(c,i,wallId,along,elevation);c.instances.Add(i);return new[]{i.instanceId};
        });
    }
}
