# Integration contracts

Implemented contracts. The exact catalog schema is documented in PREPARED-PACK.md;
the versioned layout and shared API are documented in Assets/BlueCafe/Editor/API.md.

## Ownership

- Asset worker: `tools/`, `Assets/BlueCafe/Content/`, `docs/PREPARED-PACK.md`,
  `evidence/asset-pipeline/`.
- Builder worker: `Assets/BlueCafe/Editor/`, `Assets/BlueCafe/Shaders/`,
  `Assets/BlueCafe/Tests/`; scene creation via C# Editor APIs. No package or live
  Editor ownership.
- Reference worker: `docs/REFERENCES.md`, `docs/PRESET-ACCEPTANCE.md`,
  `evidence/references/`.
- Orchestrator: project bootstrap, packages, shared docs, integration, Unity CLI,
  evidence review and final verification.

## Catalog and layout

Use stable game furniture ids as catalog keys. Asset paths are project-relative.
Catalog entries need display name, category, theme/set, footprint, placement type
(floor/wall/flooring/wallpaper), model path, thumbnail path, availability and source.
Keep unavailable catalog entries searchable with an explicit state. Do not infer
that a thumbnail is a 3D mesh. The canonical acquired catalog is Assets/BlueCafe/Content/catalog.json.

Layouts are versioned JSON with room settings and furniture instances. Instances
have stable instance id, furniture id, position and quarter-turn rotation. The UI
and agent API use one placement/model implementation. No independent agent scene
assembly that bypasses tool constraints. Include save/load, undo/redo and screenshot
commands, with deterministic default framing and machine-readable errors.

The room is the primary view. Catalog and direct placement controls support it.
Use a fixed oblique 2.5D camera, with pan/zoom and an optional free camera later.
First prove a small actual-asset room; expand the catalog after reviewing it.

## Reconstruction diagnostics

`CafeReconstruction` is an additive Editor diagnostic API. Version-1 orientation
annotations live in `docs/reconstruction/orientations.json`; reference plans and
camera profiles are separate versioned JSON sidecars. They do not rewrite the
acquired catalog, source meshes/materials, or portable version-1 layouts.

Orientation observations use the assembled native prefab frame before placement
yaw and the renderer's wall-center pivot offset. Audited directions require source
identity and evidence; unknown directions remain explicit. Renderer AABBs,
declared horizontal footprints and pivots are distinct measurements. Signed AABB
gaps are geometric aids, not collision or reference-adjacency ground truth.

Frozen captures preserve target, yaw/elevation, projection/lens, dimensions and
an explicit pixel crop across layout edits. Existing `CafeApi.Capture` retains
room-fit behavior. A saved room-fit profile demonstrates reproducible framing;
reference registration requires separate landmark evidence. Reference-specific
checks supplement `ValidateLayout` and never impose global creative constraints.

Diagnostic model assembly reuses `CafePreview`; authoring reuses `CafeApi`.
Workers request all live Editor commands from the orchestrator. Evidence must
distinguish a bounded group proof from a full-scene fidelity pass, retain unresolved
prominent features, and preserve the active session after integration checks.

Semantic authoring (`PlaceOnWall`, `FaceToward`, `PlaceAdjacent`) preflights a
detached layout, then invokes one shared `CafeApi` operation. Failure does not
change the session or undo history. Explicit wall/grid requests and audited
quarter-turn directions must be achievable; commands do not silently approximate
relations. Adjacency distinguishes renderer AABBs from declared footprints.
Catalog aliases remain derived metadata and apply only to matching source identity.

Partition checks consume confirmed sidecar boundaries. Axis parallelism uses
audited directions; side clearance and polyline distance use pivots in source
cells. They do not infer wall extents, furniture contact or unknown partitions.
Orthographic camera registration keeps fitting and validation landmarks separate,
requires explicit evidence/uncertainty, and reports residuals and candidate status.
A numerical fit is not visual acceptance or recovery of native source transforms.

Surfaces 210394, 22116, 19212, 23112, 23302, 23309, 15300, 22309, 18304 and
23304 use derived render states
keyed to audited prefab/material/frame-mesh identity while preserving original
content bytes. Native state evidence for the additional 39 original materials lives in
`evidence/core-reconstruction-stage2-2026-10-09/inventory-repair/special-surfaces/native-render-state.json`.
Actual frames determine wall centering; mask/scenery helpers remain rendered and
measurable but cannot frame the view or intercept picking. All original parts,
including both door leaf poses, remain retained; intended leaf state is unknown.
Door reference pose matching remains render-blocked; cockpit sky state is unknown.
Static stencil repair does not establish native animation, lighting or scene fidelity.

`bin/feedback.py` targets this project explicitly. Before `eval` or `tests`, it
records raw `recompile_status` output and rejects an incomplete, failed, erroneous
or unparseable compilation preflight. Code is passed as a subprocess argument,
without shell interpolation. Each run preserves source fingerprints and evidence;
the guard checks compilation state, not visual correctness or scene acceptance.


## Standalone runtime spine (2026-10-10)

The executable uses `Core`, `Infrastructure`, `Unity`, `App` and `Agent` assemblies.
Core has no Unity engine reference. The Editor adapter delegates placement and
layout validation to Core while retaining the Editor API and reconstruction tools.
Runtime DTOs retain portable v1 field names and stable IDs; v2 polygon geometry
is a later migration and is not represented by rectangular v1 bounds.

Infrastructure reads a prepared pack without AssetDatabase and without writing
source content. It normalizes only the exact legacy `Assets/BlueCafe/Content/`
prefix in memory. Escaping, absolute and symlink paths are rejected. Catalog
structure is strict; meshes/PNGs and file hashes are checked. Missing/corrupt
resources make their item explicitly unavailable; thumbnail availability is
independent. Catalog refresh preflights document/history references before adoption.
Runtime rendering uses the original geometry/materials and the audited stencil
rules above. Known unavailable items may survive saved layouts, with diagnostics;
new placement requires an available catalog item.

Core Session owns revisions, document dirty state and atomic group operations.
A command can specify expectedRevision; conflicts and invalid group operations
leave document/history unchanged. OpenDocument validates before replacement and
clears old undo/redo. RefreshCatalog is separate from document revisions. Public
snapshots are detached so callers cannot bypass commands by mutating a returned DTO.

Saved cafe identity is a local GUID, separate from layout name and portable IDs.
Imported layouts are unsaved copies. App storage uses complete atomic writes,
with recovery separate from ordinary saves and a retained previous recovery.
Save/Discard/Cancel protects UI replacement and close. The loopback bridge import
refuses dirty work; it has no implicit discard. It dispatches into the same Session
on the main thread, with bounded queue/message sizes and expired-request rejection.
The owner-only discovery file holds the session token; it is never portable data.

Details and current limitations are in [STANDALONE.md](STANDALONE.md). Player
acceptance evidence must remain distinct from Editor baseline evidence.


App popup Escape cancels the topmost native menu or modal from either UI key or navigation cancellation, restoring focus without committing its action. Deferred automatic recovery remains stored and reviewable in Settings; automatic recovery writes pause until Restore/Discard explicitly resolves it. Manual Save and normal dirty-close prompts remain available, and reviewing Restore protects later dirty work through Save/Discard/Cancel.

Floor furniture centers and rigid all-floor translations snap at half the room grid; wall/mixed selections retain full-grid movement and attachment protection. Existing v1 coordinates remain valid. Runtime preview/selection outlines use the same rotated catalog footprint at floor height, independent of rendered mesh bounds. Automatic edit-grid visibility does not change the manual Grid preference.
