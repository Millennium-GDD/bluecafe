using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlueCafe.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlueCafe.Unity
{
    public sealed partial class RuntimeView
    {
        const int MaxFloorBlocks = 8192;
        const int MaxFloorBlockVertices = 65536;
        readonly Dictionary<GameObject, List<Mesh>> floorMeshes = new Dictionary<GameObject, List<Mesh>>();
        static long FittedBytes(Mesh mesh) => 2 * (mesh.vertexCount*48L + Enumerable.Range(0,mesh.subMeshCount).Sum(i=>(long)mesh.GetIndexCount(i)*4));
        void ReserveSurface(long bytes)
        {
            if(bytes < 0 || bytes > renderBudget - resources.Bytes - fittedBytes)
                throw new InvalidDataException("RESOURCE_BUDGET: fitted original surfaces exceed runtime budget.");
        }
        void ReleaseFitted(GameObject go)
        {
            foreach(var module in wallModules.Keys.Where(t=>!t||t.IsChildOf(go.transform)).ToArray())wallModules.Remove(module);
            foreach(var pair in fittedMeshes.ToArray()) if(!pair.Key || pair.Key.transform.IsChildOf(go.transform)) {fittedBytes-=FittedBytes(pair.Value);RuntimeResources.Release(pair.Value);fittedMeshes.Remove(pair.Key);}
            foreach(var pair in floorMeshes.ToArray()) if(!pair.Key || pair.Key==go || pair.Key.transform.IsChildOf(go.transform)) {
                foreach(var mesh in pair.Value){fittedBytes-=FittedBytes(mesh);RuntimeResources.Release(mesh);}floorMeshes.Remove(pair.Key);
            }
        }
        // Classify a mesh's projected bounding rectangle. A polygon edge crossing
        // the rectangle prevents the intact fast path even for concave polygons.
        static int FloorRelation(Vector3 minimum,Vector3 maximum,Position[] polygon)
        {
            const double epsilon=.00001;
            double left=minimum.x,right=maximum.x,bottom=minimum.z,top=maximum.z;
            if(polygon.Max(p=>p.x)<left-epsilon || polygon.Min(p=>p.x)>right+epsilon || polygon.Max(p=>p.z)<bottom-epsilon || polygon.Min(p=>p.z)>top+epsilon)return -1;
            bool EdgeCrosses(Position a,Position b) {
                double from=0,to=1,dx=(double)b.x-a.x,dz=(double)b.z-a.z;
                bool Cut(double p,double q) {if(Math.Abs(p)<1e-12)return q>=0;double t=q/p;if(p<0){if(t>to)return false;from=Math.Max(from,t);}else{if(t<from)return false;to=Math.Min(to,t);}return true;}
                return left+epsilon<right-epsilon && bottom+epsilon<top-epsilon && Cut(-dx,a.x-left-epsilon)&&Cut(dx,right-epsilon-a.x)&&Cut(-dz,a.z-bottom-epsilon)&&Cut(dz,top-epsilon-a.z);
            }
            for(int i=0;i<polygon.Length;i++)if(EdgeCrosses(polygon[i],polygon[(i+1)%polygon.Length]))return 0;
            var corners=new[]{new Position((float)left,0,(float)bottom),new Position((float)right,0,(float)bottom),new Position((float)right,0,(float)top),new Position((float)left,0,(float)top)};
            int inside=corners.Count(p=>Geometry.Contains(polygon,p));
            if(inside==4)return 1;
            if(inside>0)return 0;
            return polygon.Any(p=>p.x>=left-epsilon&&p.x<=right+epsilon&&p.z>=bottom-epsilon&&p.z<=top+epsilon)?0:-1;
        }
        void FitFloor(GameObject go,Position[] polygon,IEnumerable<Position[]> exclude)
        {
            var holes=exclude.ToArray();
            foreach(var filter in go.GetComponentsInChildren<MeshFilter>()) {
                // Floor blocks bake the native rotation/scale into their mesh and
                // only translate here. Renderer.bounds recomputes center/extents,
                // inflating distant edges by float roundoff and needlessly clipping
                // entire rows. Transform the actual mesh endpoints instead.
                var minimum=filter.transform.TransformPoint(filter.sharedMesh.bounds.min);
                var maximum=filter.transform.TransformPoint(filter.sharedMesh.bounds.max);
                int relation=FloorRelation(minimum,maximum,polygon);
                var overlapping=holes.Where(p=>FloorRelation(minimum,maximum,p)!=-1).ToArray();
                if(relation==-1 || overlapping.Any(p=>FloorRelation(minimum,maximum,p)==1)){RuntimeResources.Release(filter.gameObject);continue;}
                if(relation==1 && overlapping.Length==0)continue;
                var mesh=RuntimeSurfaceClip.Clip(filter.sharedMesh,filter.transform.localToWorldMatrix,polygon,overlapping);
                try{ReserveSurface(FittedBytes(mesh));}catch{RuntimeResources.Release(mesh);throw;}
                if(mesh.vertexCount==0){RuntimeResources.Release(mesh);RuntimeResources.Release(filter.gameObject);continue;}
                filter.sharedMesh=mesh;fittedMeshes.Add(filter,mesh);fittedBytes+=FittedBytes(mesh);
            }
            // Edge-only shapes may never use an intact prototype after clipping.
            var used=new HashSet<Mesh>(go.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh));
            if(floorMeshes.TryGetValue(go,out var prototypes))foreach(var mesh in prototypes.ToArray())if(!used.Contains(mesh)){
                fittedBytes-=FittedBytes(mesh);RuntimeResources.Release(mesh);prototypes.Remove(mesh);
            }
        }
        Mesh RepeatFloorMesh(Mesh source,Matrix4x4 part,int nx,int nz,float spanX,float spanZ)
        {
            int copies=checked(nx*nz),count=checked(source.vertexCount*copies);
            long indices=Enumerable.Range(0,source.subMeshCount).Sum(i=>(long)source.GetIndexCount(i));
            ReserveSurface(2*(count*48L+indices*copies*4));
            var vertices=source.vertices;var normals=source.normals;var uv=source.uv;var colors=source.colors;
            var outV=new Vector3[count];var outN=new Vector3[count];var outUv=new Vector2[count];var outC=new Color[count];
            var normalMatrix=part.inverse.transpose;
            for(int x=0;x<nx;x++)for(int z=0;z<nz;z++){
                int start=(x*nz+z)*vertices.Length;var offset=new Vector3(x*spanX,0,z*spanZ);
                for(int i=0;i<vertices.Length;i++){
                    outV[start+i]=part.MultiplyPoint3x4(vertices[i])+offset;
                    outN[start+i]=normalMatrix.MultiplyVector(normals.Length>i?normals[i]:Vector3.up).normalized;
                    outUv[start+i]=uv.Length>i?uv[i]:Vector2.zero;outC[start+i]=colors.Length>i?colors[i]:Color.white;
                }
            }
            var mesh=new Mesh{name=source.name+" [repeated original floor]",hideFlags=HideFlags.DontSave,indexFormat=count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
            try {
                mesh.vertices=outV;mesh.normals=outN;mesh.uv=outUv;mesh.colors=outC;mesh.subMeshCount=source.subMeshCount;
                for(int sub=0;sub<source.subMeshCount;sub++){
                    var triangles=source.GetTriangles(sub);var repeated=new int[checked(triangles.Length*copies)];
                    for(int n=0;n<copies;n++)for(int i=0;i<triangles.Length;i++)repeated[n*triangles.Length+i]=triangles[i]+n*vertices.Length;
                    mesh.SetTriangles(repeated,sub,false);
                }
                mesh.RecalculateBounds();return mesh;
            } catch {RuntimeResources.Release(mesh);throw;}
        }
        void AssembleFloorBlocks(Item item,GameObject group,int nx,int nz,float spanX,float spanZ,float startX,float startZ)
        {
            if(item.parts==null || item.parts.Length==0)throw new InvalidDataException("MODEL_UNAVAILABLE: no prepared mesh parts for "+item.id);
            var prototypes=new List<Mesh>();floorMeshes.Add(group,prototypes);int blocks=0;
            foreach(var part in item.parts){
                if(string.IsNullOrEmpty(part.meshJsonPath))throw new InvalidDataException("MODEL_UNAVAILABLE: part has no prepared mesh JSON for "+item.id);
                var source=resources.Mesh(part.meshJsonPath);
                if(part.materials==null || part.materials.Length==0)throw new InvalidDataException("MATERIAL_UNAVAILABLE: original material array is missing for "+item.id);
                var materials=part.materials.Select(spec=>resources.Material(spec,item)).ToArray();ReserveSurface(0);
                int side=Math.Max(1,Math.Min(32,Mathf.FloorToInt(Mathf.Sqrt((float)MaxFloorBlockVertices/Math.Max(1,source.vertexCount)))));
                long required=(long)((nx+side-1)/side)*((nz+side-1)/side);
                if(required>MaxFloorBlocks-blocks)throw new InvalidDataException("SURFACE_BUDGET: original floor exceeds the bounded block budget.");blocks+=(int)required;
                var rotation=part.rotation?.Length==4?new Quaternion(part.rotation[0],part.rotation[1],part.rotation[2],part.rotation[3]):Quaternion.identity;
                var transform=Matrix4x4.Rotate(Quaternion.Euler(0,90,0))*Matrix4x4.TRS(V3(part.position,Vector3.zero),rotation,V3(part.scale,Vector3.one));
                var variants=new Dictionary<Vector2Int,Mesh>();
                for(int x=0;x<nx;x+=side)for(int z=0;z<nz;z+=side){
                    var size=new Vector2Int(Math.Min(side,nx-x),Math.Min(side,nz-z));
                    if(!variants.TryGetValue(size,out var mesh)){mesh=RepeatFloorMesh(source,transform,size.x,size.y,spanX,spanZ);variants.Add(size,mesh);prototypes.Add(mesh);fittedBytes+=FittedBytes(mesh);}
                    var child=NewObject(source.name+" [floor block]",group.transform);child.transform.localPosition=new Vector3(startX+x*spanX,0,startZ+z*spanZ);
                    child.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=child.AddComponent<MeshRenderer>();renderer.sharedMaterials=materials;
                    if(RuntimeSurfaceRules.IsFramingHelper(item,part))framingHelpers.Add(renderer);
                }
            }
        }
        void AssembleRoomStyle(Item item, Transform parent, Area area = null)
        {
            var group = parent.gameObject;var unit = pack.Catalog.gridUnit;
            if (item.placement == "flooring")
            {
                bool hasBounds = item.bounds?.min != null && item.bounds.max != null;
                float spanX = hasBounds ? item.bounds.max[2] - item.bounds.min[2] : unit;
                float spanZ = hasBounds ? item.bounds.max[0] - item.bounds.min[0] : unit;
                spanX = Mathf.Max(unit, Mathf.Round(spanX / unit) * unit);spanZ = Mathf.Max(unit, Mathf.Round(spanZ / unit) * unit);
                float offsetX = hasBounds ? -item.bounds.min[2] : unit / 2;float offsetZ = hasBounds ? item.bounds.max[0] : unit / 2;
                var polygon=area?.vertices??Geometry.Extent(room);
                float minX=polygon.Min(p=>p.x),maxX=polygon.Max(p=>p.x),minZ=polygon.Min(p=>p.z),maxZ=polygon.Max(p=>p.z);
                int nx = Mathf.CeilToInt((maxX-minX) / spanX), nz = Mathf.CeilToInt((maxZ-minZ) / spanZ);
                AssembleFloorBlocks(item,group,nx,nz,spanX,spanZ,minX+offsetX,minZ+offsetZ);
                // V1 retains its exact historic rendering; v2 clips original triangle/UV
                // data at partial modules, diagonals and concave corners.
                if(geometryLayout?.version==2) FitFloor(group,polygon,area==null?(geometryLayout.areas??new List<Area>()).Where(a=>!string.IsNullOrEmpty(a.flooringId)).Select(a=>a.vertices):Enumerable.Empty<Position[]>());
            }
            else if(geometryLayout?.version==2) AssembleWalls(item,parent,area);
            else AssembleLegacyWalls(item,parent);
        }
        void AssembleLegacyWalls(Item item,Transform parent)
        {
            float unit=pack.Catalog.gridUnit;
            float span=item.bounds?.min!=null&&item.bounds.max!=null?item.bounds.max[2]-item.bounds.min[2]:unit*2;
            span=Mathf.Max(unit,Mathf.Round(span/unit)*unit);
            float start=item.bounds?.min!=null?-item.bounds.min[2]:unit/2;
            if(Mathf.CeilToInt(room.width/span)+Mathf.CeilToInt(room.depth/span)>MaxModulesPerStyle)throw new InvalidDataException("SURFACE_BUDGET: room requires too many original wall modules.");
            for(int side=0;side<2;side++) {float length=side==0?room.depth:room.width;
                for(int n=0;n<Mathf.CeilToInt(length/span);n++) {var wall=NewObject(item.name,parent);Assemble(item,wall.transform);wall.transform.localPosition=side==0?new Vector3(-room.width/2,0,-room.depth/2+start+n*span):new Vector3(-room.width/2+start+n*span,0,room.depth/2);wall.transform.localRotation=Quaternion.Euler(0,side*90,0);}
            }
        }
        void AssembleWalls(Item defaultItem,Transform parent,Area scope)
        {
            int modules=0;var ids=geometryLayout?.version==2?Geometry.WallIds:new[]{"left","back"};
            foreach(var id in ids) {
                float length=Geometry.WallLength(room,id);var cuts=new List<float>{0,length};
                foreach(var a in geometryLayout?.areas??new List<Area>()) foreach(var v in a.vertices) {var along=Geometry.Along(room,id,v);if(along>0&&along<length)cuts.Add(along);}
                foreach(var o in geometryLayout?.openings??new List<WallOpening>()) if(o.wallId==id){cuts.Add(o.along-o.width/2);cuts.Add(o.along+o.width/2);}
                cuts=cuts.Distinct().OrderBy(x=>x).ToList();
                for(int n=0;n<cuts.Count-1;n++) {
                    float from=cuts[n],to=cuts[n+1];if(to-from<.0001f)continue;
                    var sample=Geometry.WallPosition(room,id,(from+to)/2,0);var inward=Geometry.WallTurns(id);sample.x+=(inward==0?.001f:inward==2?-.001f:0);sample.z+=(inward==1?-.001f:inward==3?.001f:0);
                    var adjacent=(geometryLayout?.areas??new List<Area>()).FirstOrDefault(a=>Geometry.Contains(a.vertices,sample));var item=defaultItem;
                    if(scope!=null && adjacent?.id!=scope.id) continue;
                    if(!string.IsNullOrEmpty(adjacent?.wallpaperId)) {
                        if(!items.TryGetValue(adjacent.wallpaperId,out item)||!pack.Availability(adjacent.wallpaperId).Available) throw new InvalidDataException("WALLPAPER_UNAVAILABLE: "+adjacent.wallpaperId);
                    }
                    var openings=(geometryLayout?.openings??new List<WallOpening>()).Where(o=>o.wallId==id && o.along-o.width/2<to-.0001f && o.along+o.width/2>from+.0001f).OrderBy(o=>o.bottom).ToArray();
                    var heights=new List<Vector2>();float bottom=0;foreach(var o in openings){if(o.bottom>bottom)heights.Add(new Vector2(bottom,o.bottom));bottom=Math.Max(bottom,o.bottom+o.height);}if(bottom<room.wallHeight)heights.Add(new Vector2(bottom,room.wallHeight));
                    foreach(var h in heights) {
                        float span=item.bounds?.min?.Length>=3&&item.bounds.max?.Length>=3?item.bounds.max[2]-item.bounds.min[2]:pack.Catalog.gridUnit*2;
                        span=Mathf.Max(pack.Catalog.gridUnit,Mathf.Round(span/pack.Catalog.gridUnit)*pack.Catalog.gridUnit);
                        float start=item.bounds?.min?.Length>=3?-item.bounds.min[2]:pack.Catalog.gridUnit/2;
                        for(float at=from;at<to-.0001f;at+=span) {
                            if(++modules>MaxModulesPerStyle)throw new InvalidDataException("SURFACE_BUDGET: too many wall modules.");
                            var wall=NewObject(item.name,parent);wallModules.Add(wall.transform,id);Assemble(item,wall.transform);
                            float width=Math.Min(span,to-at);var p=Geometry.WallPosition(room,id,at+start*(width/span),h.x);
                            wall.transform.localPosition=new Vector3(p.x,p.y,p.z);wall.transform.localRotation=Quaternion.Euler(0,Geometry.WallTurns(id)*90,0);
                            float nativeHeight=item.bounds?.min?.Length>=3&&item.bounds.max?.Length>=3?item.bounds.max[1]-item.bounds.min[1]:room.wallHeight;
                            wall.transform.localScale=new Vector3(1,(h.y-h.x)/Mathf.Max(.001f,nativeHeight),width/span);
                            if(item.bounds?.min?.Length>=3)wall.transform.localPosition-=new Vector3(0,item.bounds.min[1]*wall.transform.localScale.y,0);
                        }
                    }
                }
            }
        }
    }
}
