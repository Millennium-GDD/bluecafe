using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BlueCafe.Tests
{
    public class CafeWindowRendererTests
    {
        [Test] public void OriginalWindowRetainsAllSixPartsAndOnlyAuditedHelpersLeaveFraming()
        {
            var item = CafeApi.Find("210394");
            Assert.AreEqual(CafeWindowRendering.WindowPrefab, item.sourcePrefab);
            Assert.AreEqual(6, item.parts.Length);
            using (var preview = new CafePreview())
            {
                var layout = new CafeLayout();
                layout.instances.Add(new CafeInstance { instanceId = "window", furnitureId = item.id });
                preview.Rebuild(layout);
                var renderers = preview.Objects["window"].GetComponentsInChildren<Renderer>();
                Assert.AreEqual(6, renderers.Length, "No original geometry may be dropped to hide defects.");
                Assert.AreEqual(4, renderers.Count(preview.IsFramingHelper));
                Assert.AreEqual(2, renderers.Count(r => r.sharedMaterials.Any(m => m.GetInt("_StencilPass") == 2)));
                Assert.AreEqual(3, renderers.Count(r => r.sharedMaterials.Any(m => m.GetInt("_StencilRef") == 4)));
                Assert.Less(Mathf.Abs(preview.NativePivotOffsetFor("window").y), .01f, "Frame center, not huge source mask, defines wall pivot.");
                Assert.Greater(preview.BoundsFor("window").Value.size.y, 4.6f, "Full original geometry bounds stay inspectable.");
                foreach (var renderer in renderers) Assert.IsTrue(renderer.enabled);
                preview.ApplyCameraProfile(new CafeCameraProfile {width=800,height=800,orthographicSize=4,target=Vector3.zero,yaw=-90,elevation=0,crop=new CafeCaptureCrop {width=800,height=800}});
                Vector2 PickPoint(Vector3 world) {var screen=preview.camera.WorldToViewportPoint(world);return new Vector2(screen.x*800,(1-screen.y)*800);}
                Assert.AreEqual("window",preview.Pick(PickPoint(Vector3.zero),new Rect(0,0,800,800)),"Visible window frame remains selectable.");
                Assert.IsNull(preview.Pick(PickPoint(new Vector3(0,-2,0)),new Rect(0,0,800,800)),"Invisible mask/scenery AABB must not steal empty-space clicks.");
            }
        }

        [TestCase("22116",4,3)]
        [TestCase("19212",4,3)]
        [TestCase("23309",4,3)]
        [TestCase("23302",4,3)]
        [TestCase("23112",6,3)]
        [TestCase("15300",7,6)]
        [TestCase("22309",4,3)]
        [TestCase("18304",5,4)]
        [TestCase("23304",4,3)]
        public void AdditionalAuditedSurfacesKeepGeometryAndCenterTheirActualFrame(string id,int partCount,int helpers)
        {
            var item=CafeApi.Find(id);
            Assert.IsTrue(CafeWindowRendering.IsAuditedSurface(item));
            using(var preview=new CafePreview())
            {
                var layout=new CafeLayout();layout.instances.Add(new CafeInstance {instanceId="surface",furnitureId=id});
                preview.Rebuild(layout);
                var renderers=preview.Objects["surface"].GetComponentsInChildren<Renderer>();
                Assert.AreEqual(partCount,renderers.Length,"Native aperture/animated leaf geometry must remain present.");
                Assert.AreEqual(helpers,renderers.Count(preview.IsFramingHelper));
                var frame=renderers.Single(r=>CafeWindowRendering.IsFrameRenderer(item,r));
                Assert.AreEqual(0,frame.bounds.center.y,.0001f,"Placement elevation centers actual source frame.");
                Assert.IsFalse(preview.IsFramingHelper(frame));
                Assert.AreEqual(2,renderers.Count(r=>r.sharedMaterials.Any(m=>m.GetInt("_StencilPass")==2)));
                foreach(var renderer in renderers)
                {
                    Assert.IsTrue(renderer.enabled);
                    foreach(var material in renderer.sharedMaterials.Where(m=>m.GetInt("_StencilPass")==2))
                    {Assert.AreEqual(0,material.GetInt("_SrcBlend"));Assert.AreEqual(1,material.GetInt("_DstBlend"));Assert.AreEqual(0,material.GetInt("_ZWrite"));}
                }
            }
            var wrongSource=new CafeItem {id=id,sourcePrefab="different-source"};
            Assert.IsFalse(CafeWindowRendering.IsAuditedSurface(wrongSource));
        }

        [Test] public void SourceIdentityGuardsAgainstBlanketUntexturedSuppression()
        {
            var shader = Shader.Find("BlueCafe/BakedTexture"); Assert.IsNotNull(shader);
            var mat = new Material(shader);
            try
            {
                var spec = new CafeMaterial { name = "my_event088_retractwindow_cover", sourceShader = CafeWindowRendering.MaskShaderId };
                CafeWindowRendering.Apply(new CafeItem { id = "unrelated", placement = "floor" }, spec, mat);
                Assert.AreEqual(1, mat.GetInt("_SrcBlend")); Assert.AreEqual(1, mat.GetInt("_ZWrite"));
                CafeWindowRendering.Apply(new CafeItem { id = "210394", sourcePrefab = "different-original-prefab", placement = "wall" }, spec, mat);
                Assert.AreEqual(1, mat.GetInt("_SrcBlend"));
                spec.sourceShader = "unknown";
                CafeWindowRendering.Apply(new CafeItem { id = "210394", sourcePrefab = CafeWindowRendering.WindowPrefab }, spec, mat);
                Assert.AreEqual(1, mat.GetInt("_SrcBlend"));
            }
            finally { UnityEngine.Object.DestroyImmediate(mat); }
        }

        [Test] public void StencilOpeningShowsSceneryAndPreservesWallAndUntexturedForeground()
        {
            var original = CafeApi.Catalog;
            var field = typeof(CafeApi).GetField("catalog", BindingFlags.NonPublic | BindingFlags.Static);
            var paths = new List<string>();
            CafePart Quad(float minX, float maxX, float minY, float maxY, float z, CafeMaterial material)
            {
                var path = Path.GetTempFileName(); paths.Add(path);
                File.WriteAllText(path, JsonUtility.ToJson(new CafeMeshData {
                    vertices = new [] { minX,minY,z, maxX,minY,z, maxX,maxY,z, minX,maxY,z },
                    submeshes = new [] { new CafeSubmesh { triangles = new [] { 0,2,1,0,3,2 } } }
                }));
                return new CafePart { meshJsonPath = path, materials = new[] { material } };
            }
            CafeMaterial Spec(string name, string shaderId, Color color) => new CafeMaterial { name=name, sourceShader=shaderId, color=new [] {color.r,color.g,color.b,color.a} };
            try
            {
                var window = new CafeItem { id = "210394", sourcePrefab = CafeWindowRendering.WindowPrefab, placement = "wall", available = true, parts = new[] {
                    Quad(-1.5f,1.5f,-1.5f,1.5f,0,Spec("my_event088_retractwindow_cover",CafeWindowRendering.MaskShaderId,Color.white)),
                    Quad(-.5f,.5f,-.5f,.5f,0,Spec("my_event088_retractwindow_hole",CafeWindowRendering.MaskShaderId,Color.white)),
                    Quad(-1,1,-1,1,0,Spec("my_event088_retractwindow_out",CafeWindowRendering.SceneryShaderId,Color.blue)) } };
                // Use the audited wallpaper material state without the room module assembler,
                // so this test measures rasterized stencil semantics on a bounded fixture.
                var wall = new CafeItem { id = "wall", placement = "floor", available = true, parts = new[] {
                    Quad(-2,2,-2,2,.5f,Spec("wall-fixture",null,Color.red)) } };
                var other = new CafeItem { id = "other", placement = "floor", available = true, parts = new[] {
                    Quad(1.1f,1.4f,-.5f,.5f,-.5f,Spec("untextured-furniture",null,Color.green)) } };
                field.SetValue(null,new CafeCatalog { gridUnit=.6f, items=new[] {window,wall,other} });
                var layout = new CafeLayout();
                foreach(var id in new [] {"210394","wall","other"}) layout.instances.Add(new CafeInstance {instanceId=id,furnitureId=id});
                using(var preview = new CafePreview())
                {
                    preview.Rebuild(layout);
                    var wallMaterial = preview.Objects["wall"].GetComponentInChildren<Renderer>().sharedMaterial;
                    CafeWindowRendering.Apply(new CafeItem {placement="wallpaper"},Spec("my_defaultroom_wall",CafeWindowRendering.SceneryShaderId,Color.red),wallMaterial);
                    preview.ApplyCameraProfile(new CafeCameraProfile {width=256,height=256,orthographicSize=2,target=Vector3.zero,yaw=0,elevation=0,crop=new CafeCaptureCrop {width=256,height=256}});
                    var rt=preview.Render(256,256); var old=RenderTexture.active;
                    var pixels=new Texture2D(256,256,TextureFormat.RGB24,false);
                    try
                    {
                        RenderTexture.active=rt; pixels.ReadPixels(new Rect(0,0,256,256),0,0); pixels.Apply();
                        AssertColor(Color.blue,pixels.GetPixel(128,128),"Hole ref7 must reveal original scenery ref4.");
                        AssertColor(Color.red,pixels.GetPixel(176,128),"Cover ref4 must suppress scenery while preserving wallpaper ref7.");
                        AssertColor(Color.green,pixels.GetPixel(208,128),"Untextured unrelated foreground must remain visible.");
                    }
                    finally {RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(pixels);}
                }
            }
            finally {field.SetValue(null,original);foreach(var path in paths)File.Delete(path);}
        }
        static void AssertColor(Color expected, Color actual, string message)
        { Assert.Less(Vector3.Distance(new Vector3(expected.r,expected.g,expected.b),new Vector3(actual.r,actual.g,actual.b)),.05f,message); }
    }
}
