using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BlueCafe.Core;
using UnityEngine;

namespace BlueCafe.Infrastructure
{
    [Serializable] public sealed class Settings
    {
        public int version = 1;
        public string packRoot = "", defaultCafeId = "", emptyCafeId = "";
    }
    [Serializable] public sealed class CafeRecord
    {
        public int version = 1;
        public string cafeId, name, updatedUtc;
        public Layout layout;
    }
    [Serializable] public sealed class RecoveryRecord
    {
        public int version = 1;
        public string documentId, cafeId, writtenUtc;
        public long revision;
        public Layout layout;
    }
    public sealed class DocumentException : Exception
    {
        public string Code { get; }
        public DocumentException(string code, string message) : base(message) { Code = code; }
    }
    /// <summary>App-local identity and recovery are separate from portable layout JSON.</summary>
    public sealed class DocumentStore
    {
        public string Root { get; }
        readonly object gate = new object();
        readonly Dictionary<string, long> invalidated = new Dictionary<string, long>(StringComparer.Ordinal);
        string CafeFolder => Path.Combine(Root, "cafes");
        string RecoveryPath => Path.Combine(Root, "automatic-recovery.json");
        public DocumentStore(string appDataRoot)
        {
            if (string.IsNullOrWhiteSpace(appDataRoot)) throw new ArgumentException("An application data folder is required.", nameof(appDataRoot));
            Root = Path.GetFullPath(appDataRoot);
        }
        public static Layout LoadLayout(string path) => ParseLayout(File.ReadAllText(path, Encoding.UTF8));
        public static void SaveLayout(string path, Layout layout) => AtomicWrite(path, SerializeLayout(layout));
        public static Layout ParseLayout(string json)
        {
            try
            {
                InspectLayoutJson(json);
                var layout = JsonUtility.FromJson<Layout>(json);
                ValidatePortable(layout);
                return layout;
            }
            catch (DocumentException) { throw; }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException)
            { throw new DocumentException("INVALID_LAYOUT", "Cannot read cafe layout: " + ex.Message); }
        }
        public static string SerializeLayout(Layout layout)
        {
            ValidatePortable(layout);
            return JsonUtility.ToJson(layout, true);
        }
        // Catalog-dependent legality is checked by the shared Core before adoption.
        // This layer accepts known unavailable instances and never rewrites IDs/transforms.
        static void ValidatePortable(Layout layout)
        {
            if (layout == null || (layout.version != 1 && layout.version != 2) || layout.room == null || layout.instances == null)
                throw new DocumentException("INVALID_LAYOUT", "Expected a version 1 or 2 layout with room and instances.");
            try { Geometry.Validate(layout); } catch(ValidationException e) { throw new DocumentException(e.code,e.Message); }
            var room = layout.room;
            if (!Finite(room.width) || !Finite(room.depth) || !Finite(room.wallHeight) || !Finite(room.grid) || room.width <= 0 || room.depth <= 0 || room.wallHeight <= 0 || room.grid <= 0 || room.width > 1000 || room.depth > 1000 || room.grid > Math.Min(room.width, room.depth))
                throw new DocumentException("INVALID_ROOM", "Room dimensions and grid must be finite, positive and supported.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var instance in layout.instances)
            {
                if (instance == null || string.IsNullOrWhiteSpace(instance.instanceId) || string.IsNullOrWhiteSpace(instance.furnitureId) || !ids.Add(instance.instanceId))
                    throw new DocumentException("INVALID_INSTANCE", "Instances need unique nonempty instance IDs and furniture IDs.");
                if (!Finite(instance.position.x) || !Finite(instance.position.y) || !Finite(instance.position.z) || instance.position.y < 0 || instance.position.y > room.wallHeight)
                    throw new DocumentException("INVALID_TRANSFORM", "Instance position must be finite with a valid elevation.");
                if (!Finite(instance.wallAlong)) throw new DocumentException("INVALID_ATTACHMENT", "Wall along coordinate must be finite.");
                if (instance.quarterTurns < 0 || instance.quarterTurns > 3) throw new DocumentException("INVALID_ROTATION", "quarterTurns must be 0, 1, 2 or 3.");
            }
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static void InspectLayoutJson(string json, string prefix = "")
        {
            int portableVersion=0; HashSet<string> layoutKeys=null, roomKeys=null;
            StrictJson.Inspect(json, (path, keys) =>
            {
                path = LayoutJsonPath(path, prefix); if (path == null) return;
                if (path == "") { layoutKeys=keys; StrictJson.Require(keys, "version", "room", "instances"); }
                else if (path == ".room") {roomKeys=keys; StrictJson.Require(keys, "width", "depth", "wallHeight", "grid");}
                else if(Regex.IsMatch(path,@"^\.areas\[\d+\]$")) StrictJson.Require(keys,"id","name","vertices");
                else if(Regex.IsMatch(path,@"^\.areas\[\d+\]\.vertices\[\d+\]$")) StrictJson.Require(keys,"x","y","z");
                else if(Regex.IsMatch(path,@"^\.openings\[\d+\]$")) StrictJson.Require(keys,"id","wallId","along","width","bottom","height");
                else if (Regex.IsMatch(path, @"^\.instances\[\d+\]\.position$")) StrictJson.Require(keys, "x", "y", "z");
                else if (Regex.IsMatch(path, @"^\.instances\[\d+\]$")) StrictJson.Require(keys, "instanceId", "furnitureId", "position", "quarterTurns");
            }, (path, kind) =>
            {
                path = LayoutJsonPath(path, prefix); if (path == null) return;
                if (path == "" && kind != '{') throw new FormatException("Expected layout object.");
                string member = path.Substring(path.LastIndexOf('.') + 1);
                bool root = path == ".version";
                bool roomNumber = path.StartsWith(".room.", StringComparison.Ordinal) && new[] { "width", "depth", "wallHeight", "grid", "centerX", "centerZ" }.Contains(member);
                bool geometryNumber = Regex.IsMatch(path,@"^\.areas\[\d+\]\.vertices\[\d+\]\.(x|y|z)$") || Regex.IsMatch(path,@"^\.openings\[\d+\]\.(along|width|bottom|height)$") || Regex.IsMatch(path,@"^\.instances\[\d+\]\.wallAlong$");
                bool geometryString = Regex.IsMatch(path,@"^\.areas\[\d+\]\.(id|name|color|flooringId|wallpaperId)$") || Regex.IsMatch(path,@"^\.openings\[\d+\]\.(id|wallId)$") || Regex.IsMatch(path,@"^\.instances\[\d+\]\.wallId$");
                if(Regex.IsMatch(path,@"^\.instances\[\d+\]\.legacyWallFootprint$") && kind!='t' && kind!='f') throw new FormatException("Expected boolean " + path);
                if(geometryString && kind != '\"') throw new FormatException("Expected string " + path);
                if((path==".areas" || path==".openings" || Regex.IsMatch(path,@"^\.areas\[\d+\]\.vertices$")) && kind!='[') throw new FormatException("Expected geometry array " + path);
                if((Regex.IsMatch(path,@"^\.(areas|openings)\[\d+\]$") || Regex.IsMatch(path,@"^\.areas\[\d+\]\.vertices\[\d+\]$")) && kind!='{') throw new FormatException("Expected geometry object " + path);
                bool positionNumber = Regex.IsMatch(path, @"^\.instances\[\d+\]\.(quarterTurns|position\.(x|y|z))$");
                bool instanceMember = Regex.IsMatch(path, @"^\.instances\[\d+\]\.[^.\[]+$");
                if ((root || roomNumber || positionNumber || geometryNumber) && kind != '-' && (kind < '0' || kind > '9')) throw new FormatException("Expected numeric member " + path + ".");
                if ((path == ".room" || (instanceMember && member == "position")) && kind != '{') throw new FormatException("Expected object " + path + ".");
                if (path == ".instances" && kind != '[') throw new FormatException("Expected instances array.");
                if (((instanceMember && (member == "instanceId" || member == "furnitureId")) || path == ".name") && kind != '"') throw new FormatException("Expected string " + path + ".");
            }, (path, number) =>
            {
                path = LayoutJsonPath(path, prefix); if (path == null) return;
                if(path==".version") int.TryParse(number,out portableVersion);
                if (path == ".version" && number != "1" && number != "2") throw new DocumentException("UNSUPPORTED_LAYOUT_VERSION", "Only portable layout versions 1 and 2 are supported.");
                if (Regex.IsMatch(path, @"^\.instances\[\d+\]\.quarterTurns$") && !int.TryParse(number, out _)) throw new FormatException("quarterTurns must be an integer.");
            });
            if(portableVersion==2) { StrictJson.Require(layoutKeys,"areas","openings");StrictJson.Require(roomKeys,"centerX","centerZ"); }
        }
        static string LayoutJsonPath(string path, string prefix)
        {
            if (prefix.Length == 0) return path;
            if (path == prefix || path.StartsWith(prefix + ".", StringComparison.Ordinal)) return path.Substring(prefix.Length);
            return null;
        }
        public Settings LoadSettings()
        {
            string path = Path.Combine(Root, "settings.json");
            if (!File.Exists(path)) return new Settings();
            string json = File.ReadAllText(path, Encoding.UTF8); StrictJson.Require(StrictJson.Validate(json), "version", "packRoot");
            var settings = JsonUtility.FromJson<Settings>(json);
            if (settings == null || settings.version != 1) throw new DocumentException("INVALID_SETTINGS", "Unsupported application settings version.");
            return settings;
        }
        public void SaveSettings(Settings settings)
        {
            if (settings == null || settings.version != 1) throw new DocumentException("INVALID_SETTINGS", "Expected settings version 1.");
            lock (gate) AtomicWrite(Path.Combine(Root, "settings.json"), JsonUtility.ToJson(settings, true));
        }
        public CafeRecord[] ListCafes()
        {
            lock (gate)
            {
                if (!Directory.Exists(CafeFolder)) return Array.Empty<CafeRecord>();
                return Directory.GetFiles(CafeFolder, "*.json").Select(ReadCafe).OrderBy(c => c.name, StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }
        public CafeRecord EnsureDefaultCafe(Layout emptyLayout)
        {
            var starter = ParseLayout(SerializeLayout(emptyLayout));
            if (starter.instances.Any(i => i.furnitureId != "1" && i.furnitureId != "2"))
                throw new DocumentException("EMPTY_LAYOUT_REQUIRED", "The startup starter may contain only the original floor and wall styles, with no furniture.");
            lock (gate)
            {
                var settings = LoadSettings();
                var selected = FindSavedCafe(settings.defaultCafeId);
                if (selected != null) return selected;
                selected = FindSavedCafe(settings.emptyCafeId);
                if (selected == null)
                {
                    var names = new HashSet<string>(ListCafes().Select(c => c.name), StringComparer.OrdinalIgnoreCase);
                    starter.name = "Empty room";
                    for (int suffix = 2; names.Contains(starter.name); suffix++) starter.name = "Empty room (" + suffix + ")";
                    selected = SaveCafe(starter);
                    settings.emptyCafeId = selected.cafeId;
                }
                settings.defaultCafeId = selected.cafeId;
                SaveSettings(settings);
                return selected;
            }
        }
        CafeRecord FindSavedCafe(string cafeId)
        {
            if (!Guid.TryParseExact(cafeId, "N", out _)) return null;
            string path = CafePath(cafeId);
            if (!File.Exists(path)) return null;
            return ReadCafe(path);
        }
        public CafeRecord SaveCafe(Layout layout, string cafeId = null)
        {
            string portable = SerializeLayout(layout);
            if (string.IsNullOrWhiteSpace(layout.name)) throw new DocumentException("NAME_REQUIRED", "Choose a name for this cafe.");
            lock (gate)
            {
                if (cafeId != null) ValidateIdentity(cafeId);
                else cafeId = Guid.NewGuid().ToString("N");
                if (ListCafes().Any(c => c.cafeId != cafeId && string.Equals(c.name, layout.name, StringComparison.OrdinalIgnoreCase)))
                    throw new DocumentException("NAME_COLLISION", "A different saved cafe has this name. Choose another name or explicitly replace that cafe.");
                var record = new CafeRecord { cafeId = cafeId, name = layout.name, updatedUtc = DateTime.UtcNow.ToString("O"), layout = ParseLayout(portable) };
                AtomicWrite(CafePath(cafeId), JsonUtility.ToJson(record, true));
                return record;
            }
        }
        public Layout LoadCafe(string cafeId) { lock (gate) return ReadCafe(CafePath(cafeId)).layout; }
        public CafeRecord RenameCafe(string cafeId, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new DocumentException("NAME_REQUIRED", "Choose a name for this cafe.");
            lock (gate)
            {
                var record = ReadCafe(CafePath(cafeId));
                name = name.Trim();
                if (record.name == name) return record;
                record.layout.name = name;
                return SaveCafe(record.layout, cafeId);
            }
        }
        public void DeleteCafe(string cafeId) { lock (gate) File.Delete(CafePath(cafeId)); }
        string CafePath(string id) { ValidateIdentity(id); return Path.Combine(CafeFolder, id + ".json"); }
        static void ValidateIdentity(string id)
        {
            if (!Guid.TryParseExact(id, "N", out _)) throw new DocumentException("INVALID_DOCUMENT_ID", "Saved-cafe identities must be local GUIDs.");
        }
        static CafeRecord ReadCafe(string path)
        {
            string json = File.ReadAllText(path, Encoding.UTF8); StrictJson.Require(StrictJson.Validate(json), "version", "cafeId", "name", "layout");
            InspectLayoutJson(json, ".layout");
            var record = JsonUtility.FromJson<CafeRecord>(json);
            if (record == null || record.version != 1) throw new DocumentException("INVALID_CAFE", "Unsupported saved-cafe record.");
            ValidateIdentity(record.cafeId); ValidatePortable(record.layout);
            if (Path.GetFileNameWithoutExtension(path) != record.cafeId) throw new DocumentException("INVALID_CAFE", "Saved-cafe identity differs from its local filename.");
            return record;
        }
        /// <summary>Returns false for stale or explicitly discarded work. Errors leave older complete snapshots intact.</summary>
        public bool WriteRecovery(Layout layout, string documentId, long revision, string cafeId = null)
        {
            ValidateIdentity(documentId);
            if (cafeId != null) ValidateIdentity(cafeId);
            if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
            string portable = SerializeLayout(layout);
            lock (gate)
            {
                if (invalidated.TryGetValue(documentId, out var limit) && revision <= limit) return false;
                var current = LoadRecovery();
                if (current != null && current.documentId == documentId && current.revision >= revision) return false;
                var record = new RecoveryRecord { documentId = documentId, cafeId = cafeId, revision = revision, writtenUtc = DateTime.UtcNow.ToString("O"), layout = ParseLayout(portable) };
                // A corrupt latest file must not overwrite the retained complete backup.
                string backup = ReadRecoveryFile(RecoveryPath) == null ? null : RecoveryPath + ".previous";
                AtomicWrite(RecoveryPath, JsonUtility.ToJson(record, true), backup);
                return true;
            }
        }
        public RecoveryRecord LoadRecovery()
        {
            lock (gate)
            {
                foreach (string path in new[] { RecoveryPath, RecoveryPath + ".previous" })
                {
                    var record = ReadRecoveryFile(path);
                    if (record != null) return record;
                }
                return null;
            }
        }
        static RecoveryRecord ReadRecoveryFile(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8); StrictJson.Require(StrictJson.Validate(json), "version", "documentId", "revision", "layout");
                InspectLayoutJson(json, ".layout");
                var record = JsonUtility.FromJson<RecoveryRecord>(json);
                if (record == null || record.version != 1 || record.revision < 0) return null;
                ValidateIdentity(record.documentId); ValidatePortable(record.layout);
                return record;
            }
            catch (Exception ex) when (ex is IOException || ex is FormatException || ex is ArgumentException || ex is DocumentException) { return null; }
        }
        /// <summary>Use savedRevision after Save; omit it after Discard to reject all pending writes for this document.</summary>
        public void ClearRecovery(string documentId, long savedRevision = long.MaxValue)
        {
            ValidateIdentity(documentId);
            lock (gate)
            {
                invalidated[documentId] = invalidated.TryGetValue(documentId, out var old) ? Math.Max(old, savedRevision) : savedRevision;
                foreach (string path in new[] { RecoveryPath, RecoveryPath + ".previous" })
                {
                    var record = ReadRecoveryFile(path);
                    if (record == null || (record.documentId == documentId && record.revision <= savedRevision)) File.Delete(path);
                }
            }
        }
        static void AtomicWrite(string path, string content, string previous = null)
        {
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = new UTF8Encoding(false, true).GetBytes(content);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, previous);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
