# Integration contracts

BlueCafe accepts separately supplied prepared packs under the user's applicable permissions. This repository contains the application, original source, licensed UI font and authored layout examples. It includes no game catalog, geometry, textures, thumbnails, reference captures or acquisition tools. See [the prepared-pack contract](PREPARED-PACK.md).

Core owns placement, validation, revisions, document history and atomic edits. Infrastructure verifies read-only packs and stores saved cafes/recovery atomically. Unity renders supplied original geometry. App UI and the local Agent bridge invoke the same Core operations; agents do not assemble independent scenes. The retained [Editor API](../Assets/BlueCafe/Editor/API.md) delegates validation to Core.

One orchestrator owns the live Editor, packages and scene integration. Independent workers edit source and request live scene work through that orchestrator. Use explicit Unity project targeting and [verification gates](VERIFICATION.md).

Catalog keys are stable furniture IDs. Supported fields include display name, category, theme, footprint, placement kind, original parts, resource paths, availability and provenance. Source geometry and textures remain read-only. Missing resources remain explicitly unavailable. A thumbnail is independently available and is never treated as furniture geometry. Source metadata alone does not establish authenticity or permission.

Portable layouts store stable instance/furniture IDs and transforms, cafe-wide Interior, and v2 geometry. Saved cafe identity is a separate local GUID. Rendering state is derived; original content bytes remain unchanged. Reconstruction code retains observations of original material/frame behavior, while private source captures and annotation sidecars are omitted from the public repository. Optional reconstruction diagnostics require the user's own separately supplied annotations; diagnostic tests that require a prepared pack remain integration checks.

## Standalone runtime

Version-1 app settings retain `packRoot` and add optional `defaultCafeId` and
`emptyCafeId` saved-record identities. Normal startup opens the selected saved
cafe clean. First startup saves an Empty room with original floor/wall styles,
then uses it as the default. Missing default records fall back to the tracked
starter (or a uniquely named replacement); existing corrupted records report an
error. Asset-folder changes preserve these settings. Explicit `--layout` opens
an unsaved portable layout instead, and recovery remains independently protected.
Choosing a startup cafe changes settings only, leaving the open document intact.

Saved-cafe renaming uses `DocumentStore.RenameCafe`: trim and validate the name,
retain cafe/instance IDs and transforms, and use the existing atomic save and
case-insensitive name collision check. Renaming an open dirty cafe updates its
name without saving its unsaved furniture changes. Build name edits remain
undoable and require Save. Cross-wall drag commits one `SetWallAttachment`
operation after target-wall placement validation; cancellation preserves history.

The executable uses `Core`, `Infrastructure`, `Unity`, `App` and `Agent` assemblies.
Core has no Unity engine reference. The Editor adapter delegates placement and
layout validation to Core while retaining the Editor API and reconstruction tools.
Runtime DTOs retain portable v1 furniture field names and stable IDs. The Linux
beta source also implements portable layout v2; the enclosing cafe remains an
axis-aligned rectangle, while named floor areas are independent polygons. This
source contract does not establish release or Player acceptance by itself.

### Portable geometry and atomic migration

- `room` retains `width`, `depth`, `wallHeight`, `grid` and adds `centerX`,
  `centerZ`. Extent edges are center plus/minus half the corresponding dimension;
  the cafe need not remain at the old origin.
- `areas` contains records with stable `id`, nonempty `name`, `color`, optional
  `flooringId` and `wallpaperId`, and perimeter-ordered `vertices` of `{x,y,z}`
  with `y = 0`. Each area is a connected simple polygon with 3–256 vertices and
  no holes. Concave/L-shaped and diagonal boundaries are supported. Areas must
  fit inside the enclosure; shared boundaries are legal, positive-area overlap,
  self-intersections, backtracking and degeneracy are rejected. Empty style IDs
  inherit the cafe-wide Interior selection.
- `openings` contains `{id, wallId, along, width, bottom, height}`. `along` is
  the opening's horizontal center on its host wall; `bottom` is height above
  the floor. Openings must fit the wall and cannot intersect an attached
  decoration's validated footprint.
- Wall furniture adds `wallId`, `wallAlong` and `legacyWallFootprint` to the
  existing instance fields. `position.y` remains mounting-center elevation;
  `position.x/z` and `quarterTurns` must match the identified wall.

V1 files remain readable with their original IDs, positions and rotations. The
first geometry or explicit wall command upgrades a detached candidate to v2 and
derives existing left/back attachment identity. Migration and the requested edit
commit together only after complete validation; rejection leaves version,
document, revision and history unchanged. One successful changed edit has one undo step; no-ops add none.
Public snapshots and gesture previews remain detached.

Area translation, anchored rectangle corner resizing and polygon vertex edits
leave cafe furniture transforms fixed and retain area identity/styles. Coordinate
edits and enclosure edges snap at half the room grid; `setAreas` preserves the
supplied valid polygon coordinates. Enclosure edits also resize an area that
exactly matched the previous enclosing rectangle. Other areas must still fit.
Attached furniture stays in place, with its along-wall coordinate recomputed;
moved wall planes, insufficient wall span or invalid openings block the edit.

The physical enclosure has four stable wall IDs: `left`, `back`, `right`,
`front`. Their along-wall origins/directions are documented in STANDALONE.md.
They are separate from named-area boundaries. Arbitrary internal walls,
independently editable wall segments and the broader shared-wall/two-sided
wallpaper model are **not implemented by this cardinal enclosure contract**.

New or explicitly adjusted wall attachments snap along-wall position and elevation
at the full room grid. Their footprint uses the larger of the declared catalog
span and original bounds span (native Z for width, native Y for height), and must
fit wall edges, floor/top and openings. Migrated v1 attachments carry
`legacyWallFootprint = true`: they retain the prior declared dimensions and
along-wall grid phase so older legal transforms are not silently displaced or
invalidated by larger original bounds. Duplicate preserves that marker;
`setWallAttachment` clears it on the candidate and enforces the current original
bounds checks. A rejected adjustment retains the existing marker and transform.

V2 floor rendering derives clipped meshes from original triangles, normals, UVs,
colors and material assignments; it does not modify source pack bytes. Polygon
triangulation supports concave/diagonal clipping and partial modules. Explicit
area floors replace default coverage within their polygons; uncovered space keeps
the cafe default. Clipping classifies/intersects the same normalized plane with a
small roundoff tolerance so near-coplanar original skirts remain whole. Perimeter
wallpaper derives from the adjacent area's selection or the cafe default, using
fitted original modules and opening intervals. Missing selected resources are
reported unavailable. Floor modules repeat in bounded shared mesh blocks (up to 32×32 copies per part,
adapted to 65,536 vertices, and 8,192 blocks per floor style). Intact blocks share
original-derived geometry; only polygon/exclusion boundary blocks are clipped.
All derived prototypes and clipped meshes count toward the rendering budget and
are released when their style retires. Original source bytes remain unchanged.
Room-fit camera depth, background distance and maximum zoom adapt to large cafes.

The v2 renderer cuts away enclosure wall modules facing away from the camera;
this does not delete physical walls or attachments. `FocusWall` views any cardinal
wall from the interior; room-fit capture retains the standard oblique framing.
Runtime views isolate their visibility during rendering, and retired projections
are deactivated immediately before deferred destruction. Geometry tests, exact
persistence, original-asset Player rendering, physical input, performance and human
visual approval remain distinct verification obligations.

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

## Windows build and packaging (2026-10-11)

`WindowsStandaloneBuild` reuses the Linux builder's Bootstrap/UI preparation and
targets `StandaloneWindows64` with a temporary Mono backend and .NET Framework
4.8 API compatibility for the Windows System appearance Registry API. Existing Linux entry
points retain their target, versions and output paths. The shared builder restores
window, company, product and version settings after each invocation; the Windows
wrapper also restores the original active target before the previous standalone
backend and API compatibility. A SessionState recovery record resumes settings
cleanup after a domain reload. It does not install modules.
Windows output must be a fresh folder under project `Builds/`, with `BlueCafe.exe`;
evidence stays under project `evidence/`. Build support is checked before preparing
the scene. Scheduling through the resident Editor avoids a second Editor and does
not itself indicate successful build completion.

The Windows `build-result.json` records `result`, `version`, `target`, `backend`,
absolute `output`, `errors`, `warnings`, `bytes`, `development`, optional `error`,
`apiCompatibility`, `originalActiveTarget`, `settingsRestored`,
`sourceFingerprint`, and `files: [{path, sha256}]`. Paths in `files` are relative
POSIX paths of all output files except `.meta`; hashes are lowercase SHA-256.
The source fingerprint hashes relative paths and file hashes in Core,
Infrastructure, Unity, App (including build code and Bootstrap), Agent, Shaders,
Packages and ProjectSettings immediately before BuildPlayer. It excludes the
external downloaded pack. Scheduling writes `Queued` immediately, then building
writes `Started`; settings cleanup uses `Restoring` until verified. A new attempt invalidates any prior success report;
failure records an exception and cannot be packaged as a successful Player.

`tools/package_windows.py` validates the exact file set/hashes, output identity,
target/backend and successful zero-error report, rejects development Players
unless explicitly allowed, and creates a folder and ZIP with notices and the
font OFL license. Existing outputs/evidence are preserved. It never acquires or
packages the downloaded pack. This integrity proof does not establish Windows
runtime acceptance. Native dialogs still have only a Linux adapter; Windows uses
the existing editable paths. Usage is in [WINDOWS-BUILD.md](WINDOWS-BUILD.md).

Floor furniture centers and rigid all-floor translations snap at half the room grid; wall/mixed selections retain full-grid movement and attachment protection. Existing v1 coordinates remain valid. Runtime preview/selection outlines use the same rotated catalog footprint at floor height, independent of rendered mesh bounds. Automatic edit-grid visibility does not change the manual Grid preference.

LocalBridge socket accept/read/write and task continuations run outside Unity's
synchronization context. Only the bounded main-thread Pump parses Unity JSON,
authenticates and dispatches Core commands. Closing the bridge shuts queue
admission before draining; authentication, revision/deadline/message/queue limits
are unchanged. Network round-trip timing and input-to-render preview timing are
recorded separately; neither is a timed physical-input human workflow.

Saved startup preferences retain a default saved cafe ID separately from the current document. A first profile stores an Empty room with original flooring/wallpaper IDs 1/2 and opens it clean once a compatible pack is selected. Selecting another saved cafe as startup does not replace or dirty the open document. Pack refresh preserves these preferences; a valid saved default reopens clean on launch. Rename preserves saved cafe identity, and named-area rename/delete changes only area records through Core history.

Wall selection alone does not enable automatic wall grids. Armed placement and active wall moves show the grid; the manual Grid preference remains independent. Wall dragging can transfer an attachment to another visible cardinal wall, with current footprint validation, preview cancellation and one undoable commit. Rooms plan thumbnails reuse supplied original thumbnails within footprint bounds; missing previews stay explicit and the retained thumbnail scope is bounded.

## 0.1.1 workspace controls

Build Area select uses a rectangle on the floor XZ plane and selects furniture
instance centers within it, including wall decorations and unavailable catalog instances;
room-wide flooring/wallpaper/background styles are excluded. Shift adds to the existing
selection. Selection is view state and does not revise or dirty the document.
Escape, lost pointer capture, focus loss and a changed document revision cancel
an in-progress rectangle without changing the selection. Drag endpoints clamp
to the canvas before projecting onto the floor. The active outline projects the
four floor corners, so it appears as a parallelogram in oblique view. Releasing
the left button completes selection and immediately returns to normal Select.

Middle-drag orbits the camera around the actual cafe center at half wall height
with azimuth/elevation angles, preserving radius, zoom and panned framing;
elevation stays between 5 and 85 degrees. Meaningful orbit exits top-down.
Right-drag pans. Only a stationary right-click opens the furniture menu.
God's-eye view looks vertically down,
fits the complete enclosure, and toggles back to the previous camera pan/zoom.
Fit room respects the current view mode; wall inspection exits top-down.
Portable image capture retains deterministic oblique framing and restores the
active viewport camera afterward.

Presets can reveal a saved cafe's existing record in the desktop file manager.
Bundled examples materialize their original TextAsset bytes into app storage
under Examples, keyed by source SHA-256. Existing modified copies are preserved.
Missing files report unavailable. Linux requests file selection through
FileManager1 with containing-folder fallback; Windows and macOS use their native
reveal commands. File-manager actions leave the open document and history intact.
