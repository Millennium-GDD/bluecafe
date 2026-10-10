using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BlueCafe
{
    [Serializable] public class CafeRegistrationLandmark
    {
        public string id, evidence, status = "inferred";
        public Vector3 world;
        // Full-reference, top-left pixels, before crop. Validation anchors never affect fitting.
        public Vector2 pixel;
        public float uncertaintyPixels = 5;
        public bool fit = true;
    }
    [Serializable] public class CafeRegistrationRequest
    {
        public int version = 1, width, height;
        public string id, referenceImage, hypothesis;
        public float yawMinimum = 300, yawMaximum = 340, elevationMinimum = 20, elevationMaximum = 45;
        public float maximumRmsPixels = 10;
        public CafeRegistrationLandmark[] landmarks = Array.Empty<CafeRegistrationLandmark>();
    }
    [Serializable] public class CafeRegistrationResidual
    {
        public string id, status;
        public bool fit;
        public Vector2 observed, predicted;
        public float pixels, uncertaintyUnits;
    }
    [Serializable] public class CafeRegistrationResult
    {
        public int version = 1;
        public string id, hypothesis, status, limitation = "Orthographic candidate; no perspective fit or recovered source transforms. Depth along the viewing ray is unobservable.";
        public CafeCameraProfile camera;
        public float fitRmsPixels, validationRmsPixels;
        public int validationCount;
        public bool residualThresholdPassed;
        public CafeRegistrationResidual[] residuals;
    }
    public static partial class CafeReconstruction
    {
        static void RegistrationRequire(bool value, string message)
        { if (!value) throw new CafeValidationException("INVALID_REGISTRATION", message); }

        public static Vector2 ProjectReferencePoint(CafeCameraProfile camera, Vector3 world)
        {
            ValidateCameraProfile(camera);
            RegistrationRequire(camera.projection == "orthographic", "Only orthographic landmark projection is supported.");
            var q = Quaternion.Euler(camera.elevation, camera.yaw, 0);
            var local = Quaternion.Inverse(q) * (world - camera.target);
            float scale = camera.height / (2 * camera.orthographicSize);
            return new Vector2(camera.width / 2f + scale * local.x, camera.height / 2f - scale * local.y);
        }

        public static CafeRegistrationResult FitOrthographicCamera(CafeRegistrationRequest request)
        {
            RegistrationRequire(request != null && request.version == 1 && !string.IsNullOrWhiteSpace(request.id), "Named version 1 request required.");
            RegistrationRequire(request.width >= 32 && request.width <= 4096 && request.height >= 32 && request.height <= 4096, "Image dimensions must be 32..4096.");
            RegistrationRequire(new[] {request.yawMinimum, request.yawMaximum, request.elevationMinimum, request.elevationMaximum, request.maximumRmsPixels}.All(CafeApi.Finite), "Finite search bounds required.");
            RegistrationRequire(Mathf.Abs(request.yawMinimum)<=360 && Mathf.Abs(request.yawMaximum)<=360 && request.yawMaximum >= request.yawMinimum && request.yawMaximum-request.yawMinimum <= 360 && request.elevationMinimum > -89 && request.elevationMaximum < 89 && request.elevationMaximum >= request.elevationMinimum && request.maximumRmsPixels > 0, "Invalid angle range or residual threshold.");
            RegistrationRequire(request.landmarks != null && request.landmarks.Length >= 3, "At least three landmarks required.");
            var ids = new HashSet<string>();
            foreach (var p in request.landmarks)
                RegistrationRequire(p != null && !string.IsNullOrWhiteSpace(p.id) && ids.Add(p.id) && !string.IsNullOrWhiteSpace(p.evidence) && new[]{"confirmed","inferred"}.Contains(p.status) && new[]{p.world.x,p.world.y,p.world.z,p.pixel.x,p.pixel.y,p.uncertaintyPixels}.All(CafeApi.Finite) && p.uncertaintyPixels>0 && p.pixel.x>=0 && p.pixel.x<=request.width && p.pixel.y>=0 && p.pixel.y<=request.height, "Invalid or duplicate landmark; explicit evidence and uncertainty required.");
            var points = request.landmarks.Where(p=>p.fit).ToArray();
            RegistrationRequire(points.Length >= 3, "Three fitting landmarks required independently of validation anchors.");
            var origin=points[0].world;
            RegistrationRequire(points.Any(a=>points.Any(b=>Vector3.Cross(a.world-origin,b.world-origin).sqrMagnitude>1e-8f)), "Collinear world landmarks cannot constrain camera angles.");
            CafeCameraProfile best=null; double cost=double.PositiveInfinity;
            float bestYaw=0,bestElevation=0;
            void Try(float yaw,float elevation)
            {
                var rotation=Quaternion.Euler(elevation,yaw,0);
                var right=rotation*Vector3.right; var up=rotation*Vector3.up;
                double sw=0, ax=0,ay=0,bx=0,by=0;
                foreach(var p in points) {double w=1.0/(p.uncertaintyPixels*p.uncertaintyPixels);sw+=w;ax+=w*Vector3.Dot(right,p.world);ay+=w*-Vector3.Dot(up,p.world);bx+=w*p.pixel.x;by+=w*p.pixel.y;}
                ax/=sw;ay/=sw;bx/=sw;by/=sw;
                double numerator=0,denominator=0;
                foreach(var p in points) {double w=1.0/(p.uncertaintyPixels*p.uncertaintyPixels), x=Vector3.Dot(right,p.world)-ax,y=-Vector3.Dot(up,p.world)-ay;numerator+=w*(x*(p.pixel.x-bx)+y*(p.pixel.y-by));denominator+=w*(x*x+y*y);}
                if(denominator<1e-10)return;
                double scale=numerator/denominator;if(scale<=1e-8)return;
                double tx=bx-scale*ax,ty=by-scale*ay,error=0;
                foreach(var p in points) {double x=scale*Vector3.Dot(right,p.world)+tx-p.pixel.x,y=-scale*Vector3.Dot(up,p.world)+ty-p.pixel.y;error+=(x*x+y*y)/(p.uncertaintyPixels*p.uncertaintyPixels);}
                if(error>=cost)return;
                cost=error;bestYaw=yaw;bestElevation=elevation;
                best=new CafeCameraProfile {framingSource="landmark-calibrated-candidate",yaw=yaw,elevation=elevation,width=request.width,height=request.height,orthographicSize=(float)(request.height/(2*scale)),target=right*(float)((request.width/2.0-tx)/scale)+up*(float)((ty-request.height/2.0)/scale),crop=new CafeCaptureCrop {width=request.width,height=request.height}};
            }
            // Bounded search followed by local refinement; fixed ranges also support explicitly competing hypotheses.
            for(float yaw=request.yawMinimum;yaw<=request.yawMaximum+.001f;yaw+=1)
                for(float elevation=request.elevationMinimum;elevation<=request.elevationMaximum+.001f;elevation+=1)Try(yaw,elevation);
            Try(request.yawMaximum,request.elevationMaximum);
            RegistrationRequire(best!=null,"No positive-scale camera hypothesis.");
            foreach(float step in new[]{.2f,.04f,.008f})
            {float y0=bestYaw,e0=bestElevation;for(int a=-5;a<=5;a++)for(int b=-5;b<=5;b++)Try(Mathf.Clamp(y0+a*step,request.yawMinimum,request.yawMaximum),Mathf.Clamp(e0+b*step,request.elevationMinimum,request.elevationMaximum));}
            ValidateCameraProfile(best);
            var residuals=request.landmarks.Select(p=>{var predicted=ProjectReferencePoint(best,p.world);float distance=Vector2.Distance(predicted,p.pixel);return new CafeRegistrationResidual{id=p.id,status=p.status,fit=p.fit,observed=p.pixel,predicted=predicted,pixels=distance,uncertaintyUnits=distance/p.uncertaintyPixels};}).ToArray();
            var fit=residuals.Where(r=>r.fit).ToArray();var validation=residuals.Where(r=>!r.fit).ToArray();
            float rms=Mathf.Sqrt(fit.Average(r=>r.pixels*r.pixels));
            float validationRms=validation.Length==0 ? 0 : Mathf.Sqrt(validation.Average(r=>r.pixels*r.pixels));
            bool accepted=rms<=request.maximumRmsPixels && (validation.Length==0 || validationRms<=request.maximumRmsPixels);
            return new CafeRegistrationResult{id=request.id,hypothesis=request.hypothesis,camera=best,fitRmsPixels=rms,validationRmsPixels=validationRms,validationCount=validation.Length,residualThresholdPassed=accepted,status=!accepted ? "residual-rejected" : request.landmarks.Any(p=>p.status!="confirmed") || validation.Length==0 ? "calibrated-candidate" : "landmarks-registered",residuals=residuals};
        }
    }
}
