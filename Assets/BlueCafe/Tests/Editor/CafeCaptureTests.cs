using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace BlueCafe.Tests
{
    public class CafeCaptureTests
    {
        string directory;
        [SetUp] public void SetUp() { directory = Path.Combine(Path.GetTempPath(), "BlueCafeCapture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); }
        [TearDown] public void TearDown() { Directory.Delete(directory, true); }
        [Test] public void ProfileRoundTripPreservesPerspectiveAndExplicitCrop()
        {
            var p = new CafeCameraProfile { yaw = 143.5f, elevation = 32.25f, target = new Vector3(-2, 4, 8), projection = "perspective",
                distance = 37, fieldOfView = 42, width = 960, height = 720, crop = new CafeCaptureCrop { x = 15, y = 40, width = 510, height = 300 } };
            string path = Path.Combine(directory, "camera.json"); CafeReconstruction.SaveCameraProfile(path, p);
            var loaded = CafeReconstruction.LoadCameraProfile(path);
            Assert.AreEqual(JsonUtility.ToJson(p), JsonUtility.ToJson(loaded));
            using (var preview = new CafePreview())
            {
                preview.ApplyCameraProfile(loaded);
                Assert.IsFalse(preview.camera.orthographic);
                Assert.AreEqual(42, preview.camera.fieldOfView);
                Assert.Less(Vector3.Distance(loaded.target, preview.camera.transform.position + preview.camera.transform.forward * loaded.distance), .0001f);
            }
        }
        [Test] public void FrozenProfileSurvivesRebuildAndFirstRender()
        {
            var p = new CafeCameraProfile { yaw = 121, elevation = 44, target = new Vector3(3, 2, -1), orthographicSize = 7,
                width = 64, height = 64, crop = new CafeCaptureCrop { width = 64, height = 64 } };
            using (var preview = new CafePreview())
            {
                preview.Rebuild(new CafeLayout()); preview.ApplyCameraProfile(p);
                var position = preview.camera.transform.position; var rotation = preview.camera.transform.rotation;
                preview.Rebuild(new CafeLayout { room = new CafeRoom { width = 60, depth = 42 } });
                preview.Render(64, 64);
                Assert.AreEqual(position, preview.camera.transform.position);
                Assert.AreEqual(rotation, preview.camera.transform.rotation);
                Assert.AreEqual(7, preview.camera.orthographicSize);
            }
        }
        [Test] public void PlanCameraUsesWorldXRightAndWorldZUpWithoutRoll()
        {
            using (var preview = new CafePreview())
            {
                var profile = new CafeCameraProfile { yaw = 0, elevation = 90, target = Vector3.zero };
                preview.ApplyCameraProfile(profile);
                var center = preview.camera.WorldToViewportPoint(Vector3.zero);
                var east = preview.camera.WorldToViewportPoint(Vector3.right);
                var north = preview.camera.WorldToViewportPoint(Vector3.forward);
                Assert.Greater(east.x, center.x); Assert.AreEqual(center.y, east.y, .0001f);
                Assert.Greater(north.y, center.y); Assert.AreEqual(center.x, north.x, .0001f);
            }
        }
        [TestCase("perspective")]
        [TestCase("orthographic")]
        public void BackdropSpansTheViewAtItsActualCameraDistance(string projection)
        {
            var catalogField = typeof(CafeApi).GetField("catalog", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var originalCatalog = CafeApi.Catalog;
            try
            {
                // A mesh is unnecessary here: inspect the original backdrop assembly's camera-facing transform.
                catalogField.SetValue(null, new CafeCatalog { items = new[] { new CafeItem { id="backdrop", placement="background", available=true } } });
                var layout = new CafeLayout(); layout.instances.Add(new CafeInstance { instanceId="background-instance", furnitureId="backdrop" });
                using (var preview = new CafePreview())
                {
                    preview.Rebuild(layout);
                    var profile = new CafeCameraProfile { projection=projection, fieldOfView=42, orthographicSize=13, width=960, height=720,
                        crop=new CafeCaptureCrop { width=960, height=720 } };
                    // Bind the declared capture viewport before screen projection; a meshless
                    // preview camera otherwise inherits rounded host-window pixel dimensions.
                    preview.ApplyCameraProfile(profile); preview.Render(profile.width, profile.height);
                    var background = preview.Objects["background-instance"].transform;
                    float depth = Vector3.Dot(background.position-preview.camera.transform.position, preview.camera.transform.forward);
                    Assert.AreEqual(200, depth, .0001f);
                    float expectedHeight = projection == "perspective" ? 2*depth*Mathf.Tan(42*Mathf.Deg2Rad/2) : 26;
                    Assert.AreEqual(expectedHeight, background.localScale.y, .0001f);
                    Assert.AreEqual(expectedHeight*960/720, background.localScale.x, .0001f);
                    var lowerLeft = preview.camera.WorldToViewportPoint(background.TransformPoint(new Vector3(-.5f,-.5f,0)));
                    var upperRight = preview.camera.WorldToViewportPoint(background.TransformPoint(new Vector3(.5f,.5f,0)));
                    Assert.AreEqual(0,lowerLeft.x,.0001f); Assert.AreEqual(0,lowerLeft.y,.0001f);
                    Assert.AreEqual(1,upperRight.x,.0001f); Assert.AreEqual(1,upperRight.y,.0001f);
                }
            }
            finally { catalogField.SetValue(null,originalCatalog); }
        }
        [Test] public void CropIsTopLeftPixelsAndDoesNotReframeOrResize()
        {
            var p = new CafeCameraProfile { width = 64, height = 48, crop = new CafeCaptureCrop { x = 4, y = 5, width = 20, height = 15 } };
            // Empty acquired-independent layout avoids fixture furniture and only exercises camera/image IO.
            string old = CafeApi.ExportJson();
            try
            {
                CafeApi.NewLayout();
                string path = CafeReconstruction.CaptureReference(Path.Combine(directory, "crop.png"), p);
                var image = new Texture2D(2, 2);
                try { Assert.IsTrue(image.LoadImage(File.ReadAllBytes(path))); Assert.AreEqual(20, image.width); Assert.AreEqual(15, image.height); }
                finally { UnityEngine.Object.DestroyImmediate(image); }
            }
            finally { CafeApi.ImportJson(old); }
        }
        [Test] public void InvalidProfilesAreRejectedBeforeCameraMutationOrFileWrite()
        {
            var p = new CafeCameraProfile(); string path = Path.Combine(directory, "invalid.json");
            p.target.x = float.NaN; Assert.Throws<CafeValidationException>(() => CafeReconstruction.SaveCameraProfile(path,p)); Assert.IsFalse(File.Exists(path));
            p.target = Vector3.zero; p.crop.x = int.MaxValue;
            Assert.Throws<CafeValidationException>(() => CafeReconstruction.ValidateCameraProfile(p));
            p.crop.x = 0; p.projection = "unknown";
            Assert.Throws<CafeValidationException>(() => CafeReconstruction.ValidateCameraProfile(p));
            p.projection = "orthographic"; p.version = 2;
            Assert.Throws<CafeValidationException>(() => CafeReconstruction.ValidateCameraProfile(p));
        }
        [Test] public void MissingOrMalformedProfilesDoNotBecomeDefaultCamera()
        {
            string path = Path.Combine(directory,"camera.json");
            File.WriteAllText(path,"{}"); Assert.Throws<CafeValidationException>(() => CafeReconstruction.LoadCameraProfile(path));
            File.WriteAllText(path,"not json"); Assert.Throws<CafeValidationException>(() => CafeReconstruction.LoadCameraProfile(path));
            const string complete = "{\"version\":1,\"yaw\":0.0,\"elevation\":0.0,\"target\":{\"x\":0.0,\"y\":0.0,\"z\":0.0},\"projection\":\"orthographic\",\"orthographicSize\":10.0,\"fieldOfView\":60.0,\"distance\":50.0,\"nearClip\":0.01,\"farClip\":1000.0,\"width\":1600,\"height\":1200,\"crop\":{\"x\":0,\"y\":0,\"width\":1600,\"height\":1200}}";
            File.WriteAllText(path,complete); Assert.DoesNotThrow(() => CafeReconstruction.LoadCameraProfile(path));
            string missingYaw = complete.Replace("\"yaw\":0.0,", "");
            var variants = new[] {
                missingYaw,
                // A nested field with the same name must not satisfy a root requirement.
                missingYaw.Replace("\"target\":{", "\"target\":{\"yaw\":0.0,"),
                complete.Replace(",\"z\":0.0", ""),
                complete.Replace("\"target\":{\"x\":0.0,\"y\":0.0,\"z\":0.0}", "\"target\":{}"),
                complete.Replace("\"crop\":{\"x\":0,", "\"crop\":{"),
                complete.Replace("\"width\":1600,\"height\":1200,\"crop\"", "\"width\":64.5,\"height\":1200,\"crop\""),
                complete.Substring(0,complete.Length-1) + ",\"yaw\":123}"
            };
            foreach (var variant in variants)
            {
                Assert.AreNotEqual(complete,variant,"Malformed fixture mutation must change the valid JSON.");
                File.WriteAllText(path,variant); Assert.Throws<CafeValidationException>(() => CafeReconstruction.LoadCameraProfile(path));
            }
            File.WriteAllText(path,"null"); Assert.Throws<CafeValidationException>(() => CafeReconstruction.LoadCameraProfile(path));
        }
    }
}
