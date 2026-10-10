using System.Linq;
using NUnit.Framework;
using UnityEngine;
namespace BlueCafe.Tests
{
    public class CafeRegistrationTests
    {
        CafeRegistrationRequest Fixture()
        {
            var camera=new CafeCameraProfile{yaw=320,elevation=32,target=new Vector3(.5f,1,-.2f),orthographicSize=5,width=1200,height=900,crop=new CafeCaptureCrop{width=1200,height=900}};
            var positions=new[]{new Vector3(-2,0,-2),new Vector3(2,0,-2),new Vector3(-2,0,2),new Vector3(1,2,1),new Vector3(0,1,0)};
            return new CafeRegistrationRequest{id="synthetic-known-camera",width=1200,height=900,maximumRmsPixels=.05f,landmarks=positions.Select((p,i)=>new CafeRegistrationLandmark{id="p"+i,world=p,pixel=CafeReconstruction.ProjectReferencePoint(camera,p),status="confirmed",evidence="Synthetic known camera",fit=i<4}).ToArray()};
        }
        [Test] public void FitsKnownCameraAndIndependentHeightLandmark()
        {
            var result=CafeReconstruction.FitOrthographicCamera(Fixture());
            Assert.That(result.camera.yaw,Is.EqualTo(320).Within(.02));Assert.That(result.camera.elevation,Is.EqualTo(32).Within(.02));
            Assert.That(result.camera.orthographicSize,Is.EqualTo(5).Within(.01));Assert.IsTrue(result.residualThresholdPassed);Assert.AreEqual("landmarks-registered",result.status);
        }
        [Test] public void InferredAnchorCannotBecomeConfirmedBySmallResidual()
        {var f=Fixture();f.landmarks[0].status="inferred";Assert.AreEqual("calibrated-candidate",CafeReconstruction.FitOrthographicCamera(f).status);}
        [Test] public void HeldOutErrorRejectsEvenAnExactTrainingFit()
        {var f=Fixture();f.landmarks[4].pixel+=new Vector2(50,20);var r=CafeReconstruction.FitOrthographicCamera(f);Assert.Less(r.fitRmsPixels,.05);Assert.AreEqual("residual-rejected",r.status);Assert.Greater(r.validationRmsPixels,50);}
        [Test] public void RejectsDegenerateAndNonfiniteEvidence()
        {var f=Fixture();foreach(var p in f.landmarks)p.world=Vector3.right;Assert.Throws<CafeValidationException>(()=>CafeReconstruction.FitOrthographicCamera(f));f=Fixture();f.landmarks[0].uncertaintyPixels=float.NaN;Assert.Throws<CafeValidationException>(()=>CafeReconstruction.FitOrthographicCamera(f));}
        [Test] public void RejectsHugeFiniteYawBeforeSearch()
        {var f=Fixture();f.yawMinimum=1e10f;f.yawMaximum=1e10f;Assert.Throws<CafeValidationException>(()=>CafeReconstruction.FitOrthographicCamera(f));}
        [Test] public void CropDoesNotShiftFullImageLandmarkCoordinates()
        {var c=new CafeCameraProfile();var p=CafeReconstruction.ProjectReferencePoint(c,Vector3.zero);c.crop=new CafeCaptureCrop{x=100,y=100,width=200,height=200};Assert.AreEqual(p,CafeReconstruction.ProjectReferencePoint(c,Vector3.zero));c.projection="perspective";Assert.Throws<CafeValidationException>(()=>CafeReconstruction.ProjectReferencePoint(c,Vector3.zero));}
    }
}
