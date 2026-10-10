using System;
using System.Collections.Generic;
using System.Linq;

namespace BlueCafe.Core
{
    // Field names are the portable version-1 catalog/layout JSON contract.
    [Serializable] public class Catalog
    {
        public int version = 1;
        public float gridUnit = .6f;
        public string region, gameVersion, metadataSource, sourceCatalog, coordinates;
        public int patchVersion;
        public Item[] items = Array.Empty<Item>();
        public SupplementData[] supplements;
    }
    [Serializable] public class SupplementData
    {
        public string region, release, milestone, sourceCatalog, evidence;
        public int patchVersion, added;
    }
    [Serializable] public class Item
    {
        public BoundsData bounds;
        public string id, name, category, theme, modelPath, thumbnailPath, placement, sourcePrefab;
        public string availability, sourceAlias, sourceRegion, sourceRelease, sourceCatalog, provenancePath;
        public float width = 1, depth = 1, height = 1;
        public bool available;
        public Part[] parts;
        public string[] sourceBundles;
    }
    [Serializable] public class BoundsData { public float[] min, max; }
    [Serializable] public class Part
    {
        public string name, modelPath, meshJsonPath;
        public float[] position, rotation, scale;
        public MaterialSpec[] materials;
        public bool skinned;
    }
    [Serializable] public class MaterialSpec
    {
        public string name, texturePath, sourceShader;
        public float[] color, textureScale, textureOffset;
        public bool transparent;
        public float cutoff, cull;
    }
    [Serializable] public struct Position
    {
        public float x, y, z;
        public Position(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
    [Serializable] public class Room { public float width = 18, depth = 18, wallHeight = 4.8f, grid = .6f; }
    [Serializable] public class Instance
    {
        public string instanceId, furnitureId;
        public Position position;
        public int quarterTurns;
    }
    [Serializable] public class Layout
    {
        public int version = 1;
        public string name = "Untitled cafe";
        public Room room = new Room();
        public List<Instance> instances = new List<Instance>();
    }

    public static class Copies
    {
        public static Layout Copy(Layout value) => value == null ? null : new Layout {
            version = value.version, name = value.name, room = Copy(value.room),
            instances = value.instances?.Select(Copy).ToList()
        };
        public static Room Copy(Room value) => value == null ? null : new Room {
            width = value.width, depth = value.depth, wallHeight = value.wallHeight, grid = value.grid
        };
        public static Instance Copy(Instance value) => value == null ? null : new Instance {
            instanceId = value.instanceId, furnitureId = value.furnitureId,
            position = value.position, quarterTurns = value.quarterTurns
        };
        static float[] Copy(float[] value) => value == null ? null : (float[])value.Clone();
        public static Catalog Copy(Catalog value) => value == null ? null : new Catalog {
            version = value.version, gridUnit = value.gridUnit, region = value.region,
            gameVersion = value.gameVersion, patchVersion = value.patchVersion, metadataSource = value.metadataSource,
            sourceCatalog = value.sourceCatalog, coordinates = value.coordinates, items = value.items?.Select(Copy).ToArray(),
            supplements = value.supplements?.Select(s => s == null ? null : new SupplementData {
                region = s.region, release = s.release, milestone = s.milestone, sourceCatalog = s.sourceCatalog,
                evidence = s.evidence, patchVersion = s.patchVersion, added = s.added
            }).ToArray()
        };
        public static Item Copy(Item value) => value == null ? null : new Item {
            id = value.id, name = value.name, category = value.category, theme = value.theme,
            modelPath = value.modelPath, thumbnailPath = value.thumbnailPath, placement = value.placement,
            sourcePrefab = value.sourcePrefab, availability = value.availability, sourceAlias = value.sourceAlias,
            sourceRegion = value.sourceRegion, sourceRelease = value.sourceRelease, sourceCatalog = value.sourceCatalog,
            provenancePath = value.provenancePath, sourceBundles = value.sourceBundles == null ? null : (string[])value.sourceBundles.Clone(),
            width = value.width, depth = value.depth, height = value.height, available = value.available,
            bounds = value.bounds == null ? null : new BoundsData { min = Copy(value.bounds.min), max = Copy(value.bounds.max) },
            parts = value.parts?.Select(p => p == null ? null : new Part {
                name = p.name, modelPath = p.modelPath, meshJsonPath = p.meshJsonPath, skinned = p.skinned,
                position = Copy(p.position), rotation = Copy(p.rotation), scale = Copy(p.scale),
                materials = p.materials?.Select(m => m == null ? null : new MaterialSpec {
                    name = m.name, texturePath = m.texturePath, sourceShader = m.sourceShader,
                    color = Copy(m.color), textureScale = Copy(m.textureScale), textureOffset = Copy(m.textureOffset),
                    transparent = m.transparent, cutoff = m.cutoff, cull = m.cull
                }).ToArray()
            }).ToArray()
        };
        public static bool Equal(Layout a, Layout b)
        {
            if (a == null || b == null) return a == b;
            if (a.version != b.version || a.name != b.name || a.room.width != b.room.width ||
                a.room.depth != b.room.depth || a.room.wallHeight != b.room.wallHeight || a.room.grid != b.room.grid ||
                a.instances.Count != b.instances.Count) return false;
            for (int n = 0; n < a.instances.Count; n++)
            {
                var x = a.instances[n]; var y = b.instances[n];
                if (x.instanceId != y.instanceId || x.furnitureId != y.furnitureId || x.quarterTurns != y.quarterTurns ||
                    x.position.x != y.position.x || x.position.y != y.position.y || x.position.z != y.position.z) return false;
            }
            return true;
        }
    }
}
