using UnityEngine;
using UnityEngine.UIElements;

namespace BlueCafe.App
{
    internal sealed class StudioIcon : VisualElement
    {
        string kind;
        public string Kind { get => kind; set { kind = value.ToLowerInvariant(); MarkDirtyRepaint(); } }
        public StudioIcon(string description) { Kind = description; pickingMode = PickingMode.Ignore; AddToClassList("icon-child"); generateVisualContent += Draw; }
        static Vector2 V(float x, float y) => new Vector2(x, y);
        static void Line(Painter2D p, params Vector2[] points) { p.BeginPath(); p.MoveTo(points[0]); for (int i = 1; i < points.Length; i++) p.LineTo(points[i]); p.Stroke(); }
        static void Rect(Painter2D p, float x, float y, float w, float h) { Line(p,V(x,y),V(x+w,y),V(x+w,y+h),V(x,y+h),V(x,y)); }
        static void Circle(Painter2D p, float x, float y, float r) { p.BeginPath(); p.Arc(V(x,y),r,Angle.Degrees(0),Angle.Degrees(360)); p.Stroke(); }
        void Draw(MeshGenerationContext context)
        {
            if (contentRect.width < 1 || contentRect.height < 1) return;
            var p = context.painter2D; p.lineWidth = 1.6f; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round; p.strokeColor = resolvedStyle.color;
            var k = kind.Split(' ')[0];
            switch(k)
            {
                case "coffee": Rect(p,4,8,13,10); Line(p,V(17,10),V(21,10),V(21,15),V(17,15)); Line(p,V(3,21),V(20,21)); Line(p,V(8,3),V(8,5)); Line(p,V(13,2),V(13,5)); break;
                case "build": Rect(p,6,3,12,10); Rect(p,4,13,16,5); Line(p,V(6,18),V(6,22)); Line(p,V(18,18),V(18,22)); break;
                case "presets": case "grid": case "boundary": Rect(p,3,3,18,18); Line(p,V(3,12),V(21,12)); Line(p,V(12,3),V(12,21)); break;
                case "assets": case "image": Rect(p,3,4,18,16); Line(p,V(4,17),V(9,11),V(13,15),V(16,12),V(20,17)); Circle(p,16,8,1.3f); break;
                case "rooms": Line(p,V(3,7),V(12,2),V(21,7),V(12,12),V(3,7),V(3,17),V(12,22),V(21,17),V(21,7)); Line(p,V(12,12),V(12,22)); break;
                case "settings": Circle(p,12,12,3); Circle(p,12,12,7); for(int i=0;i<8;i++) { float a=i*Mathf.PI/4; Line(p,V(12+7*Mathf.Cos(a),12+7*Mathf.Sin(a)),V(12+10*Mathf.Cos(a),12+10*Mathf.Sin(a))); } break;
                case "search": Circle(p,10,10,6); Line(p,V(15,15),V(21,21)); break;
                case "question": p.BeginPath(); p.MoveTo(V(8,8)); p.BezierCurveTo(V(8,3),V(17,3),V(17,8)); p.BezierCurveTo(V(17,11),V(12,11),V(12,15)); p.Stroke(); Circle(p,12,19,.5f); break;
                case "check": Line(p,V(5,12),V(10,17),V(20,6)); break;
                case "verify": Line(p,V(12,2),V(21,6),V(20,14),V(17,19),V(12,22),V(7,19),V(4,14),V(3,6),V(12,2)); Line(p,V(8,12),V(11,15),V(17,8)); break;
                case "unsaved": Circle(p,12,12,4); break;
                case "undo": case "redo": case "rotate": { bool redo=k=="redo" || k=="rotate"; float X(float x)=>redo?24-x:x; p.BeginPath(); p.MoveTo(V(X(19),18)); p.BezierCurveTo(V(X(25),7),V(X(9),4),V(X(5),10)); p.Stroke(); Line(p,V(X(5),5),V(X(5),11),V(X(11),11)); break; }
                case "save": Rect(p,4,3,16,18); Rect(p,8,3,8,7); Rect(p,7,14,10,7); break;
                case "select": Line(p,V(5,3),V(6,20),V(11,15),V(15,21),V(18,19),V(14,13),V(21,12),V(5,3)); break;
                case "move": Line(p,V(12,2),V(12,22)); Line(p,V(2,12),V(22,12)); Line(p,V(8,6),V(12,2),V(16,6)); Line(p,V(8,18),V(12,22),V(16,18)); Line(p,V(6,8),V(2,12),V(6,16)); Line(p,V(18,8),V(22,12),V(18,16)); break;
                case "duplicate": Rect(p,8,8,13,13); Line(p,V(16,6),V(16,3),V(3,3),V(3,16),V(6,16)); break;
                case "delete": Line(p,V(4,6),V(20,6)); Line(p,V(8,6),V(8,3),V(16,3),V(16,6)); Line(p,V(6,6),V(7,21),V(17,21),V(18,6)); Line(p,V(10,10),V(10,17)); Line(p,V(14,10),V(14,17)); break;
                case "folder": Line(p,V(3,5),V(10,5),V(12,8),V(21,8),V(21,20),V(3,20),V(3,5)); break;
                case "import": case "export": Rect(p,4,14,16,7); Line(p,V(12,3),V(12,16)); if(k=="import") Line(p,V(8,12),V(12,16),V(16,12)); else Line(p,V(8,7),V(12,3),V(16,7)); break;
                case "zoom-in": case "zoom-out": Circle(p,10,10,7); Line(p,V(15,15),V(22,22)); Line(p,V(6,10),V(14,10)); if(k=="zoom-in") Line(p,V(10,6),V(10,14)); break;
                case "frame": case "resize": Line(p,V(3,9),V(3,3),V(9,3)); Line(p,V(15,3),V(21,3),V(21,9)); Line(p,V(3,15),V(3,21),V(9,21)); Line(p,V(15,21),V(21,21),V(21,15)); break;
                case "plus": Line(p,V(12,4),V(12,20)); Line(p,V(4,12),V(20,12)); break;
                case "left": Line(p,V(15,5),V(8,12),V(15,19)); break;
                case "right": Line(p,V(9,5),V(16,12),V(9,19)); break;
                case "filter": Line(p,V(3,5),V(21,5),V(14,12),V(14,20),V(10,18),V(10,12),V(3,5)); break;
                case "snap": Line(p,V(5,3),V(5,15)); p.BeginPath(); p.MoveTo(V(5,15)); p.BezierCurveTo(V(5,23),V(19,23),V(19,15)); p.LineTo(V(19,3)); p.Stroke(); Rect(p,5,3,4,5); Rect(p,15,3,4,5); break;
                case "rectangle": Rect(p,3,5,18,14); break;
                case "pen": Line(p,V(4,20),V(6,14),V(17,3),V(21,7),V(10,18),V(4,20)); break;
                case "wall": Rect(p,3,5,18,14); Line(p,V(3,12),V(21,12)); Line(p,V(9,5),V(9,12)); Line(p,V(15,12),V(15,19)); break;
                case "opening": Line(p,V(3,21),V(3,3),V(17,3),V(17,21)); Line(p,V(5,20),V(13,16),V(13,5),V(5,4),V(5,20)); break;
                case "split": case "merge": Line(p,V(3,4),V(21,4),V(21,20),V(3,20),V(3,4)); Line(p,V(12,4),V(12,20)); break;
                case "warning": Line(p,V(12,3),V(22,21),V(2,21),V(12,3)); Line(p,V(12,8),V(12,14)); Circle(p,12,18,.5f); break;
                default: Line(p,V(6,6),V(18,18)); Line(p,V(18,6),V(6,18)); break;
            }
        }
    }
}
