using System.IO;
using BlueCafe.Core;
using UnityEngine;

namespace BlueCafe.Unity
{
    public sealed partial class RuntimeView
    {
        void AssembleRoomStyle(Item item, Transform parent)
        {
            var group = parent.gameObject;
            var unit = pack.Catalog.gridUnit;
            if (item.placement == "flooring")
            {
                // Modules can cover multiple grid cells (Abydos uses 2x2). Rotate the
                // native two-sided skirt toward the open edges and tile by its bounds.
                bool hasBounds = item.bounds?.min != null && item.bounds.max != null;
                float spanX = hasBounds ? item.bounds.max[2] - item.bounds.min[2] : unit;
                float spanZ = hasBounds ? item.bounds.max[0] - item.bounds.min[0] : unit;
                spanX = Mathf.Max(unit, Mathf.Round(spanX / unit) * unit);
                spanZ = Mathf.Max(unit, Mathf.Round(spanZ / unit) * unit);
                float offsetX = hasBounds ? -item.bounds.min[2] : unit / 2;
                float offsetZ = hasBounds ? item.bounds.max[0] : unit / 2;
                int nx = Mathf.CeilToInt(room.width / spanX), nz = Mathf.CeilToInt(room.depth / spanZ);
                if ((long)nx * nz > MaxModulesPerStyle) throw new InvalidDataException("SURFACE_BUDGET: room requires too many original floor modules.");
                for (int x = 0; x < nx; x++) for (int z = 0; z < nz; z++)
                {
                    var tile = NewObject(item.name, group.transform); Assemble(item, tile.transform); tile.transform.localRotation = Quaternion.Euler(0, 90, 0); tile.transform.localPosition = new Vector3(-room.width / 2 + offsetX + spanX * x, 0, -room.depth / 2 + offsetZ + spanZ * z);
                }
            }
            else
            {
                float span = item.bounds != null && item.bounds.max != null ? item.bounds.max[2] - item.bounds.min[2] : unit * 2;
                // Source float bounds can be microscopically shorter than a whole
                // grid module; ceil(length / span) must not duplicate a 12m wall.
                span = Mathf.Max(unit, Mathf.Round(span / unit) * unit);
                float start = item.bounds != null && item.bounds.min != null ? -item.bounds.min[2] : unit / 2;
                if (Mathf.CeilToInt(room.width / span) + Mathf.CeilToInt(room.depth / span) > MaxModulesPerStyle) throw new InvalidDataException("SURFACE_BUDGET: room requires too many original wall modules.");
                for (int side = 0; side < 2; side++)
                {
                    float length = side == 0 ? room.depth : room.width;
                    for (int i = 0; i < Mathf.CeilToInt(length / span); i++)
                    {
                        var wall = NewObject(item.name, group.transform); Assemble(item, wall.transform);
                        wall.transform.localPosition = side == 0 ? new Vector3(-room.width / 2, 0, -room.depth / 2 + start + i * span) : new Vector3(-room.width / 2 + start + i * span, 0, room.depth / 2);
                        wall.transform.localRotation = Quaternion.Euler(0, side * 90, 0);
                    }
                }
            }

        }
    }
}
