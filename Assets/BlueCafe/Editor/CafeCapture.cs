using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace BlueCafe
{
    // Crop coordinates use image top-left pixels. No implicit resize is performed.
    [Serializable] public class CafeCaptureCrop { public int x, y, width, height; }
    [Serializable] public class CafeCameraProfile
    {
        public int version = 1;
        public string framingSource = "explicit-unregistered";
        public float yaw, elevation;
        public Vector3 target;
        public string projection = "orthographic";
        public float orthographicSize = 10, fieldOfView = 60, distance = 50, nearClip = .01f, farClip = 1000;
        public int width = 1600, height = 1200;
        public CafeCaptureCrop crop = new CafeCaptureCrop { width = 1600, height = 1200 };
    }
    [Serializable] public class CafePlanLabel
    {
        public string instanceId, furnitureId;
        public int quarterTurns;
        public Vector3 position;
        public Vector2 pixel;
        public string orientationMeaning = "unknown";
    }
    [Serializable] public class CafePlanCapture
    {
        public int version = 1;
        public string coordinateSystem = "top-left pixels; +X right; +Z up";
        public CafeCameraProfile camera;
        public CafeReferencePlan reference;
        public CafePlanLabel[] labels;
    }
    [Serializable] public class CafeOrientationAtlas
    {
        public int version = 1;
        public string furnitureId, sourcePrefab, orientationMeaning = "unknown";
        public CafeCameraProfile camera;
        public string[] panels;
        public int[] quarterTurns = { 0, 1, 2, 3 };
    }
    public static partial class CafeReconstruction
    {
        static void CameraRequire(bool valid, string message)
        {
            if (!valid) throw new CafeValidationException("INVALID_CAMERA_PROFILE", message);
        }
        public static void ValidateCameraProfile(CafeCameraProfile p)
        {
            CameraRequire(p != null && p.version == 1, "Expected version 1 camera profile.");
            CameraRequire(new[] { p.yaw, p.elevation, p.target.x, p.target.y, p.target.z, p.orthographicSize, p.fieldOfView, p.distance, p.nearClip, p.farClip }.All(CafeApi.Finite), "Camera values must be finite.");
            CameraRequire(p.projection == "orthographic" || p.projection == "perspective", "Projection must be orthographic or perspective.");
            CameraRequire(p.elevation >= -90 && p.elevation <= 90 && p.orthographicSize > 0 && p.fieldOfView > 0 && p.fieldOfView < 180 && p.distance > 0 && p.nearClip > 0 && p.farClip > p.nearClip, "Invalid lens or camera range.");
            CameraRequire(p.width >= 32 && p.width <= 4096 && p.height >= 32 && p.height <= 4096, "Render dimensions must be 32..4096.");
            CameraRequire(p.crop != null && p.crop.x >= 0 && p.crop.y >= 0 && p.crop.width > 0 && p.crop.height > 0 && (long)p.crop.x + p.crop.width <= p.width && (long)p.crop.y + p.crop.height <= p.height, "Crop must be inside the rendered image.");
        }
        static string WriteCaptureJson(string path, object value)
        {
            path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(value, true)); return path;
        }
        public static string SaveCameraProfile(string path, CafeCameraProfile profile)
        {
            ValidateCameraProfile(profile); return WriteCaptureJson(path, profile);
        }
        public static CafeCameraProfile LoadCameraProfile(string path)
        {
            CafeCameraProfile profile;
            // Prevent JsonUtility defaults from silently making an incomplete profile valid.
            var json = File.ReadAllText(path);
            try
            {
                var root = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                void Number(JObject obj, string field, bool integer = false)
                {
                    var token = obj[field]; CameraRequire(token != null && (token.Type == JTokenType.Integer || (!integer && token.Type == JTokenType.Float)), "Missing or invalid numeric field: " + field);
                }
                foreach (var field in new[] { "version", "width", "height" }) Number(root, field, true);
                foreach (var field in new[] { "yaw", "elevation", "orthographicSize", "fieldOfView", "distance", "nearClip", "farClip" }) Number(root, field);
                CameraRequire(root["projection"]?.Type == JTokenType.String, "Missing or invalid projection.");
                CameraRequire(root["target"] is JObject && root["crop"] is JObject, "Target and crop objects must be explicit.");
                foreach (var field in new[] { "x", "y", "z" }) Number((JObject)root["target"],field);
                foreach (var field in new[] { "x", "y", "width", "height" }) Number((JObject)root["crop"],field,true);
                profile = new CafeCameraProfile { version = -1, yaw = float.NaN, elevation = float.NaN,
                    target = new Vector3(float.NaN,float.NaN,float.NaN), projection = null,
                    orthographicSize = float.NaN, fieldOfView = float.NaN, distance = float.NaN, nearClip = float.NaN, farClip = float.NaN,
                    width = -1, height = -1, crop = new CafeCaptureCrop { x = -1, y = -1, width = -1, height = -1 } };
                JsonUtility.FromJsonOverwrite(json, profile);
            }
            catch (CafeValidationException) { throw; }
            catch (Exception e) { throw new CafeValidationException("INVALID_CAMERA_PROFILE", e.Message); }
            ValidateCameraProfile(profile); return profile;
        }
        public static string FreezeCameraProfile(string path, int width = 1600, int height = 1200)
        {
            using (var preview = new CafePreview())
            {
                preview.Rebuild(CafeApi.Layout); preview.camera.aspect = (float)width / height; preview.FrameRoom();
                var c = preview.camera; var angles = c.transform.eulerAngles;
                var profile = new CafeCameraProfile { framingSource = "room-fit-unregistered", yaw = angles.y, elevation = Mathf.DeltaAngle(0, angles.x),
                    target = c.transform.position + c.transform.forward * 50, distance = 50,
                    orthographicSize = c.orthographicSize, fieldOfView = c.fieldOfView,
                    nearClip = c.nearClipPlane, farClip = c.farClipPlane, width = width, height = height,
                    crop = new CafeCaptureCrop { width = width, height = height } };
                return SaveCameraProfile(path, profile);
            }
        }
        public static string FreezeCameraProfile(string path, CafeCameraProfile profile) => SaveCameraProfile(path, profile);
        public static string CaptureReference(string path, string profilePath) => CaptureReference(path, LoadCameraProfile(profilePath));
        public static string CaptureReference(string path, CafeCameraProfile profile)
        {
            ValidateCameraProfile(profile);
            using (var preview = new CafePreview())
            {
                preview.Rebuild(CafeApi.Layout); preview.ApplyCameraProfile(profile);
                using (var pixels = new CapturePixels(preview, profile)) return pixels.Save(path);
            }
        }
        public static string CapturePlan(string path, int width = 1600, int height = 1200) => CapturePlanCore(path, null, width, height);
        public static string CapturePlan(string path, CafeReferencePlan plan, int width = 1600, int height = 1200)
        { ValidateReferencePlan(plan); return CapturePlanCore(path, plan, width, height); }
        static string CapturePlanCore(string path, CafeReferencePlan plan, int width, int height)
        {
            var layout = CafeApi.Layout; CafeApi.ValidateLayout(layout);
            var profile = new CafeCameraProfile { yaw = 0, elevation = 90, target = Vector3.zero,
                width = width, height = height, orthographicSize = Mathf.Max(layout.room.depth / 2, layout.room.width / 2 / ((float)width / height)) * 1.1f,
                crop = new CafeCaptureCrop { width = width, height = height } };
            ValidateCameraProfile(profile);
            using (var preview = new CafePreview())
            {
                preview.Rebuild(layout); preview.ApplyCameraProfile(profile);
                // Roof/backdrops and wall surfaces occlude a floor plan; their identities remain in the legend.
                foreach (var i in layout.instances.Where(i => CafeApi.IsRoomStyle(CafeApi.Find(i.furnitureId)))) preview.Objects[i.instanceId].SetActive(false);
                using (var pixels = new CapturePixels(preview, profile))
                {
                    Vector2 Pixel(Vector3 point) { var v = preview.camera.WorldToViewportPoint(point); return new Vector2(v.x * width, (1-v.y) * height); }
                    var gridColor = new Color(.45f,.50f,.55f);
                    // Source-cell coordinates remain anchored at the room's minimum edges, including partial edge cells.
                    float unit = CafeApi.Catalog.gridUnit;
                    for (int n = 0; n <= Mathf.FloorToInt(layout.room.width/unit); n++)
                    { float x = -layout.room.width/2+n*unit; pixels.Line(Pixel(new Vector3(x,0,-layout.room.depth/2)), Pixel(new Vector3(x,0,layout.room.depth/2)), gridColor); }
                    for (int n = 0; n <= Mathf.FloorToInt(layout.room.depth/unit); n++)
                    { float z = -layout.room.depth/2+n*unit; pixels.Line(Pixel(new Vector3(-layout.room.width/2,0,z)), Pixel(new Vector3(layout.room.width/2,0,z)), gridColor); }
                    pixels.Text(8, 8, "REAR WALL Z MAX / +Z UP / CELL " + unit.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                    pixels.Text(8, 18, "REAR WALL X MIN / +X RIGHT / ORIGIN ROOM CENTER");
                    var labels = layout.instances.OrderBy(i => i.instanceId, StringComparer.Ordinal).Select(i => {
                        var v = preview.camera.WorldToViewportPoint(i.position);
                        return new CafePlanLabel { instanceId = i.instanceId, furnitureId = i.furnitureId, quarterTurns = i.quarterTurns, position = i.position,
                            pixel = new Vector2(v.x * width, (1 - v.y) * height) };
                    }).ToArray();
                    var orientations = File.Exists(OrientationPath) ? LoadOrientations() : null;
                    for (int n = 0; n < labels.Length; n++)
                    {
                        var label = labels[n]; var item = CafeApi.Find(label.furnitureId);
                        // Whole-room styles share the origin and hide floor-instance labels there.
                        // They stay fully identified by the numbered legend and sidecar below.
                        if (CafeApi.IsRoomStyle(item)) continue;
                        pixels.Text(Mathf.RoundToInt(label.pixel.x), Mathf.RoundToInt(label.pixel.y), (n + 1) + " " + label.furnitureId + " Q" + label.quarterTurns);
                        var annotation = orientations?.items.FirstOrDefault(a => a.furnitureId == label.furnitureId);
                        if (annotation != null && !string.IsNullOrWhiteSpace(annotation.sourcePrefab) && annotation.sourcePrefab != item.sourcePrefab)
                            throw new CafeValidationException("STALE_ANNOTATION", label.furnitureId);
                        var direction = annotation?.directions.FirstOrDefault(d => d.name == "front" && d.status == "audited") ??
                            annotation?.directions.FirstOrDefault(d => d.name == "seat" && d.status == "audited");
                        if (item.placement != "floor" || direction == null) continue;
                        label.orientationMeaning = "audited-" + direction.name;
                        var worldDirection = Quaternion.Euler(0,label.quarterTurns*90,0) * direction.local;
                        pixels.Arrow(label.pixel,Pixel(label.position + worldDirection * unit * .8f),new Color(0,.5f,0),direction.name);
                    }
                    var overlayLegend = plan == null ? Array.Empty<string>() : DrawReferenceOverlay(pixels, preview.camera, profile, plan, layout.room);
                    // Full stable IDs in a separate legend canvas avoid truncation and preserve render geometry.
                    pixels.AddLegend(labels.Select((i,n) => (n+1) + " " + i.instanceId + " / " + i.furnitureId + " Q" + i.quarterTurns).Concat(overlayLegend).ToArray());
                    var result = pixels.Save(path); WriteCaptureJson(Path.ChangeExtension(result, ".plan.json"), new CafePlanCapture { camera = profile, reference = plan, labels = labels }); return result;
                }
            }
        }
        public static string CaptureReferenceOverlay(string path, CafeCameraProfile profile, CafeReferencePlan plan)
        {
            ValidateCameraProfile(profile); ValidateReferencePlan(plan);
            using (var preview = new CafePreview())
            {
                var layout = CafeApi.Layout; preview.Rebuild(layout); preview.ApplyCameraProfile(profile);
                using (var pixels = new CapturePixels(preview,profile))
                {
                    pixels.AddLegend(DrawReferenceOverlay(pixels,preview.camera,profile,plan,layout.room));
                    path = pixels.Save(path); WriteCaptureJson(Path.ChangeExtension(path,".reference.json"),new CafePlanCapture { camera=profile, reference=plan, labels=Array.Empty<CafePlanLabel>() }); return path;
                }
            }
        }
        static string[] DrawReferenceOverlay(CapturePixels pixels, Camera camera, CafeCameraProfile profile, CafeReferencePlan plan, CafeRoom room)
        {
            var legend = new List<string> { "REFERENCE " + plan.id + " CAMERA " + plan.cameraStatus + " / CONFIRMED SOLID INFERRED DASHED" };
            Vector2 Pixel(Vector3 point) { var v=camera.WorldToViewportPoint(point); return new Vector2(v.x*profile.width-profile.crop.x,(1-v.y)*profile.height-profile.crop.y); }
            void Draw(string id, string status, string confidence, Vector3[] points, bool closed)
            {
                legend.Add(id + " " + status + " " + confidence);
                if ((status != "confirmed" && status != "inferred") || points.Length < 2) return;
                var color = status == "confirmed" ? new Color(0,.55f,.3f) : new Color(.9f,.4f,0);
                for (int n=1;n<points.Length;n++) pixels.Line(Pixel(points[n-1]),Pixel(points[n]),color,status == "inferred");
                if (closed) pixels.Line(Pixel(points[points.Length-1]),Pixel(points[0]),color,status == "inferred");
                var p=Pixel(points[0]); pixels.Text(Mathf.RoundToInt(p.x),Mathf.RoundToInt(p.y),id + " " + status);
            }
            foreach (var wall in plan.walls)
            {
                var points = wall.mapping == "x=min" ? new[] { new Vector3(-room.width/2,0,-room.depth/2),new Vector3(-room.width/2,0,room.depth/2) } :
                    wall.mapping == "z=max" ? new[] { new Vector3(-room.width/2,0,room.depth/2),new Vector3(room.width/2,0,room.depth/2) } : Array.Empty<Vector3>();
                Draw(wall.id + " " + wall.mapping,wall.status,wall.confidence,points,false);
            }
            foreach (var boundary in plan.boundaries)
            {
                Draw(boundary.id + " " + boundary.kind,boundary.status,boundary.confidence,boundary.cellPoints.Select(v=>v*plan.sourceCellUnit).ToArray(),false);
                foreach (var opening in boundary.openings)
                {
                    legend.Add(opening.id + " OPENING " + opening.status);
                    if (opening.status != "confirmed" && opening.status != "inferred") continue;
                    var a=Pixel(opening.startCell*plan.sourceCellUnit); var b=Pixel(opening.endCell*plan.sourceCellUnit);
                    pixels.Line(a,b,Color.white,opening.status=="inferred"); pixels.Text(Mathf.RoundToInt(a.x),Mathf.RoundToInt(a.y),opening.id + " OPENING");
                }
            }
            foreach (var zone in plan.zones) Draw(zone.id + " ZONE",zone.status,zone.confidence,zone.cellPolygon.Select(v=>v*plan.sourceCellUnit).ToArray(),true);
            legend.AddRange(plan.uncertainties.Select(u=>"UNKNOWN " + u)); return legend.ToArray();
        }
        public static string CaptureOrientationAtlas(string furnitureId, string path, int panelWidth = 600, int panelHeight = 600)
        {
            var item = CafeApi.Find(furnitureId);
            var annotation = File.Exists(OrientationPath) ? LoadOrientations().items.FirstOrDefault(a => a.furnitureId == furnitureId) : null;
            if (annotation != null && !string.IsNullOrWhiteSpace(annotation.sourcePrefab) && annotation.sourcePrefab != item.sourcePrefab)
                throw new CafeValidationException("STALE_ANNOTATION", furnitureId);
            var front = annotation?.directions.FirstOrDefault(d => d.name == "front" && d.status == "audited");
            if (!item.available) throw new CafeValidationException("ASSET_UNAVAILABLE", furnitureId);
            if (item.placement != "floor") throw new CafeValidationException("INVALID_ATLAS_ITEM", "Four floor rotations require floor furniture.");
            // Constrain through shared placement without mutating the user's session or undo history.
            float size = Mathf.Max(18, Mathf.Max(item.width, item.depth) * CafeApi.Catalog.gridUnit * 2);
            var layout = new CafeLayout { room = new CafeRoom { width = size, depth = size } };
            var i = new CafeInstance { instanceId = "atlas", furnitureId = furnitureId };
            layout.instances.Add(i);
            using (var preview = new CafePreview())
            {
                Bounds? all = null;
                for (int q = 0; q < 4; q++)
                {
                    i.quarterTurns = q; i.position = CafeApi.Constrain(item, Vector3.zero, q, layout.room); CafeApi.ValidateLayout(layout); preview.Rebuild(layout);
                    var b = preview.BoundsFor(i.instanceId);
                    if (!b.HasValue) throw new CafeValidationException("ASSET_UNAVAILABLE", furnitureId + " has no renderable acquired mesh.");
                    if (!all.HasValue) all = b; else { var union = all.Value; union.Encapsulate(b.Value); all = union; }
                }
                // Fit union across all rotations once. Every panel then has the identical target/lens.
                var bAll = all.Value; float radius = bAll.extents.magnitude;
                var profile = new CafeCameraProfile { yaw = 145, elevation = 35, target = bAll.center,
                    width = panelWidth, height = panelHeight, orthographicSize = Mathf.Max(.1f, radius * 1.12f / Mathf.Min(1, (float)panelWidth/panelHeight)),
                    crop = new CafeCaptureCrop { width = panelWidth, height = panelHeight } };
                ValidateCameraProfile(profile);
                var atlas = new Texture2D(panelWidth * 2, panelHeight * 2, TextureFormat.RGB24, false);
                try
                {
                    for (int q = 0; q < 4; q++)
                    {
                        i.quarterTurns = q; preview.Rebuild(layout); preview.ApplyCameraProfile(profile);
                        using (var pixels = new CapturePixels(preview, profile))
                        {
                            pixels.Text(8, 8, furnitureId + " Q" + q + " " + (q * 90) + " DEG FRONT " + (front == null ? "UNKNOWN" : "AUDITED"));
                            Vector2 Pixel(Vector3 point) { var v = preview.camera.WorldToViewportPoint(point); return new Vector2(v.x*panelWidth, (1-v.y)*panelHeight); }
                            float axisLength = Mathf.Max(.1f, radius * .7f);
                            var center = Pixel(bAll.center);
                            pixels.Arrow(center, Pixel(bAll.center + Vector3.right * axisLength), Color.red, "+X");
                            pixels.Arrow(center, Pixel(bAll.center + Vector3.forward * axisLength), Color.blue, "+Z");
                            if (front != null) pixels.Arrow(center, Pixel(bAll.center + Quaternion.Euler(0,q*90,0) * front.local * axisLength), new Color(0,.5f,0), "FRONT");
                            atlas.SetPixels((q % 2) * panelWidth, (1 - q / 2) * panelHeight, panelWidth, panelHeight, pixels.image.GetPixels());
                        }
                    }
                    atlas.Apply(); path = SaveImage(path, atlas);
                    WriteCaptureJson(Path.ChangeExtension(path, ".atlas.json"), new CafeOrientationAtlas { furnitureId = furnitureId, sourcePrefab = item.sourcePrefab, orientationMeaning = front == null ? "unknown" : "audited-front", camera = profile,
                        panels = new[] { "top-left", "top-right", "bottom-left", "bottom-right" } }); return path;
                }
                finally { UnityEngine.Object.DestroyImmediate(atlas); }
            }
        }
        static string SaveImage(string path, Texture2D image)
        {
            path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, image.EncodeToPNG()); return path;
        }
        sealed class CapturePixels : IDisposable
        {
            public Texture2D image;
            public CapturePixels(CafePreview preview, CafeCameraProfile p)
            {
                var target = preview.Render(p.width, p.height); var previous = RenderTexture.active;
                image = new Texture2D(p.crop.width, p.crop.height, TextureFormat.RGB24, false);
                try { RenderTexture.active = target; image.ReadPixels(new Rect(p.crop.x, p.height - p.crop.y - p.crop.height, p.crop.width, p.crop.height), 0, 0); image.Apply(); }
                catch { Dispose(); throw; }
                finally { RenderTexture.active = previous; }
            }
            public void AddLegend(string[] rows)
            {
                int w = Mathf.Max(image.width, rows.Length == 0 ? 0 : rows.Max(r => r.Length) * 6 + 16);
                var expanded = new Texture2D(w, image.height + rows.Length * 10 + 16, TextureFormat.RGB24, false);
                expanded.SetPixels(Enumerable.Repeat(Color.white, expanded.width * expanded.height).ToArray());
                expanded.SetPixels(0, expanded.height - image.height, image.width, image.height, image.GetPixels());
                int start = image.height + 8; UnityEngine.Object.DestroyImmediate(image); image = expanded;
                for (int n = 0; n < rows.Length; n++) Text(8, start + n * 10, rows[n]);
            }
            public void Line(Vector2 a, Vector2 b, Color color, bool dashed = false)
            {
                int steps = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(b.x-a.x), Mathf.Abs(b.y-a.y)));
                for (int n=0;n<=steps;n++)
                {
                    if (dashed && (n / 5) % 2 == 1) continue;
                    var p = Vector2.Lerp(a,b,steps == 0 ? 0 : (float)n/steps); int x = Mathf.RoundToInt(p.x), y = image.height-1-Mathf.RoundToInt(p.y);
                    if (x>=0 && x<image.width && y>=0 && y<image.height) image.SetPixel(x,y,color);
                }
            }
            public void Arrow(Vector2 a, Vector2 b, Color color, string label)
            {
                Line(a,b,color); var v=(a-b).normalized*7; var cross=new Vector2(-v.y,v.x)*.5f;
                Line(b,b+v+cross,color); Line(b,b+v-cross,color); Text(Mathf.RoundToInt(b.x)+3,Mathf.RoundToInt(b.y),label);
            }
            public void Text(int x, int y, string text)
            {
                foreach (char ch in text.ToUpperInvariant())
                {
                    string bits; if (!Glyphs.TryGetValue(ch, out bits)) bits = Glyphs['?'];
                    for (int dy = -1; dy <= 7; dy++) for (int dx = -1; dx <= 5; dx++)
                    {
                        int px = x + dx, py = image.height - 1 - y - dy;
                        if (px < 0 || px >= image.width || py < 0 || py >= image.height) continue;
                        bool ink = dx >= 0 && dx < 5 && dy >= 0 && dy < 7 && bits[dy * 5 + dx] == '1';
                        image.SetPixel(px, py, ink ? Color.black : Color.white);
                    }
                    x += 6;
                }
            }
            public string Save(string path) { image.Apply(); return SaveImage(path, image); }
            public void Dispose() { if (image) UnityEngine.Object.DestroyImmediate(image); }
            static readonly Dictionary<char, string> Glyphs = BuildGlyphs();
            static Dictionary<char,string> BuildGlyphs()
            {
                const string chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-/. ?+";
                string[] rows = {
                    "01110/10001/10011/10101/11001/10001/01110", "00100/01100/00100/00100/00100/00100/01110", "01110/10001/00001/00010/00100/01000/11111", "11110/00001/00001/01110/00001/00001/11110", "00010/00110/01010/10010/11111/00010/00010", "11111/10000/10000/11110/00001/00001/11110", "01110/10000/10000/11110/10001/10001/01110", "11111/00001/00010/00100/01000/01000/01000", "01110/10001/10001/01110/10001/10001/01110", "01110/10001/10001/01111/00001/00001/01110",
                    "01110/10001/10001/11111/10001/10001/10001", "11110/10001/10001/11110/10001/10001/11110", "01111/10000/10000/10000/10000/10000/01111", "11110/10001/10001/10001/10001/10001/11110", "11111/10000/10000/11110/10000/10000/11111", "11111/10000/10000/11110/10000/10000/10000", "01111/10000/10000/10111/10001/10001/01111", "10001/10001/10001/11111/10001/10001/10001", "01110/00100/00100/00100/00100/00100/01110", "00111/00010/00010/00010/00010/10010/01100", "10001/10010/10100/11000/10100/10010/10001", "10000/10000/10000/10000/10000/10000/11111", "10001/11011/10101/10101/10001/10001/10001", "10001/11001/10101/10011/10001/10001/10001", "01110/10001/10001/10001/10001/10001/01110", "11110/10001/10001/11110/10000/10000/10000", "01110/10001/10001/10001/10101/10010/01101", "11110/10001/10001/11110/10100/10010/10001", "01111/10000/10000/01110/00001/00001/11110", "11111/00100/00100/00100/00100/00100/00100", "10001/10001/10001/10001/10001/10001/01110", "10001/10001/10001/10001/10001/01010/00100", "10001/10001/10001/10101/10101/10101/01010", "10001/10001/01010/00100/01010/10001/10001", "10001/10001/01010/00100/00100/00100/00100", "11111/00001/00010/00100/01000/10000/11111", "00000/00000/00000/11111/00000/00000/00000", "00001/00010/00010/00100/01000/01000/10000", "00000/00000/00000/00000/00000/00100/00100", "00000/00000/00000/00000/00000/00000/00000", "01110/10001/00001/00010/00100/00000/00100", "00000/00100/00100/11111/00100/00100/00000"
                };
                var result = new Dictionary<char,string>(); for (int n=0;n<chars.Length;n++) result[chars[n]] = rows[n].Replace("/", ""); return result;
            }
        }
    }
}
