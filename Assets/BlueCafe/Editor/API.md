# BlueCafe editor API

The standalone runtime uses `BlueCafe.Core.Session`; this retained Editor facade
delegates placement/layout validation to Core. Runtime commands and storage are
documented in [STANDALONE.md](../../../docs/STANDALONE.md). Editor-only exports
and reconstruction diagnostics remain available through this API.

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
BlueCafe.CafeApi.Save("output/my-cafe.json");
BlueCafe.CafeApi.Capture("output/my-cafe.png", 1600, 1200);
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

## Reconstruction diagnostics

`BlueCafe.CafeReconstruction` provides additive Editor diagnostics. It leaves the
version-1 layout format and creative placement rules unchanged. Orientation
annotations are versioned derived observations in
a separately supplied local `docs/reconstruction/orientations.json` annotation file. It is omitted from the public repository along with source content and private captures.
Audited local vectors use the assembled native prefab frame before placement yaw;
an unknown front is never inferred from `Transform.forward`.

```csharp
var annotations = BlueCafe.CafeReconstruction.LoadOrientations();
var instance = BlueCafe.CafeReconstruction.InspectInstance(id);
var pair = BlueCafe.CafeReconstruction.InspectPair(id, otherId);
BlueCafe.CafeReconstruction.CaptureOrientationAtlas("11204", "output/desk-atlas.png");
BlueCafe.CafeReconstruction.FreezeCameraProfile("output/camera.json", 1600, 1200);
BlueCafe.CafeReconstruction.CaptureReference("output/frozen.png", "output/camera.json");
var plan = BlueCafe.CafeReconstruction.LoadReferencePlan("output/reference-plan.json");
BlueCafe.CafeReconstruction.CapturePlan("output/plan.png", plan);
var audit = BlueCafe.CafeReconstruction.ValidateReconstruction(plan);
BlueCafe.CafeApi.ValidateLayout(BlueCafe.CafeApi.Layout);
```

`InspectInstance` and `InspectPair` accept an optional detached layout and
annotations and do not change the session. They return typed serializable
records: placement/native pivots, actual renderer world AABBs, declared horizontal
footprints, audited local/world directions/axes/anchors, and signed edge gaps.
Positive gaps mean separation, negative gaps mean overlap on that AABB axis.
These measurements do not establish universal collision or contact truth.
Wall footprints are along-wall horizontal segments: catalog wall depth describes
vertical source size, so it cannot supply a wall-normal footprint. Wall height
validity remains the shared placement check; renderer AABBs expose actual depth.

`SaveCameraProfile`, `LoadCameraProfile`, and `ValidateCameraProfile` support
orthographic or perspective profiles with yaw, elevation, target, distance, lens,
clipping planes, render dimensions and a top-left pixel crop. `CaptureReference`
emits that crop without resizing or refitting after edits. Default
`CafeApi.Capture` continues fitting the room. Freezing a fitted view initially
records `room-fit-unregistered`; it does not establish reference registration.

`CapturePlan` writes a top-down PNG with source-cell grid, named rear walls,
instance/furniture/quarter-turn labels and a full-ID legend, plus `.plan.json`.
Its optional reference plan adds wall mappings, boundaries, openings and zones.
`CaptureReferenceOverlay(path, profile, plan)` projects those annotations into the
frozen view. Overlays append a legend below the render; ordinary frozen captures
retain the exact crop dimensions. Unknown annotations remain metadata rather
than guessed geometry. Atlas panels share one camera fitted to all four original
mesh rotations, with world-axis arrows and audited front or unknown labels.

`ValidateReferencePlan` validates the versioned diagnostic sidecar.
`ValidateReconstruction` reports declared inventory and supported facing, wall,
axis, pivot-zone, alignment and AABB-gap relations separately from layout legality.
Required uncertainty, unavailable content and rendering blockers prevent a scoped
semantic pass. Context-only features do not extend a bounded proof to a full room.
Visual review remains explicitly unverified in its result. Polygon/partition
geometry is diagnostic; it does not create walls or impose global placement rules.

## Semantic authoring

These `CafeReconstruction` methods preflight a detached layout, then use one shared
`CafeApi` operation. Rejected requests leave the session and undo history unchanged.

```csharp
var wallId = BlueCafe.CafeReconstruction.PlaceOnWall("11100", "x=min", 0, 1.2f);
BlueCafe.CafeReconstruction.FaceToward(id, wallId); // audited front; exact quarter turn
BlueCafe.CafeReconstruction.FaceToward(id, new UnityEngine.Vector3(0, 0, 3));
BlueCafe.CafeReconstruction.PlaceAdjacent(id, otherId, "x+", 0, "footprint");
```

`PlaceOnWall(furnitureId, wallId, alongWorld, elevation = -1)` supports `x=min`
(along Z, q0) and `z=max` (along X, q1). The default elevation uses the shared
grid-centered wall height. Requests must already be achievable on the grid;
corner resolution to another wall fails with `UNACHIEVABLE_WALL_PLACEMENT`.
Non-wall content or unknown walls yield `INVALID_PLACEMENT` or `UNKNOWN_WALL`.

`FaceToward(instanceId, targetInstanceId, direction = "front", annotations = null)`
also accepts a `Vector3` target. It uses an audited local direction and exact
horizontal target alignment, ignoring target elevation. Floor items try four
quarter turns; wall items try their current and adjacent wall attachment, so a
successful wall rotation can transfer its pivot. Unknown, stale or unachievable
directions yield `UNKNOWN_DIRECTION`, `STALE_ANNOTATION` or `UNACHIEVABLE_FACING`.
It does not approximate a diagonal target with the nearest quarter turn.

`PlaceAdjacent(instanceId, otherInstanceId, axis, gap = 0, basis = "renderer",
annotations = null)` moves an existing item on `x+`, `x-`, `z+` or `z-` relative to
the other item. Bounds use actual renderer AABBs or declared `footprint`; the
cross-axis position, elevation and rotation are preserved. Bounds must overlap
on the other horizontal axis and the nonnegative world-unit gap must be exactly
achievable on the grid without changing wall attachment. Failures include
`INVALID_RELATION`, `INVALID_AXIS`, `INVALID_BASIS`, `INVALID_GAP`,
`GEOMETRY_UNAVAILABLE` and `UNACHIEVABLE_ADJACENCY`. Room styles cannot use facing
or adjacency. All methods retain shared catalog, transform and layout validation.

Catalog search includes case-insensitive orientation aliases whose source prefab
identity still matches, alongside name/id/prefab/category/theme. Category/theme
filters and unavailable-entry behavior remain shared with the Designer.

## Partition checks and camera registration

Reference relations also support `principal_axis_parallel_boundary`,
`pivot_opposite_sides` and `pivot_boundary_distance` against a named partition
boundary. Confirmed boundary coordinates are required; uncertain boundaries
report `BOUNDARY_UNCERTAIN`. Parallel and opposite-side checks require one
directed straight segment (`BOUNDARY_AMBIGUOUS` otherwise). Parallel angles use
audited principal axes and degrees. Opposite-side clearance and nearest polyline
distance use source cells; they measure pivots, not renderer edges or collisions.

`FitOrthographicCamera(CafeRegistrationRequest)` fits a bounded yaw/elevation
search and scale/target using uncertainty-weighted landmarks. Version-1 requests
require dimensions 32..4096, at least three noncollinear fitting world landmarks,
unique ids, finite full-reference top-left pixel coordinates, evidence and positive
pixel uncertainty. Holdout landmarks (`fit = false`) never influence fitting.
Malformed requests throw `INVALID_REGISTRATION`; residual threshold failure
returns `residual-rejected`. Accepted inferred landmarks or absent holdouts return
`calibrated-candidate`; only confirmed landmarks with holdouts can return
`landmarks-registered`. Results include fit/holdout RMS, individual residuals and
the candidate camera. `ProjectReferencePoint(profile, world)` projects into the
full image before cropping. Only orthographic registration is implemented; depth
along the viewing ray remains unobservable and source transforms are not recovered.

Original surfaces 210394, 22116, 19212, 23112, 23302, 23309, 15300, 22309,
18304 and 23304 have derived static
stencil render paths keyed to exact prefab, material and frame-mesh identity.
Additional native state evidence for 39 original materials is retained in
private development diagnostics, which are excluded from this repository.
Actual architectural frames determine wall centering. Original mask/scenery parts
still rasterize and remain in full geometry bounds, but are excluded from visible
framing and picking. All original parts remain retained, including both door leaf
poses; their intended open/closed state is unknown. Downloaded bytes remain
unchanged. Door reference pose matching remains render-blocked; cockpit sky state
is unknown. Native sky/cloud animation, lighting and visual fidelity remain
unverified; static render repair does not establish full appearance parity.
