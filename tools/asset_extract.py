#!/usr/bin/env python3
"""Local Unity bundle extraction for BlueCafe. No downloading or Editor launch.

Original geometry, textures and prefab transforms are exported using UnityPy.
"""
from __future__ import annotations
import hashlib
import json
import pathlib
import re

def dump(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + '.tmp')
    tmp.write_text(json.dumps(value, ensure_ascii=False, allow_nan=False, separators=(',', ':')), encoding='utf-8')
    tmp.replace(path)

def extract(bundle_paths, metadata, localization, output, source, unity_data=None):
    """Export original prefab geometry; unresolved references keep items unavailable."""
    CONTENT = ROOT = pathlib.Path(output)
    EVIDENCE = CONTENT / "ConversionReport"
    scope = "local"
    release = source
    import UnityPy
    from UnityPy.helpers.MeshHelper import MeshHandler
    import numpy as np
    paths=[str(p) for p in bundle_paths]
    print('Loading',len(paths),'bundles',flush=True)
    if unity_data is None:
        candidates=sorted(pathlib.Path.home().glob('Unity/Hub/Editor/*/Editor/Data'))
        unity_data=candidates[-1] if candidates else CONTENT / '__no_unity_editor__'
    builtin=pathlib.Path(unity_data)/'Resources/unity default resources'
    if builtin.exists():paths.append(str(builtin))
    env=UnityPy.load(*paths)
    print('Loaded',len(env.container),'containers',flush=True)
    prefabs={}
    for path,ptr in env.container.items():
        if path.lower().endswith('.prefab'):
            key=pathlib.Path(path).stem.lower()
            # Addressable prefabs contain complete in-game visual/collider hierarchies.
            if key not in prefabs or 'addressableasset' in path.lower(): prefabs[key]=(path,ptr)
    dump(EVIDENCE/'prefab-inventory.json',sorted(prefabs))
    for raw in metadata.values():
        icon=raw['Icon'].lower()
        if icon not in prefabs and raw['IsReleased'][1]:
            candidates=[k for k in prefabs if icon.replace('_1x20','') in k]
            print('Unmatched prefab',icon,'candidates',candidates,flush=True)
    used_bundles=set()
    issues=[]; mesh_cache={}; texture_cache={}; material_cache={}; material_sources={}; stats={'meshCount':0,'textureCount':0,'vertexCount':0,'triangleCount':0}
    def key(ptr):
        resolved=ptr.deref()
        used_bundles.add(pathlib.Path(str(getattr(resolved.assets_file.parent,'name',''))).name)
        return str(resolved.assets_file.name)+':'+str(resolved.path_id)
    def assetname(obj,prefix):
        objkey=str(obj.object_reader.assets_file.name)+':'+str(obj.object_reader.path_id)
        return re.sub(r'[^a-zA-Z0-9_-]','_',getattr(obj,'m_Name',prefix))[:100]+'_'+hashlib.sha256(objkey.encode()).hexdigest()[:12]
    def rel(path): return path.relative_to(ROOT).as_posix()
    def vector(v, n=3): return [float(getattr(v,c)) for c in 'xyzw'[:n]]
    def export_texture(ptr):
        if not ptr.path_id:return ''
        k=key(ptr)
        if k in texture_cache:return texture_cache[k]
        data=ptr.read();path=CONTENT/'Textures'/(assetname(data,'texture')+'.png')
        path.parent.mkdir(exist_ok=True,parents=True)
        data.image.save(path)
        texture_cache[k]=rel(path);stats['textureCount']+=1
        return rel(path)
    def export_material(ptr):
        if not ptr.path_id:return {'name':'Missing material','texturePath':'','color':[1,1,1,1]}
        k=key(ptr)
        if k in material_cache:
            used_bundles.update(material_sources[k])
            return material_cache[k]
        data=ptr.read(); props=data.m_SavedProperties
        texs=dict(props.m_TexEnvs);colors=dict(props.m_Colors);floats=dict(props.m_Floats)
        texture=texs.get('_MainTex') or texs.get('_BaseMap')
        color=colors.get('_Color') or colors.get('_BaseColor')
        result={'name':data.m_Name,'texturePath':export_texture(texture.m_Texture) if texture else '',
            'color':[float(getattr(color,c)) for c in 'rgba'] if color else [1,1,1,1],
            'textureScale':vector(texture.m_Scale,2) if texture else [1,1],
            'textureOffset':vector(texture.m_Offset,2) if texture else [0,0],
            'transparent':floats.get('_ZWrite',1)==0 or floats.get('_DstBlend',0)!=0,
            'cutoff':floats.get('_Cutoff',0),'cull':floats.get('_Cull',2),
            'sourceShader':str(data.m_Shader.path_id)}
        material_sources[k]={pathlib.Path(str(ptr.deref().assets_file.parent.name)).name}
        if texture and texture.m_Texture.path_id:
            material_sources[k].add(pathlib.Path(str(texture.m_Texture.deref().assets_file.parent.name)).name)
        used_bundles.update(material_sources[k])
        material_cache[k]=result
        return result
    def export_mesh(ptr):
        k=key(ptr)
        if k in mesh_cache:return mesh_cache[k]
        data=ptr.read();handler=MeshHandler(data);handler.process()
        vertices=np.array(handler.m_Vertices,dtype=float)[:,:3]
        if not np.isfinite(vertices).all():raise ValueError('Nonfinite mesh vertices')
        submeshes=[{'triangles':[int(i) for tri in tris for i in tri]} for tris in handler.get_triangles()]
        path=CONTENT/'Meshes'/(assetname(data,'mesh')+'.mesh.json')
        result={'name':data.m_Name,'vertices':vertices.ravel().tolist(),
            'normals':[float(x) for row in (handler.m_Normals or []) for x in row[:3]],
            'uv':[float(x) for row in (handler.m_UV0 or []) for x in row[:2]],'submeshes':submeshes}
        dump(path,result)
        stats['meshCount']+=1;stats['vertexCount']+=len(vertices)
        stats['triangleCount']+=sum(len(x['triangles'])//3 for x in submeshes)
        mesh_cache[k]=(rel(path),vertices)
        return mesh_cache[k]
    def trs(t):
        p=vector(t.m_LocalPosition);q=np.array(vector(t.m_LocalRotation,4));s=vector(t.m_LocalScale)
        if not np.isfinite(q).all() or np.linalg.norm(q)==0:raise ValueError('Invalid source rotation')
        q=q/np.linalg.norm(q);x,y,z,w=q
        m=np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w),p[0]],
                    [2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w),p[1]],
                    [2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y),p[2]], [0,0,0,1]],dtype=float)
        m[:3,:3]=m[:3,:3]@np.diag(s);return m
    def decompose(m):
        if not np.isfinite(m).all():raise ValueError('Nonfinite source transform')
        s=np.linalg.norm(m[:3,:3],axis=0)
        if np.any(s==0):raise ValueError('Degenerate source transform')
        r=m[:3,:3]/s
        # Stable rotation matrix to quaternion, including 180-degree cases.
        vals=[1+r[0,0]-r[1,1]-r[2,2],1-r[0,0]+r[1,1]-r[2,2],1-r[0,0]-r[1,1]+r[2,2],1+np.trace(r)]
        i=int(np.argmax(vals));q=np.zeros(4);q[i]=np.sqrt(max(vals[i],0))/2;t=4*q[i]
        if i==3:q[:3]=[(r[2,1]-r[1,2])/t,(r[0,2]-r[2,0])/t,(r[1,0]-r[0,1])/t]
        else:
            j=(i+1)%3;k=(i+2)%3;q[j]=(r[j,i]+r[i,j])/t;q[k]=(r[k,i]+r[i,k])/t;q[3]=(r[k,j]-r[j,k])/t
        return m[:3,3].tolist(),q.tolist(),s.tolist()
    def parts_for(ptr):
        used_bundles.clear()
        parts=[];bounds=[];seen=set();colliders=[]
        def walk(gptr,parent):
            if key(gptr) in seen:return
            seen.add(key(gptr));go=gptr.read()
            if not go.m_IsActive:return
            components={}
            for pair in go.m_Component:
                component_type=None
                try:
                    obj=pair.component.deref();component_type=obj.type.name
                    components[component_type]=pair.component.read()
                except Exception as exc:
                    if component_type is None or component_type in ('Transform','MeshRenderer','MeshFilter','SkinnedMeshRenderer'):
                        raise ValueError('Unresolved render component in '+go.m_Name+': '+str(exc)) from exc
            trans=components.get('Transform')
            if trans is None:return
            matrix=parent@trs(trans)
            renderer=components.get('MeshRenderer') or components.get('SkinnedMeshRenderer')
            meshptr=getattr(components.get('MeshFilter'),'m_Mesh',None) or getattr(renderer,'m_Mesh',None)
            if renderer and getattr(renderer,'m_Enabled',True) and meshptr and meshptr.path_id:
                meshpath,verts=export_mesh(meshptr)
                p,q,s=decompose(matrix)
                mats=[export_material(m) for m in renderer.m_Materials]
                parts.append({'name':go.m_Name,'modelPath':'','meshJsonPath':meshpath,'position':p,'rotation':q,'scale':s,'materials':mats,'skinned':type(renderer).__name__=='SkinnedMeshRenderer'})
                bounds.extend((verts@matrix[:3,:3].T+matrix[:3,3]).tolist())
            col=components.get('BoxCollider')
            if col:colliders.append({'name':go.m_Name,'size':vector(col.m_Size),'center':vector(col.m_Center)})
            for child in trans.m_Children:
                ct=child.read();walk(ct.m_GameObject,matrix)
        walk(ptr,np.eye(4))
        if bounds:
            b=np.array(bounds);minimum=b.min(axis=0).tolist();maximum=b.max(axis=0).tolist()
        else: minimum=maximum=[0,0,0]
        return parts,{'min':minimum,'max':maximum},colliders
    items=[]
    themes=dict(localization.get('FurnitureSet', {}))
    themes['0']='Default / Miscellaneous'
    for index,raw in enumerate(metadata.values()):
        icon=raw['Icon'].lower();source=prefabs.get(icon)
        alias=''
        if not source and icon=='my_event070_chair_01':
            alias='my_event070_chair';source=prefabs.get(alias)
        if not source and raw.get('SubCategory')=='Wallpaper':
            candidate=icon.replace('_1x20','_right_1x20') if icon.endswith('_1x20') else icon+'_right'
            if candidate not in prefabs and icon+'_right_1x20' in prefabs:candidate=icon+'_right_1x20'
            if candidate in prefabs:source=prefabs[candidate];alias=candidate
        sub=raw.get('SubCategory','')
        placement={'Floor':'flooring','Wallpaper':'wallpaper','WallDecoration':'wall','Background':'background'}.get(sub,'floor')
        size=raw.get('Size',[1,1]);item={'id':str(raw['Id']),'name':raw['Name'],'category':sub,'theme':themes.get(str(raw.get('SetGroupId')),str(raw.get('SetGroupId',0))),
            'themeId':raw.get('SetGroupId',0),'width':size[0],'depth':size[1], 'height':size[1] if placement in ('wall','wallpaper') else 1,
            'placement':placement,'modelPath':'','thumbnailPath':rel(CONTENT/'Thumbnails'/(str(raw['Id'])+'.png')) if (CONTENT/'Thumbnails'/(str(raw['Id'])+'.png')).exists() else '', 'icon':raw['Icon'],'available':False,'availability':'missing-source-prefab',
            'sourcePrefab':source[0] if source else '', 'sourceAlias':alias, 'parts':[], 'released':raw['IsReleased'], 'templates':raw.get('Templates',[])}
        if source:
            try:
                parts,bounds,colliders=parts_for(source[1]);item.update(parts=parts,bounds=bounds,sourceColliders=colliders,sourceBundles=sorted(used_bundles),sourceRegion=release.get('region','global'),sourceRelease=release['latest_build_version'])
                item['available']=bool(parts);item['availability']='extracted' if parts else 'no-static-geometry'
            except Exception as exc:
                item['availability']='missing-dependency';item['error']=str(exc);issues.append({'id':item['id'],'name':item['name'],'error':str(exc)})
        items.append(item)
        if index%50==0:print('Export',index,'/',len(metadata),'available',sum(x['available'] for x in items),flush=True)
    catalog={'version':1,'region':release.get('region','global'),'gameVersion':release['latest_build_version'],'patchVersion':release['patch']['patch_version'],
        'metadataSource':release['metadataSource'],'sourceCatalog':release['patch']['resource_path'],
        'gridUnit':0.6,'coordinates':'Original Unity units, Y-up. Prefab transforms retained; grid measured from native collider/footprint.',
        'items':items}
    dump(CONTENT/'catalog.json',catalog)
    report={**stats,'catalogEntries':len(items),'available':sum(x['available'] for x in items),'scope':scope,'loadedBundles':sum(p.endswith('.bundle') for p in paths),
        'globalReleased':sum(x['released'][1] for x in items),'globalAvailable':sum(x['released'][1] and x['available'] for x in items),
        'otherRegionAvailable':sum(not x['released'][1] and x['available'] for x in items),
        'builtinResource':{'path':str(builtin),'sha256':hashlib.sha256(builtin.read_bytes()).hexdigest()} if builtin.exists() else None,'issues':issues,
        'unavailable':[{'id':x['id'],'name':x['name'],'availability':x['availability']} for x in items if not x['available']]}
    dump(EVIDENCE/f'extraction-{scope}.json',report)
    print(json.dumps({k:v for k,v in report.items() if k not in ('issues','unavailable')}),flush=True)

    return report
