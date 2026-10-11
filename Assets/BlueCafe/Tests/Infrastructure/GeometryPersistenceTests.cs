using BlueCafe.Core;
using NUnit.Framework;

namespace BlueCafe.Infrastructure.Tests
{
    public class GeometryPersistenceTests
    {
        [Test] public void VersionTwoRoundTripsOffsetPolygonStylesOpeningsAndWallTransformsExactly()
        {
            var l=new Layout{version=2,room=new Room{centerX=2,centerZ=1,width=10,depth=10}};
            l.areas.Add(new Area{id="reading",name="Reading",color="#123456",flooringId="floor",wallpaperId="paper",vertices=new[]{new Position(0,0,0),new Position(3,0,0),new Position(3,0,3)}});
            l.openings.Add(new WallOpening{id="door",wallId="back",along=2,width=1,bottom=0,height=2});
            l.instances.Add(new Instance{instanceId="i",furnitureId="art",wallId="left",wallAlong=3,position=Geometry.WallPosition(l.room,"left",3,1.2f)});
            var json=DocumentStore.SerializeLayout(l);var loaded=DocumentStore.ParseLayout(json);Assert.AreEqual(json,DocumentStore.SerializeLayout(loaded));Assert.IsTrue(Copies.Equal(l,loaded));
        }
        [Test] public void StrictGeometryRejectsMissingCoordinatesAndWrongTypes()
        {
            var l=new Layout{version=2};l.areas.Add(new Area{id="room",name="Room",vertices=Geometry.Rectangle(-2,-2,2,2)});var json=DocumentStore.SerializeLayout(l);
            Assert.Throws<DocumentException>(()=>DocumentStore.ParseLayout(System.Text.RegularExpressions.Regex.Replace(json,@"""centerX""\s*:\s*[^,}]+,","")));
            Assert.Throws<DocumentException>(()=>DocumentStore.ParseLayout(json.Replace("\"vertices\": [","\"vertices\": null, \"ignored\": [")));
        }
    }
}
