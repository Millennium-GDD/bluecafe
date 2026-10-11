using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BlueCafe.Core.Tests
{
    public class GeometryTests
    {
        Catalog Catalog()=>new Catalog{gridUnit=1,items=new[]{new Item{id="chair",placement="floor",width=1,depth=1,height=1,available=true},new Item{id="art",placement="wall",width=1,depth=1,height=1,available=true},new Item{id="floor",placement="flooring",available=true}}};
        Session Session()=>new Session(Catalog(),new Layout{room=new Room{width=10,depth=10,grid=1,wallHeight=5}});
        Area Rectangle(string id,float minX,float minZ,float maxX,float maxZ)=>new Area{id=id,name=id,color="#ddeeff",flooringId="floor",vertices=Geometry.Rectangle(minX,minZ,maxX,maxZ)};
        void Success(CommandResult r)=>Assert.IsTrue(r.success,r.code+": "+r.message);
        [Test] public void NamedAreaTranslateResizeUndoRetainsFurnitureIdentityAndStyle()
        {
            var s=Session();Success(s.Place("chair",1,1));var furniture=JsonUtility.ToJson(s.Layout.instances[0]);Success(s.SetAreas(new[]{Rectangle("room",-3,-3,0,0)}));
            var original=JsonUtility.ToJson(s.Layout);Success(s.TranslateArea("room",1,1));Assert.AreEqual(furniture,JsonUtility.ToJson(s.Layout.instances[0]));Assert.AreEqual("floor",s.Layout.areas[0].flooringId);Assert.AreEqual("#ddeeff",s.Layout.areas[0].color);
            var anchor=s.Layout.areas[0].vertices[2];Success(s.ResizeAreaCorner("room",0,-3,-3));Assert.AreEqual(anchor,s.Layout.areas[0].vertices[2]);Success(s.Undo());Success(s.Undo());Assert.AreEqual(original,JsonUtility.ToJson(s.Layout));Success(s.Redo());
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void EveryRectangleCornerKeepsItsOppositeAnchorAndFurniture(int corner)
        {
            var s=Session();Success(s.Place("chair",0,0));Success(s.SetAreas(new[]{Rectangle("room",-2,-2,2,2)}));var before=s.Layout;var old=before.areas[0].vertices[corner];var anchor=before.areas[0].vertices[(corner+2)%4];
            Success(s.ResizeAreaCorner("room",corner,old.x+Math.Sign(old.x),old.z+Math.Sign(old.z)));
            Assert.AreEqual(anchor,s.Layout.areas[0].vertices[(corner+2)%4]);Assert.AreEqual(before.instances[0].position,s.Layout.instances[0].position);Assert.AreEqual(before.instances[0].instanceId,s.Layout.instances[0].instanceId);
            Success(s.Undo());Assert.IsTrue(Copies.Equal(before,s.Layout));Success(s.Redo());Assert.IsTrue(Geometry.IsRectangle(s.Layout.areas[0].vertices));
        }
        [Test] public void ConcaveAndDiagonalAreasRejectOverlapAndCrossingAtomically()
        {
            var s=Session();var l=new Area{id="L",name="Reading",vertices=new[]{new Position(-4,0,-4),new Position(0,0,-4),new Position(0,0,-2),new Position(-2,0,-2),new Position(-2,0,0),new Position(-4,0,0)}};
            var diagonal=new Area{id="diagonal",name="Counter",vertices=new[]{new Position(1,0,1),new Position(4,0,1),new Position(4,0,4)}};
            Success(s.SetAreas(new[]{l,diagonal}));Assert.AreEqual(12,Geometry.Triangulate(l.vertices).Length);var before=JsonUtility.ToJson(s.Layout);var revision=s.Revision;
            Assert.IsFalse(s.TranslateArea("diagonal",-4,-4).success);Assert.AreEqual(before,JsonUtility.ToJson(s.Layout));Assert.AreEqual(revision,s.Revision);
            Assert.IsFalse(s.SetAreaVertex("L",1,-4,0).success);Assert.AreEqual(before,JsonUtility.ToJson(s.Layout));
            var preview=new Session(s.Catalog,s.Layout);Success(preview.TranslateArea("diagonal",-.5f,0));Assert.AreEqual(before,JsonUtility.ToJson(s.Layout),"Cancelling detached preview must leave live history unchanged");
        }
        [Test] public void SharedEdgesAreAllowedButCoincidentAndCrossingPolygonsAreNot()
        {
            var s=Session();Success(s.SetAreas(new[]{Rectangle("a",-4,-4,0,0),Rectangle("b",0,-4,4,0)}));
            Assert.AreEqual("AREA_OVERLAP",s.SetAreas(new[]{Rectangle("a",-4,-4,0,0),Rectangle("b",-4,-4,0,0)}).code);
            Assert.IsFalse(s.SetAreas(new[]{new Area{id="bad",name="bad",vertices=new[]{new Position(-2,0,-2),new Position(2,0,2),new Position(-2,0,2),new Position(2,0,-2)}}}).success);
        }
        [Test] public void OffsetExtentFitsFootprintExactlyAndProtectsAttachedDecorations()
        {
            var s=Session();Success(s.Place("chair",1,1));Success(s.SetAreas(new[]{Rectangle("cafe",-5,-5,5,5)}));var furniture=JsonUtility.ToJson(s.Layout.instances[0]);Success(s.SetExtent(1,1,1,1));
            Assert.AreEqual(.5f,s.Layout.areas[0].vertices[0].x);Assert.AreEqual(furniture,JsonUtility.ToJson(s.Layout.instances[0]));Success(s.Undo());Success(s.PlaceOnWall("art","left",2,2));
            var before=JsonUtility.ToJson(s.Layout);var revision=s.Revision;var result=s.SetExtent(1,0,10,10);Assert.IsFalse(result.success);Assert.AreEqual("ATTACHMENT_CONFLICT",result.code);Assert.AreEqual(s.Layout.instances.Last().instanceId,result.affectedIds.Single());Assert.AreEqual(before,JsonUtility.ToJson(s.Layout));Assert.AreEqual(revision,s.Revision);
        }
        [TestCase("left")] [TestCase("back")] [TestCase("right")] [TestCase("front")]
        public void WallAttachmentHeightIdentityOpeningAndDuplicate(string wall)
        {
            var s=Session();var placed=s.PlaceOnWall("art",wall,2,2);Success(placed);var id=placed.instanceIds.Single();Success(s.SetWallAttachment(id,wall,3,3));
            var instance=s.Layout.instances.Single();Assert.AreEqual(3,instance.position.y);Assert.AreEqual(wall,instance.wallId);Assert.AreEqual(Geometry.WallPosition(s.Layout.room,wall,3,3),instance.position);
            Success(s.Duplicate(new[]{id}));Assert.AreNotEqual(id,s.Layout.instances[1].instanceId);Assert.AreEqual(wall,s.Layout.instances[1].wallId);Assert.AreEqual(3,s.Layout.instances[1].wallAlong);
            var before=JsonUtility.ToJson(s.Layout);Assert.AreEqual("OPENING_CONFLICT",s.SetWallOpenings(new[]{new WallOpening{id="door",wallId=wall,along=3,width=2,bottom=1,height=3}}).code);Assert.AreEqual(before,JsonUtility.ToJson(s.Layout));
            Assert.IsFalse(s.SetWallAttachment(id,wall,0,3).success);Assert.IsFalse(s.SetWallAttachment(id,wall,3,float.NaN).success);Assert.AreEqual(before,JsonUtility.ToJson(s.Layout));
            Success(s.Undo());Success(s.Undo());Assert.AreEqual(2,s.Layout.instances.Single().position.y);
        }
        [Test] public void MigratedLegacyWallRetainsDeclaredFootprintUntilExplicitlyAdjusted()
        {
            var catalog=Catalog();catalog.items[1].bounds=new BoundsData{min=new[]{0f,-3f,-.5f},max=new[]{.1f,3f,.5f}};
            var s=new Session(catalog,new Layout{room=new Room{width=10,depth=10,grid=1,wallHeight=5}});Success(s.Place("art",-5,0,0,2));
            var original=s.Layout.instances.Single();Success(s.SetAreas(new[]{Rectangle("room",-4,-4,4,4)}));Assert.IsTrue(s.Layout.instances.Single().legacyWallFootprint);Assert.AreEqual(original.position,s.Layout.instances.Single().position);
            var id=original.instanceId;var before=JsonUtility.ToJson(s.Layout);Assert.AreEqual("INVALID_ELEVATION",s.SetWallAttachment(id,"left",5,2).code);Assert.AreEqual(before,JsonUtility.ToJson(s.Layout));
        }
        [Test] public void LegacyUpgradeRetainsStableInstanceIdsTransformsAndWallDefaults()
        {
            var s=Session();Success(s.Place("chair",0,0));Success(s.Place("art",-5,0,0,2));var legacy=s.Layout;Success(s.SetAreas(new[]{Rectangle("room",-4,-4,4,4)}));
            Assert.AreEqual(2,s.Layout.version);for(int i=0;i<legacy.instances.Count;i++){Assert.AreEqual(legacy.instances[i].instanceId,s.Layout.instances[i].instanceId);Assert.AreEqual(legacy.instances[i].position,s.Layout.instances[i].position);Assert.AreEqual(legacy.instances[i].quarterTurns,s.Layout.instances[i].quarterTurns);}Assert.AreEqual("left",s.Layout.instances.Last().wallId);
        }
    }
}
