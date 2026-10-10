using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace BlueCafe
{
    // Diagnostic requirements, deliberately separate from CafeLayout's creative rules.
    [Serializable] public class CafeReferencePlan
    {
        public int version = 1;
        public string id, referenceImage, scope, evidence, cameraStatus = "unregistered";
        public float sourceCellUnit = .6f;
        public CafeReferenceFeature[] features = Array.Empty<CafeReferenceFeature>();
        public CafeReferenceWall[] walls = Array.Empty<CafeReferenceWall>();
        public CafeReferenceBoundary[] boundaries = Array.Empty<CafeReferenceBoundary>();
        public CafeReferenceZone[] zones = Array.Empty<CafeReferenceZone>();
        public CafeReferenceRelation[] relations = Array.Empty<CafeReferenceRelation>();
        public string[] uncertainties = Array.Empty<string>();
    }
    [Serializable] public class CafeReferenceBox
    {
        // Normalized image coordinates, origin at top left; zero box with known=false is unknown.
        public bool known;
        public float x, y, width, height;
    }
    [Serializable] public class CafeReferenceComponent
    {
        public string role, evidence;
        public int countPerInstance;
    }
    [Serializable] public class CafeReferenceFeature
    {
        public string id, label, status = "uncertain", confidence = "unknown", evidence;
        public bool inScope = true, required = true, prominent = true;
        public int minimum = 1, maximum = 1;
        public string[] candidateIds = Array.Empty<string>(), instanceIds = Array.Empty<string>();
        public CafeReferenceComponent[] contents = Array.Empty<CafeReferenceComponent>();
        public CafeReferenceBox imageBox = new CafeReferenceBox();
    }
    [Serializable] public class CafeReferenceWall
    {
        public string id, mapping, status = "unknown", confidence = "unknown", evidence;
        // Mapping is x=min or z=max only when established. Unknown does not imply a room corner.
    }
    [Serializable] public class CafeReferenceBoundary
    {
        public string id, kind, status = "unknown", confidence = "unknown", evidence;
        public Vector3[] cellPoints = Array.Empty<Vector3>();
        public CafeReferenceOpening[] openings = Array.Empty<CafeReferenceOpening>();
    }
    [Serializable] public class CafeReferenceOpening
    {
        public string id, status = "unknown", evidence;
        public Vector3 startCell, endCell;
    }
    [Serializable] public class CafeReferenceZone
    {
        public string id, status = "unknown", confidence = "unknown", evidence;
        public Vector3[] cellPolygon = Array.Empty<Vector3>();
    }
    [Serializable] public class CafeReferenceRelation
    {
        public string id, kind, subject, target, directionName = "front", axis, boundary, status = "unknown", confidence = "unknown", evidence;
        public bool required = true;
        public float tolerance = .01f, minimum, maximum;
        public Vector3 worldDirection;
    }
    [Serializable] public class CafeReconstructionCheck
    {
        public string id, status, code, message, evidence;
        public int observedCount;
        public bool hasMeasurement;
        public float measured;
        public string[] instanceIds = Array.Empty<string>();
    }
    [Serializable] public class CafeReconstructionValidation
    {
        public int version = 1;
        public string planId, scope;
        public bool passed;
        public int failures, blockers, unknowns;
        public string visualReview = "unverified";
        public CafeReconstructionCheck[] checks;
    }

    public static partial class CafeReconstruction
    {
        public static CafeReferencePlan LoadReferencePlan(string path)
        {
            CafeReferencePlan plan;
            try { plan = JsonUtility.FromJson<CafeReferencePlan>(File.ReadAllText(path)); }
            catch (Exception e) { throw new CafeValidationException("INVALID_REFERENCE_PLAN", e.Message); }
            ValidateReferencePlan(plan);
            return plan;
        }

        public static void ValidateReferencePlan(CafeReferencePlan plan)
        {
            ReferenceRequire(plan != null && plan.version == 1 && !string.IsNullOrWhiteSpace(plan.id), "Expected a named version 1 plan.");
            ReferenceRequire(CafeApi.Finite(plan.sourceCellUnit) && plan.sourceCellUnit > 0, "sourceCellUnit must be positive and finite.");
            ReferenceRequire(plan.cameraStatus == "unregistered" || plan.cameraStatus == "frozen-demo" || plan.cameraStatus == "registered", "Invalid cameraStatus.");
            ReferenceRequire(plan.features != null && plan.walls != null && plan.boundaries != null && plan.zones != null && plan.relations != null && plan.uncertainties != null, "Plan arrays must be explicit.");
            var ids = new HashSet<string>();
            Action<string> unique = id => ReferenceRequire(!string.IsNullOrWhiteSpace(id) && ids.Add(id), "All feature, wall, boundary, zone and relation ids must be nonempty and unique: " + id);
            foreach (var f in plan.features)
            {
                ReferenceRequire(f != null, "Null feature."); unique(f.id);
                ReferenceRequire(new[] { "matched", "uncertain", "unavailable", "render-blocked" }.Contains(f.status), "Invalid feature status: " + f.id);
                ReferenceRequire(f.minimum >= 0 && f.maximum >= f.minimum, "Invalid quantity interval: " + f.id);
                ReferenceRequire(f.candidateIds != null && f.instanceIds != null && f.contents != null, "Feature arrays required: " + f.id);
                ReferenceRequire(f.candidateIds.All(x => !string.IsNullOrWhiteSpace(x)) && f.candidateIds.Distinct().Count() == f.candidateIds.Length &&
                    f.instanceIds.All(x => !string.IsNullOrWhiteSpace(x)) && f.instanceIds.Distinct().Count() == f.instanceIds.Length, "Selectors must be nonempty and unique: " + f.id);
                ReferenceRequire(f.status != "matched" || (f.candidateIds.Length > 0 && !string.IsNullOrWhiteSpace(f.evidence)), "Matched identity requires candidates and evidence: " + f.id);
                foreach (var c in f.contents) ReferenceRequire(c != null && !string.IsNullOrWhiteSpace(c.role) && c.countPerInstance > 0 && !string.IsNullOrWhiteSpace(c.evidence), "Composite contents require positive quantities and evidence.");
                var b = f.imageBox;
                ReferenceRequire(b != null && new[] { b.x,b.y,b.width,b.height }.All(CafeApi.Finite), "Invalid image box.");
                ReferenceRequire(!b.known || (b.x >= 0 && b.y >= 0 && b.width > 0 && b.height > 0 && b.x+b.width <= 1.00001f && b.y+b.height <= 1.00001f), "Known image boxes must fit normalized reference image.");
            }
            foreach (var w in plan.walls)
            {
                ReferenceRequire(w != null, "Null wall."); unique(w.id); ReferenceEvidence(w.status, w.evidence);
                ReferenceRequire(w.status != "confirmed" || w.mapping == "x=min" || w.mapping == "z=max", "Confirmed walls need a supported mapping.");
            }
            foreach (var b in plan.boundaries)
            {
                ReferenceRequire(b != null, "Null boundary."); unique(b.id); ReferenceEvidence(b.status, b.evidence);
                ReferenceRequire(new[] { "crop", "partition", "room-edge" }.Contains(b.kind), "Invalid boundary kind.");
                ReferencePoints(b.cellPoints); ReferenceRequire(b.status != "confirmed" || b.cellPoints.Length >= 2, "Confirmed boundary needs two points.");
                ReferenceRequire(b.openings != null, "Openings array required.");
                for(int i=1;i<b.cellPoints.Length;i++) ReferenceRequire((b.cellPoints[i]-b.cellPoints[i-1]).sqrMagnitude>.000001f,"Boundary segments must have nonzero length.");
                foreach (var o in b.openings) { ReferenceRequire(o != null && !string.IsNullOrWhiteSpace(o.id), "Invalid opening."); ReferenceEvidence(o.status, o.evidence); ReferencePoints(new[] { o.startCell,o.endCell }); }
            }
            foreach (var z in plan.zones)
            {
                ReferenceRequire(z != null, "Null zone."); unique(z.id); ReferenceEvidence(z.status,z.evidence); ReferencePoints(z.cellPolygon);
                ReferenceRequire(z.status != "confirmed" || z.cellPolygon.Length >= 3, "Confirmed zone needs a polygon.");
                if (z.status == "confirmed")
                {
                    float area = 0;
                    for (int i=0;i<z.cellPolygon.Length;i++)
                    {
                        var a=z.cellPolygon[i]; var b=z.cellPolygon[(i+1)%z.cellPolygon.Length];
                        ReferenceRequire((b-a).sqrMagnitude>.000001f,"Zone polygons must not contain duplicate adjacent/closing vertices.");
                        area+=a.x*b.z-b.x*a.z;
                    }
                    ReferenceRequire(Mathf.Abs(area)>.000001f,"Confirmed zone must have nonzero area.");
                }
            }
            foreach (var r in plan.relations)
            {
                ReferenceRequire(r != null, "Null relation."); unique(r.id); ReferenceEvidence(r.status,r.evidence);
                ReferenceRequire(plan.features.Any(f => f.id == r.subject && f.inScope), "Relation subject must be an in-scope feature: " + r.id);
                ReferenceRequire(new[] { "faces_direction", "faces_wall", "faces_feature", "on_wall", "same_wall", "principal_axis_parallel", "pivot_inside_zone", "aabb_gap", "pivot_aligned", "principal_axis_parallel_boundary", "pivot_opposite_sides", "pivot_boundary_distance" }.Contains(r.kind), "Unsupported relation kind: " + r.kind);
                ReferenceRequire(CafeApi.Finite(r.tolerance) && r.tolerance >= 0 && CafeApi.Finite(r.minimum) && CafeApi.Finite(r.maximum) && r.minimum <= r.maximum, "Invalid relation interval/tolerance.");
                ReferencePoints(new[] {r.worldDirection});
                if (r.kind == "faces_direction") ReferenceRequire(r.worldDirection.sqrMagnitude > .0001f, "Direction required.");
                if (r.kind == "faces_wall" || r.kind == "on_wall" || r.kind == "same_wall") ReferenceRequire(plan.walls.Any(w => w.id == r.target), "Relation target must name a wall.");
                if (r.kind == "pivot_inside_zone") ReferenceRequire(plan.zones.Any(z => z.id == r.target), "Relation target must name a zone.");
                if (r.kind == "faces_feature" || r.kind == "aabb_gap" || r.kind == "pivot_aligned" || r.kind == "pivot_opposite_sides") ReferenceRequire(plan.features.Any(f => f.id == r.target && f.inScope) && r.target != r.subject, "Relation target must name another in-scope feature.");
                if (new[]{"principal_axis_parallel_boundary","pivot_opposite_sides","pivot_boundary_distance"}.Contains(r.kind)) ReferenceRequire(plan.boundaries.Any(b=>b.id==r.boundary && b.kind=="partition"), "Partition boundary required.");
                if (r.kind == "aabb_gap" || r.kind == "pivot_aligned" || r.kind == "principal_axis_parallel") ReferenceRequire(r.axis == "x" || r.axis == "z", "Axis must be x or z.");
            }
        }

        // Reports semantic assertions only; call ValidateLayout separately for legality.
        // A pass is scoped to the declared requirements, never a visual fidelity claim.
        public static CafeReconstructionValidation ValidateReconstruction(CafeReferencePlan plan, CafeLayout layout = null, CafeOrientationAnnotations annotations = null)
        {
            ValidateReferencePlan(plan);
            layout = layout ?? CafeApi.Layout;
            ReferenceRequire(layout != null && layout.instances != null && layout.instances.All(i => i != null), "Layout instances required.");
            CafeApi.ValidateRoom(layout.room);
            ReferenceRequire(layout.instances.Select(i => i.instanceId).Distinct().Count() == layout.instances.Count && layout.instances.All(i => !string.IsNullOrWhiteSpace(i.instanceId) && i.quarterTurns >= 0 && i.quarterTurns <= 3 && ReferenceFinite(i.position)), "Unique instance ids and finite valid transforms required.");
            var checks = new List<CafeReconstructionCheck>();
            var selected = new Dictionary<string,CafeInstance[]>();
            foreach (var f in plan.features)
            {
                if (!f.inScope) { checks.Add(ReferenceCheck(f.id,"info","CONTEXT_ONLY",f.status + ": " + f.label,f.evidence)); continue; }
                var instances = layout.instances.Where(i => f.candidateIds.Contains(i.furnitureId) && (f.instanceIds.Length == 0 || f.instanceIds.Contains(i.instanceId))).ToArray();
                selected[f.id] = instances;
                string state = "pass", code = "COUNT_MATCH", message = instances.Length + " instances; expected " + f.minimum + ".." + f.maximum + ". Composite contents are not standalone instances.";
                if (instances.Length < f.minimum || instances.Length > f.maximum) { state = "fail"; code = "COUNT_MISMATCH"; }
                if (!f.required && state != "pass") state = "info";
                var check = ReferenceCheck(f.id,state,code,message,f.evidence);
                check.observedCount = instances.Length; check.instanceIds = instances.Select(i => i.instanceId).ToArray(); checks.Add(check);
                if (f.status != "matched") checks.Add(ReferenceCheck(f.id+"/status",f.required ? (f.status=="uncertain" ? "unknown" : "blocked") : "info",
                    "FEATURE_"+f.status.ToUpperInvariant().Replace('-','_'),f.status+"; requirement remains unresolved even when absent.",f.evidence));
                if (f.required) foreach (var instance in instances)
                {
                    var renderCheck=ReferenceCheck(f.id+"/render/"+instance.instanceId,"pass","RENDERERS_PRESENT","Renderer geometry exists; appearance still requires visual review.",f.evidence);
                    renderCheck.instanceIds=new[]{instance.instanceId};
                    try
                    {
                        var item=CafeApi.Find(instance.furnitureId);
                        if (!item.available) { renderCheck.status="blocked"; renderCheck.code="ASSET_UNAVAILABLE"; renderCheck.message="Required selected asset is unavailable."; }
                        else
                        {
                            // Geometry existence does not require inventing an orientation annotation.
                            var geometry=InspectInstance(instance.instanceId,layout,new CafeOrientationAnnotations());
                            if (!geometry.rendererWorldBounds.available || geometry.rendererCount==0) { renderCheck.status="blocked"; renderCheck.code="GEOMETRY_UNAVAILABLE"; renderCheck.message="Required selected asset has no renderer geometry."; }
                        }
                    }
                    catch(Exception e) { renderCheck.status="blocked"; renderCheck.code="GEOMETRY_INSPECTION_FAILED"; renderCheck.message=e.Message; }
                    checks.Add(renderCheck);
                }
                foreach (var c in f.contents) checks.Add(new CafeReconstructionCheck { id=f.id+"/contents/"+c.role,status="info",code="COMPOSITE_CONTENT_COUNT",observedCount=instances.Length*c.countPerInstance,evidence=c.evidence,message="Derived component tally; not independent furniture instances." });
            }
            foreach (var r in plan.relations)
            {
                var check = ReferenceCheck(r.id,"unknown","RELATION_UNRESOLVED","Unconfirmed relation remains explicit.",r.evidence);
                if (r.status == "confirmed")
                {
                    try { EvaluateReferenceRelation(plan,layout,annotations,selected,r,check); }
                    catch (CafeValidationException e) { check.status="blocked"; check.code=e.code; check.message=e.Message; }
                }
                else if (r.status == "blocked") { check.status="blocked"; check.code="RELATION_BLOCKED"; }
                if (!r.required && check.status != "pass") check.status="info";
                checks.Add(check);
            }
            return new CafeReconstructionValidation { planId=plan.id,scope=plan.scope,checks=checks.ToArray(),
                failures=checks.Count(c=>c.status=="fail"),blockers=checks.Count(c=>c.status=="blocked"),unknowns=checks.Count(c=>c.status=="unknown"),
                passed=checks.All(c=>c.status=="pass" || c.status=="info") };
        }

        static void EvaluateReferenceRelation(CafeReferencePlan plan, CafeLayout layout, CafeOrientationAnnotations annotations,
            Dictionary<string,CafeInstance[]> selected, CafeReferenceRelation r, CafeReconstructionCheck check)
        {
            var subjects = selected[r.subject];
            if (subjects.Length == 0) { check.status="blocked"; check.code="SUBJECT_MISSING"; check.message="No instances for "+r.subject; return; }
            check.instanceIds=subjects.Select(i=>i.instanceId).ToArray();
            var wall=plan.walls.FirstOrDefault(w=>w.id==r.target);
            if (wall != null && wall.status != "confirmed") { check.code="WALL_UNCERTAIN"; check.message="Named wall mapping is unconfirmed: "+wall.id; return; }
            var zone=plan.zones.FirstOrDefault(z=>z.id==r.target);
            if (zone != null && zone.status != "confirmed") { check.code="ZONE_UNCERTAIN"; check.message="Zone extent is unconfirmed: "+zone.id; return; }
            var boundary=plan.boundaries.FirstOrDefault(b=>b.id==r.boundary);
            if(boundary!=null && boundary.status!="confirmed") {check.code="BOUNDARY_UNCERTAIN";check.message="Partition coordinates remain unconfirmed.";return;}
            if(boundary!=null && (r.kind=="pivot_opposite_sides" || r.kind=="principal_axis_parallel_boundary") && boundary.cellPoints.Length!=2) {check.code="BOUNDARY_AMBIGUOUS";check.message="Signed-side and parallel checks require one directed straight segment.";return;}
            CafeInstance target=null;
            if (r.kind=="faces_feature" || r.kind=="aabb_gap" || r.kind=="pivot_aligned" || r.kind=="pivot_opposite_sides")
            {
                var targets=selected[r.target];
                if (targets.Length != 1) { check.code="TARGET_AMBIGUOUS"; check.message="Expected exactly one target instance; observed "+targets.Length; return; }
                target=targets[0];
            }
            bool passed=true;
            var measurements=new List<float>();
            foreach (var subject in subjects)
            {
                float value;
                if (r.kind=="on_wall" || r.kind=="same_wall")
                {
                    var item=CafeApi.Find(subject.furnitureId);
                    bool correctRotation=wall.mapping=="x=min" ? subject.quarterTurns==0 : subject.quarterTurns==1;
                    value=wall.mapping=="x=min" ? Mathf.Abs(subject.position.x+layout.room.width/2) : Mathf.Abs(subject.position.z-layout.room.depth/2);
                    passed &= item.placement=="wall" && correctRotation && value<=r.tolerance;
                }
                else if (r.kind=="pivot_inside_zone")
                {
                    value=ReferenceInside(subject.position/plan.sourceCellUnit,zone.cellPolygon) ? 1 : 0; passed &= value==1;
                }
                else if(r.kind=="pivot_opposite_sides")
                {
                    var a=boundary.cellPoints[0];var b=boundary.cellPoints[1];var delta=b-a;
                    float Side(Vector3 p) => (delta.x*(p.z-a.z)-delta.z*(p.x-a.x))/delta.magnitude;
                    float one=Side(subject.position/plan.sourceCellUnit),two=Side(target.position/plan.sourceCellUnit);
                    value=Mathf.Min(Mathf.Abs(one),Mathf.Abs(two));passed &= one*two<0 && value>=r.minimum-r.tolerance;
                }
                else if(r.kind=="pivot_boundary_distance")
                {
                    value=float.PositiveInfinity;var p=subject.position/plan.sourceCellUnit;p.y=0;
                    for(int i=1;i<boundary.cellPoints.Length;i++){var a=boundary.cellPoints[i-1];var delta=boundary.cellPoints[i]-a;float t=Mathf.Clamp01(Vector3.Dot(p-a,delta)/delta.sqrMagnitude);value=Mathf.Min(value,Vector3.Distance(p,a+t*delta));}
                    passed &= value>=r.minimum-r.tolerance && value<=r.maximum+r.tolerance;
                }
                else if (r.kind=="aabb_gap")
                {
                    var pair=InspectPair(subject.instanceId,target.instanceId,layout,annotations);
                    if (!pair.rendererGapsAvailable) { check.status="blocked"; check.code="GEOMETRY_UNAVAILABLE"; check.message="Renderer bounds required; footprint and pivot distances cannot substitute."; return; }
                    value=r.axis=="x" ? pair.rendererSignedEdgeGaps.x : pair.rendererSignedEdgeGaps.z;
                    passed &= value>=r.minimum-r.tolerance && value<=r.maximum+r.tolerance;
                }
                else if (r.kind=="pivot_aligned")
                {
                    value=Mathf.Abs(r.axis=="x" ? subject.position.x-target.position.x : subject.position.z-target.position.z);
                    passed &= value<=r.tolerance;
                }
                else
                {
                    var observations=annotations ?? LoadOrientations(); ValidateOrientations(observations);
                    var item=CafeApi.Find(subject.furnitureId);
                    var a=observations.items.FirstOrDefault(x=>x.furnitureId==subject.furnitureId);
                    if (a != null && !string.IsNullOrWhiteSpace(a.sourcePrefab) && a.sourcePrefab!=item.sourcePrefab) throw new CafeValidationException("STALE_ANNOTATION",subject.furnitureId);
                    var v=((r.kind=="principal_axis_parallel" || r.kind=="principal_axis_parallel_boundary") ? a?.principalAxes : a?.directions)?.FirstOrDefault(x=>x.name==r.directionName);
                    if (v==null || v.status!="audited") { check.code="DIRECTION_UNKNOWN"; check.message="No audited "+r.directionName+" for "+subject.furnitureId; return; }
                    var world=Quaternion.Euler(0,90*subject.quarterTurns,0)*v.local;
                    var desired=r.worldDirection;
                    if (r.kind=="principal_axis_parallel") desired=r.axis=="x" ? Vector3.right : Vector3.forward;
                    if (r.kind=="principal_axis_parallel_boundary") desired=boundary.cellPoints[1]-boundary.cellPoints[0];
                    if (r.kind=="faces_wall") desired=wall.mapping=="x=min" ? Vector3.left : Vector3.forward;
                    if (r.kind=="faces_feature") { desired=target.position-subject.position; desired.y=0; }
                    if (desired.sqrMagnitude<.0001f) { check.code="DIRECTION_UNACHIEVABLE"; check.message="Coincident horizontal target pivots give no facing direction."; return; }
                    value=Vector3.Angle(world,desired);
                    if (r.kind=="principal_axis_parallel" || r.kind=="principal_axis_parallel_boundary") value=Mathf.Min(value,180-value);
                    passed &= value<=r.tolerance;
                }
                measurements.Add(value);
            }
            check.status=passed ? "pass" : "fail"; check.code=passed ? "RELATION_MATCH" : "RELATION_VIOLATION";
            check.hasMeasurement=measurements.Count==1; if(check.hasMeasurement)check.measured=measurements[0];
            check.message=r.kind+" evaluated for "+subjects.Length+" instance(s); measurements ["+string.Join(", ",measurements.Select(x=>x.ToString("G7",System.Globalization.CultureInfo.InvariantCulture)))+"]. Angles are degrees; gaps/alignment use world units; containment uses pivots; boundary distance/side clearance use source cells.";
        }

        static bool ReferenceInside(Vector3 p, Vector3[] polygon)
        {
            bool inside=false;
            for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)
            {
                var a=polygon[j]; var b=polygon[i]; var ab=b-a; var ap=p-a;
                if(ab.sqrMagnitude<.000001f)continue;
                float cross=ab.x*ap.z-ab.z*ap.x;
                if (Mathf.Abs(cross)<.00001f && Vector3.Dot(ap,ab)>=0 && Vector3.Dot(ap,ab)<=ab.sqrMagnitude) return true;
                if((a.z>p.z)!=(b.z>p.z) && p.x<(b.x-a.x)*(p.z-a.z)/(b.z-a.z)+a.x)inside=!inside;
            }
            return inside;
        }
        static CafeReconstructionCheck ReferenceCheck(string id,string status,string code,string message,string evidence) => new CafeReconstructionCheck {id=id,status=status,code=code,message=message,evidence=evidence};
        static bool ReferenceFinite(Vector3 v) => CafeApi.Finite(v.x)&&CafeApi.Finite(v.y)&&CafeApi.Finite(v.z);
        static void ReferencePoints(Vector3[] points) => ReferenceRequire(points!=null && points.All(p=>ReferenceFinite(p)&&Mathf.Abs(p.y)<.00001f),"Plan coordinates must be finite XZ points with y=0.");
        static void ReferenceEvidence(string status,string evidence) => ReferenceRequire(new[]{"confirmed","inferred","unknown","blocked"}.Contains(status) && (status!="confirmed" || !string.IsNullOrWhiteSpace(evidence)),"Confirmed observations require evidence; preserve inferred/unknown/blocked states.");
        static void ReferenceRequire(bool ok,string message) { if(!ok)throw new CafeValidationException("INVALID_REFERENCE_PLAN",message); }
    }
}
