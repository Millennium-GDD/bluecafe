using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BlueCafe.Tests
{
    public class CafeReferencePlanTests
    {
        CafeCatalog previousCatalog;
        FieldInfo catalogField;
        CafeLayout layout;
        CafeReferencePlan plan;
        CafeOrientationAnnotations annotations;
        string meshPath;

        [SetUp] public void SetUp()
        {
            previousCatalog=CafeApi.Catalog;
            meshPath=Path.GetTempFileName();
            File.WriteAllText(meshPath,JsonUtility.ToJson(new CafeMeshData {name="Reference test triangle",vertices=new[]{0f,0f,0f, .1f,0f,0f, 0f,.1f,.1f},submeshes=new[]{new CafeSubmesh {triangles=new[]{0,1,2}}}}));
            var parts=new[]{new CafePart {meshJsonPath=meshPath}};
            catalogField=typeof(CafeApi).GetField("catalog",BindingFlags.Static|BindingFlags.NonPublic);
            catalogField.SetValue(null,new CafeCatalog {items=new[] {
                new CafeItem {id="desk",name="Composite desk",sourcePrefab="desk.prefab",placement="floor",available=true,parts=parts},
                new CafeItem {id="board",name="Board",sourcePrefab="board.prefab",placement="wall",available=true,parts=parts}
            }});
            layout=new CafeLayout {room=new CafeRoom {width=6,depth=6,grid=.3f},instances={
                new CafeInstance {instanceId="student",furnitureId="desk",quarterTurns=3},
                new CafeInstance {instanceId="board-instance",furnitureId="board",quarterTurns=1,position=new Vector3(0,1.8f,3)}
            }};
            plan=new CafeReferencePlan {id="one-group",scope="one representative group",features=new[] {
                new CafeReferenceFeature {id="student",candidateIds=new[]{"desk"},status="matched",evidence="Original model and reference crop",contents=new[]{new CafeReferenceComponent {role="chair",countPerInstance=1,evidence="Intrinsic mesh chair"}}},
                new CafeReferenceFeature {id="board",candidateIds=new[]{"board"},status="matched",evidence="Reference blackboard"}
            },walls=new[]{new CafeReferenceWall {id="teaching-wall",mapping="z=max",status="confirmed",evidence="Declared group room axes"}},relations=new[]{
                new CafeReferenceRelation {id="student-facing",kind="faces_wall",subject="student",target="teaching-wall",directionName="seat",status="confirmed",evidence="Chair back behind desk",tolerance=.1f},
                new CafeReferenceRelation {id="board-wall",kind="on_wall",subject="board",target="teaching-wall",status="confirmed",evidence="Board on teaching wall"}
            }};
            annotations=new CafeOrientationAnnotations {items=new[]{new CafeOrientationAnnotation {furnitureId="desk",sourcePrefab="desk.prefab",directions=new[]{
                new CafeAuditedVector {name="seat",status="audited",local=Vector3.right,evidence="Original mesh backrest negative X"}
            }}}};
        }
        [TearDown] public void TearDown() { catalogField.SetValue(null,previousCatalog); File.Delete(meshPath); }
        [Test] public void CompositeCountIsSeparateAndQ3FacesBoard()
        {
            var result=CafeReconstruction.ValidateReconstruction(plan,layout,annotations);
            Assert.IsTrue(result.passed);
            Assert.AreEqual(1,result.checks.Single(c=>c.id=="student").observedCount);
            Assert.AreEqual(1,result.checks.Single(c=>c.code=="COMPOSITE_CONTENT_COUNT").observedCount);
            Assert.AreEqual("unverified",result.visualReview);
            Assert.AreEqual(2,layout.instances.Count);
        }
        [Test] public void LegalButWrongQuarterTurnFailsAuditedFacing()
        {
            layout.instances[0].quarterTurns=0;
            CafeApi.ValidateLayout(layout);
            var result=CafeReconstruction.ValidateReconstruction(plan,layout,annotations);
            Assert.IsFalse(result.passed);
            Assert.AreEqual("RELATION_VIOLATION",result.checks.Single(c=>c.id=="student-facing").code);
            Assert.AreEqual(90,result.checks.Single(c=>c.id=="student-facing").measured,.001f);
        }
        [Test] public void UnknownFrontNeverBecomesNativePlusZ()
        {
            annotations.items[0].directions=Array.Empty<CafeAuditedVector>();
            var result=CafeReconstruction.ValidateReconstruction(plan,layout,annotations);
            Assert.IsFalse(result.passed);
            Assert.AreEqual("DIRECTION_UNKNOWN",result.checks.Single(c=>c.id=="student-facing").code);
            Assert.AreEqual(1,result.unknowns);
        }
        [Test] public void EverySelectedInstanceIsChecked()
        {
            plan.features[0].maximum=2;
            layout.instances.Add(new CafeInstance {instanceId="second-student",furnitureId="desk",quarterTurns=1});
            var result=CafeReconstruction.ValidateReconstruction(plan,layout,annotations);
            Assert.AreEqual("fail",result.checks.Single(c=>c.id=="student-facing").status);
            Assert.AreEqual(2,result.checks.Single(c=>c.id=="student-facing").instanceIds.Length);
        }
        [Test] public void DeletedRenderBlockedFeatureIsStillBlockedAndMissing()
        {
            plan.features[0].status="render-blocked";
            layout.instances.RemoveAt(0);
            var result=CafeReconstruction.ValidateReconstruction(plan,layout,annotations);
            Assert.IsFalse(result.passed);
            Assert.AreEqual("COUNT_MISMATCH",result.checks.Single(c=>c.id=="student").code);
            Assert.AreEqual("blocked",result.checks.Single(c=>c.id=="student/status").status);
            Assert.AreEqual("SUBJECT_MISSING",result.checks.Single(c=>c.id=="student-facing").code);
        }
        [Test] public void WrongWallCannotPassFromNearestWallGuess()
        {
            layout.instances[1].position=new Vector3(-3,1.8f,0);
            layout.instances[1].quarterTurns=0;
            CafeApi.ValidateLayout(layout);
            var result=CafeReconstruction.ValidateReconstruction(plan,layout,annotations);
            Assert.AreEqual("fail",result.checks.Single(c=>c.id=="board-wall").status);
        }
        [Test] public void InferredWallAndZoneStayUnknown()
        {
            plan.walls[0].status="inferred";
            plan.zones=new[]{new CafeReferenceZone {id="students-zone",status="unknown"}};
            plan.relations=plan.relations.Concat(new[]{new CafeReferenceRelation {id="student-zone",subject="student",target="students-zone",kind="pivot_inside_zone",status="confirmed",evidence="Zone relation certain; extent not registered"}}).ToArray();
            var result=CafeReconstruction.ValidateReconstruction(plan,layout,annotations);
            Assert.IsFalse(result.passed);
            Assert.AreEqual("ZONE_UNCERTAIN",result.checks.Single(c=>c.id=="student-zone").code);
            Assert.AreEqual("WALL_UNCERTAIN",result.checks.Single(c=>c.id=="student-facing").code);
        }
        [Test] public void AmbiguousPairTargetNeverArbitrarilySelectsFirst()
        {
            plan.features[1].maximum=2;
            layout.instances.Add(new CafeInstance {instanceId="second-board",furnitureId="board",quarterTurns=1,position=new Vector3(1.2f,1.8f,3)});
            plan.relations[0].kind="faces_feature"; plan.relations[0].target="board";
            var result=CafeReconstruction.ValidateReconstruction(plan,layout,annotations);
            Assert.AreEqual("TARGET_AMBIGUOUS",result.checks.Single(c=>c.id=="student-facing").code);
        }
        [Test] public void StaleAuditAndInvalidSchemaDoNotPass()
        {
            annotations.items[0].sourcePrefab="old.prefab";
            var result=CafeReconstruction.ValidateReconstruction(plan,layout,annotations);
            Assert.AreEqual("STALE_ANNOTATION",result.checks.Single(c=>c.id=="student-facing").code);
            plan.features[0].maximum=-1;
            Assert.Throws<CafeValidationException>(()=>CafeReconstruction.ValidateReferencePlan(plan));
            plan.features[0].maximum=1; plan.version=2;
            Assert.Throws<CafeValidationException>(()=>CafeReconstruction.ValidateReferencePlan(plan));
        }
        [Test] public void JsonLoadPreservesIntervalsEvidenceAndUncertainty()
        {
            plan.uncertainties=new[]{"Camera not calibrated"};
            plan.features[0].maximum=2;
            var path=Path.GetTempFileName();
            try {
                File.WriteAllText(path,JsonUtility.ToJson(plan));
                var loaded=CafeReconstruction.LoadReferencePlan(path);
                Assert.AreEqual(2,loaded.features[0].maximum);
                Assert.AreEqual("Camera not calibrated",loaded.uncertainties.Single());
                Assert.AreEqual("Intrinsic mesh chair",loaded.features[0].contents.Single().evidence);
                File.WriteAllText(path,"not json");
                Assert.Throws<CafeValidationException>(()=>CafeReconstruction.LoadReferencePlan(path));
            } finally {File.Delete(path);}
        }
        [Test] public void DiagnosticValidationDoesNotMutateLayoutOrCurrentSession()
        {
            var session=CafeApi.ExportJson(); var before=JsonUtility.ToJson(layout);
            CafeReconstruction.ValidateReconstruction(plan,layout,annotations);
            Assert.AreEqual(before,JsonUtility.ToJson(layout)); Assert.AreEqual(session,CafeApi.ExportJson());
        }
        [Test] public void CatalogAvailabilityDoesNotProveRendererExists()
        {
            CafeApi.Find("desk").available=false;
            var result=CafeReconstruction.ValidateReconstruction(plan,layout,annotations);
            Assert.IsFalse(result.passed);
            Assert.AreEqual("ASSET_UNAVAILABLE",result.checks.Single(c=>c.id=="student/render/student").code);
            CafeApi.Find("desk").available=true; CafeApi.Find("desk").parts=Array.Empty<CafePart>();
            result=CafeReconstruction.ValidateReconstruction(plan,layout,annotations);
            Assert.IsFalse(result.passed);
            Assert.AreEqual("GEOMETRY_UNAVAILABLE",result.checks.Single(c=>c.id=="student/render/student").code);
        }
        [Test] public void DegenerateZoneEdgesAndZeroAreaCannotAcceptOutsidePivots()
        {
            plan.zones=new[]{new CafeReferenceZone {id="zone",status="confirmed",evidence="Test polygon",cellPolygon=new[]{Vector3.zero,Vector3.right,Vector3.forward,Vector3.zero}}};
            Assert.Throws<CafeValidationException>(()=>CafeReconstruction.ValidateReferencePlan(plan));
            plan.zones[0].cellPolygon=new[]{Vector3.zero,Vector3.right,Vector3.right*2};
            Assert.Throws<CafeValidationException>(()=>CafeReconstruction.ValidateReferencePlan(plan));
            plan.zones[0].cellPolygon=new[]{Vector3.zero,Vector3.right,Vector3.forward};
            plan.relations=plan.relations.Concat(new[]{new CafeReferenceRelation {id="in-zone",kind="pivot_inside_zone",subject="student",target="zone",status="confirmed",evidence="Bounded test polygon"}}).ToArray();
            layout.instances[0].position=new Vector3(2,0,2);
            Assert.AreEqual("fail",CafeReconstruction.ValidateReconstruction(plan,layout,annotations).checks.Single(c=>c.id=="in-zone").status);
        }
        [Test] public void OppositeSideAndBoundaryDistanceUseSourceCellsAndRejectUncertainCoordinates()
        {
            plan.boundaries=new[]{new CafeReferenceBoundary{id="divider",kind="partition",status="confirmed",evidence="Synthetic known line",cellPoints=new[]{new Vector3(0,0,-10),new Vector3(0,0,10)}}};
            plan.relations=new[]{new CafeReferenceRelation{id="sides",kind="pivot_opposite_sides",subject="student",target="board",boundary="divider",status="confirmed",evidence="Synthetic separated pivots",minimum=1,maximum=10,tolerance=0},new CafeReferenceRelation{id="distance",kind="pivot_boundary_distance",subject="student",boundary="divider",status="confirmed",evidence="One source cell clearance",minimum=1,maximum=1,tolerance=.001f}};
            layout.instances[0].position=new Vector3(-.6f,0,0);layout.instances[1].position=new Vector3(.6f,0,0);
            Assert.IsTrue(CafeReconstruction.ValidateReconstruction(plan,layout,annotations).passed);
            layout.instances[1].position=new Vector3(-.6f,0,0);
            Assert.AreEqual("fail",CafeReconstruction.ValidateReconstruction(plan,layout,annotations).checks.Single(c=>c.id=="sides").status);
            plan.boundaries[0].status="inferred";
            Assert.AreEqual("BOUNDARY_UNCERTAIN",CafeReconstruction.ValidateReconstruction(plan,layout,annotations).checks.Single(c=>c.id=="sides").code);
        }
        [Test] public void ParallelBoundaryRequiresAuditedAxisAndNondegenerateLine()
        {
            plan.boundaries=new[]{new CafeReferenceBoundary{id="divider",kind="partition",status="confirmed",evidence="Synthetic known line",cellPoints=new[]{Vector3.zero,Vector3.forward}}};
            plan.relations=new[]{new CafeReferenceRelation{id="parallel",kind="principal_axis_parallel_boundary",subject="student",boundary="divider",directionName="long",status="confirmed",evidence="Native longitudinal axis",tolerance=.01f}};
            annotations.items[0].principalAxes=new[]{new CafeAuditedVector{name="long",status="audited",local=Vector3.right,evidence="Synthetic axis"}};
            Assert.IsTrue(CafeReconstruction.ValidateReconstruction(plan,layout,annotations).passed);
            layout.instances[0].quarterTurns=0;
            Assert.AreEqual("fail",CafeReconstruction.ValidateReconstruction(plan,layout,annotations).checks.Single(c=>c.id=="parallel").status);
            plan.boundaries[0].cellPoints=new[]{Vector3.zero,Vector3.zero};
            Assert.Throws<CafeValidationException>(()=>CafeReconstruction.ValidateReferencePlan(plan));
        }
    }
}
