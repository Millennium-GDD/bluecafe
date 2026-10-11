using System.Collections.Generic;
using UnityEngine;

namespace BlueCafe.App
{
    // Clip projected geometry before tessellation: overflow alone does not bound
    // Painter2D strokes in every supported UI Toolkit render path.
    internal static class CanvasClip
    {
        public static bool Line(Rect rect, ref Vector2 a, ref Vector2 b)
        {
            var delta = b - a; float lo = 0, hi = 1;
            bool Edge(float p, float q) { if (Mathf.Abs(p) < .00001f) return q >= 0; float t = q / p; if (p < 0) { if (t > hi) return false; lo = Mathf.Max(lo, t); } else { if (t < lo) return false; hi = Mathf.Min(hi, t); } return true; }
            if (!Edge(-delta.x, a.x - rect.xMin) || !Edge(delta.x, rect.xMax - a.x) || !Edge(-delta.y, a.y - rect.yMin) || !Edge(delta.y, rect.yMax - a.y)) return false;
            b = a + delta * hi; a += delta * lo; return true;
        }
        public static Vector2[] Polygon(Rect rect, Vector2[] points)
        {
            var result = new List<Vector2>(points);
            for (int edge = 0; edge < 4; edge++)
            {
                var input = result; result = new List<Vector2>(); if (input.Count == 0) break;
                bool Inside(Vector2 p) => edge == 0 ? p.x >= rect.xMin : edge == 1 ? p.x <= rect.xMax : edge == 2 ? p.y >= rect.yMin : p.y <= rect.yMax;
                Vector2 Intersection(Vector2 a, Vector2 b) { float t = edge < 2 ? ((edge == 0 ? rect.xMin : rect.xMax) - a.x) / (b.x - a.x) : ((edge == 2 ? rect.yMin : rect.yMax) - a.y) / (b.y - a.y); return a + (b - a) * t; }
                var previous = input[input.Count - 1];
                foreach (var current in input) { bool currentInside = Inside(current), previousInside = Inside(previous); if (currentInside != previousInside) result.Add(Intersection(previous, current)); if (currentInside) result.Add(current); previous = current; }
            }
            return result.ToArray();
        }
    }
}
