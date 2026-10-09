# BlueCafe editor API

The Designer (Window → BlueCafe → Designer) and scripted authoring use `BlueCafe.CafeApi`.
Public static methods work in Unity Pipeline `eval` without a Pipeline code dependency.
All positions and room dimensions are in native Unity world units. Source furniture
footprint cells are converted by `catalog.gridUnit` (currently 0.6). The API default room
18 × 18 is 30 × 30 source cells. The Designer New button and reviewed examples
use 12 × 12 (20 × 20 cells), inferred from preview proportions; exact source room
metadata remains unverified.

```csharp
BlueCafe.CafeApi.NewLayout("My cafe", 12, 12, .6f);
BlueCafe.CafeApi.Place("1200", 0, 0); // original Momo floor tiles fill room
BlueCafe.CafeApi.Place("1300", 0, 0); // original Momo wallpaper modules fill rear walls
var id = BlueCafe.CafeApi.Place("1001", 0, 0); // use InspectCatalog to select exact ids
BlueCafe.CafeApi.Move(id, 1.2f, 0);
BlueCafe.CafeApi.Rotate(id);
BlueCafe.CafeApi.Duplicate(id);
BlueCafe.CafeApi.Save("evidence/my-cafe.json");
BlueCafe.CafeApi.Capture("evidence/my-cafe.png", 1600, 1200);
BlueCafe.CafeApi.ExportScene("Assets/BlueCafe/Exports/MyCafe.unity");
```

Inspect `CafeApi.InspectCatalog(query, category, theme)`, `CafeApi.ExportJson()`,
`CafeApi.Status()` or `CafeApi.Layout`. `Place` returns a stable instance id.
`Load`, `ImportJson`, `Delete`, `UndoLast`, `RedoLast`, `ResizeRoom` are also public.
Validation errors throw `CafeValidationException` with a stable `code` and text.
Imports validate all ids/transforms before altering the current layout.

Floor furniture snaps on X/Z and must fit in the room. Overlap is allowed for
creative arrangements and rugs. Wall items snap to the nearest of the two rear
walls, face inward and use wall-local horizontal/vertical placement; optional
`elevation` sets their center height. Default elevation centers on the wall and
grid. Original wall content is vertically centered under this placement anchor,
because source prefabs mix center and floor pivots. Declared source footprint
controls validation; source geometry can extend slightly beyond that footprint. `Rotate` transfers a wall piece to the adjacent wall, preserving height.
Flooring and wallpaper are singleton style instances anchored at origin, rendered
by repeating the actual acquired floor/wall modules. Their rotation is fixed.
Unavailable models are searchable but placement fails explicitly.

In the Designer, drag cards into the viewport, or click a card then the floor.
Select and drag furniture to move; use R to rotate, Ctrl+D to duplicate, Delete
to remove, Ctrl+Z / Ctrl+Shift+Z to undo/redo. Selection coordinates allow precise
placement and wall elevation. Scroll zooms; right-drag pans; Frame room resets.
Capture always uses fixed room framing, independent of interactive pan/zoom.
Save/load dialogs use version-1 JSON layouts. Export writes a Unity scene plus
native mesh/material resources through Unity's asset APIs and preserves the
currently active scene. There are no ownership or comfort constraints.
