using System;
using System.Collections.Generic;
using System.Linq;
using BlueCafe.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlueCafe.Unity
{
    // Derived render meshes only: source meshes, UVs/materials and pack bytes remain immutable.
    static class RuntimeSurfaceClip
    {
        struct Vertex { public Vector3 p,n; public Vector2 uv; public Color color; public static Vertex Mix(Vertex a,Vertex b,float t)=>new Vertex{p=Vector3.LerpUnclamped(a.p,b.p,t),n=Vector3.LerpUnclamped(a.n,b.n,t),uv=Vector2.LerpUnclamped(a.uv,b.uv,t),color=Color.LerpUnclamped(a.color,b.color,t)}; }
        static List<Vertex> Half(List<Vertex> input,Position a,Position b,bool inside)
        {
            var output=new List<Vertex>();if(input.Count==0)return output;
            // Classify and intersect against the same plane. Native module bounds
            // differ from grid spans by float roundoff; a tolerated negative distance
            // cannot then be used as a zero-plane intersection (that extrapolates).
            double dx=(double)b.x-a.x,dz=(double)b.z-a.z,length=Math.Sqrt(dx*dx+dz*dz);
            float Side(Vertex v) {double distance=(dx*((double)v.p.z-a.z)-dz*((double)v.p.x-a.x))/length;return Math.Abs(distance)<.00001?0:(float)distance;}
            var previous=input[input.Count-1];float pd=Side(previous)*(inside?1:-1);
            foreach(var current in input) {float cd=Side(current)*(inside?1:-1);bool pi=pd>=0,ci=cd>=0;if(pi!=ci)output.Add(Vertex.Mix(previous,current,Mathf.Clamp01(pd/(pd-cd))));if(ci)output.Add(current);previous=current;pd=cd;}
            return output;
        }
        static List<Position[]> Triangles(Position[] p) {var t=Geometry.Triangulate(p);var result=new List<Position[]>();for(int i=0;i<t.Length;i+=3)result.Add(new[]{p[t[i]],p[t[i+1]],p[t[i+2]]});return result;}
        public static Mesh Clip(Mesh source,Matrix4x4 toWorld,Position[] include,IEnumerable<Position[]> exclude)
        {
            var includes=Triangles(include);var excludes=exclude.SelectMany(Triangles).ToArray();var vertices=source.vertices;var normals=source.normals;var uv=source.uv;var colors=source.colors;
            var outV=new List<Vector3>();var outN=new List<Vector3>();var outUv=new List<Vector2>();var outC=new List<Color>();var subs=new List<int[]>();var inverse=toWorld.inverse;
            Vertex Read(int i)=>new Vertex{p=toWorld.MultiplyPoint3x4(vertices[i]),n=normals.Length>i?normals[i]:Vector3.up,uv=uv.Length>i?uv[i]:Vector2.zero,color=colors.Length>i?colors[i]:Color.white};
            for(int sub=0;sub<source.subMeshCount;sub++) {
                var output=new List<int>();var tris=source.GetTriangles(sub);
                for(int i=0;i<tris.Length;i+=3) foreach(var bound in includes) {
                    var poly=new List<Vertex>{Read(tris[i]),Read(tris[i+1]),Read(tris[i+2])};for(int e=0;e<3;e++)poly=Half(poly,bound[e],bound[(e+1)%3],true);
                    var pieces=new List<List<Vertex>>();if(poly.Count>=3)pieces.Add(poly);
                    foreach(var hole in excludes) {var remaining=new List<List<Vertex>>();foreach(var piece in pieces){var inside=piece;for(int e=0;e<3;e++){var outside=Half(inside,hole[e],hole[(e+1)%3],false);if(outside.Count>=3)remaining.Add(outside);inside=Half(inside,hole[e],hole[(e+1)%3],true);if(inside.Count<3)break;}}pieces=remaining;}
                    foreach(var piece in pieces) for(int t=1;t<piece.Count-1;t++) {foreach(var v in new[]{piece[0],piece[t],piece[t+1]}) {output.Add(outV.Count);outV.Add(inverse.MultiplyPoint3x4(v.p));outN.Add(v.n.normalized);outUv.Add(v.uv);outC.Add(v.color);}if(outV.Count>2000000)throw new InvalidOperationException("SURFACE_BUDGET: clipped surface exceeds vertex budget.");}
                }
                subs.Add(output.ToArray());
            }
            var mesh=new Mesh{name=source.name+" [fitted]",hideFlags=HideFlags.DontSave,indexFormat=outV.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};mesh.SetVertices(outV);mesh.SetNormals(outN);mesh.SetUVs(0,outUv);mesh.SetColors(outC);mesh.subMeshCount=subs.Count;for(int s=0;s<subs.Count;s++)mesh.SetTriangles(subs[s],s);mesh.RecalculateBounds();return mesh;
        }
    }
}
