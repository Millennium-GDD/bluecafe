# Standalone application

Build, Rooms, Presets, Assets and Settings share one validated cafe document. The application loads a compatible [prepared pack](PREPARED-PACK.md) supplied separately under applicable permissions. Source and release packages exclude game assets, catalogs and acquisition tools. Authored example layouts store arrangements and stable IDs without source geometry or artwork.

Build supports original furniture placement, drag/move, wall mounting and transfer, quarter-turn rotation, duplication, deletion and undo/redo. Shift adds to selection; R rotates; Ctrl+D duplicates; Delete removes; Ctrl+Z and Ctrl+Shift+Z undo/redo. Armed furniture previews show validity before release. Cancellation leaves the committed document/history intact. The compact toolbar shows selection count and icon actions; panel menu choices show/hide the catalog and inspector, with resize/collapse handles available on the panels.

The Build toolbar’s Area select draws an XZ rectangle on the floor, projected as a parallelogram in oblique view. It selects furniture centers, including wall decorations, and excludes room-wide styles. Shift adds to selection. Releasing the left button returns to normal Select; Escape or lost pointer capture cancels the gesture. Selection does not dirty the cafe. Middle-drag orbits around the cafe using azimuth/elevation angles; right-drag pans and scroll zooms. God’s-eye view toggles a fitted vertical view, then restores the previous camera framing.

Wall decorations use mounting-center Height above the floor. Drag on visible walls, use the wall selector/Along/Height fields or Lower/Raise for a grid step. Grids appear during placement/movement unless manually kept visible. Original items classified as floor furniture retain that placement kind, including some door/window props.

Rooms edits the enclosing cafe and independently named polygon areas. Move/resize an area without moving furniture; rename/delete an area explicitly and undo if needed. Original thumbnails within footprint bounds identify furniture; unavailable thumbnails are indicated. Extent fitting includes supplied furniture footprints. Four cardinal enclosing walls and openings are supported; arbitrary internal/diagonal physical walls and oppositely styled shared-wall faces remain outside this implementation.

Presets includes My cafes and authored examples, lazy rendered previews, double-click opening, rename, export, enlarged preview and Open in file manager. Reveal uses the existing saved record; bundled examples are materialized from their original portable JSON in the app’s Examples folder. Existing modified example copies are preserved. The action does not change the open document. Open edits a saved cafe; Open as new cafe starts a separate saved identity. Opening protects dirty work with Save/Discard/Cancel. A fresh profile saves an Empty room with flooring/wallpaper IDs 1/2; Open at startup chooses a saved cafe for subsequent launches without replacing current work. Assets supports real filters, sorting and virtualized scrolling. Settings selects/verifies the pack and shows folder requirements directly.

Save writes My cafes atomically. Import creates an unsaved copy and validates all IDs/transforms. Portable export preserves stable furniture identity and transforms without assets. Automatic recovery is separate, with a two-second idle/thirty-second maximum dirty interval and a retained previous complete snapshot. Escape/Later can defer recovery; the draft stays intact and automatic recovery pauses until Restore/Discard.

Run the whole extracted Player directory together. Unity Editor and Python are development requirements only. The Linux executable accepts:

```sh
./BlueCafe.x86_64 --asset-pack /path/to/prepared-pack --data-path /path/to/isolated-profile
```

Settings also selects the pack. Linux native dialogs use the desktop `zenity` package; editable paths provide a fallback. Windows currently uses editable path fields. See [development/build instructions](DEVELOPMENT.md) and [Windows build instructions](WINDOWS-BUILD.md).

## Agent bridge

`agent.json` in application data identifies the loopback port and private session
token. It is a local discovery file, not a portable layout. Linux permissions are
owner-only; messages use a four-byte network-order length followed by UTF-8 JSON,
with a 4 MiB limit. Queued requests expire and execute on the Unity main thread.

The supplied development client can be run from an agent environment:

```sh
python3 bin/cafe-agent.py status
python3 bin/cafe-agent.py catalog --arguments '{"query":"chair"}'
python3 bin/cafe-agent.py place --revision 3 \
  --arguments '{"furnitureId":"11204","x":0,"z":0}'
```

Commands: status, catalog, layout, place, move, rotate, duplicate, delete, undo,
redo, import, save, export, capture and the geometry commands below. Arguments use `instanceIds` for groups;
move/copy use dx/dz, rotate uses steps. An optional expectedRevision rejects stale
commands. Save accepts an optional `name`, writes My cafes and optionally a portable path; export only writes
the portable file. Import refuses dirty work and validates before document
replacement. Capture synchronizes the committed revision and uses room-fit framing.
Use `--discovery /path/to/agent.json` when `--data-path` overrides app storage.

## Geometry bridge arguments

The editing commands dispatch into the same Core Session operations as Rooms and Build.
They accept the request-level `expectedRevision` (`--revision` in the client),
validate a detached candidate, and commit one undoable edit when content changes. Failed edits do not
change the document or undo history. A first successful geometry command upgrades
v1 to v2 atomically. Read back `layout` to inspect snapped coordinates, stable IDs,
styles and attachment metadata. Earlier alpha binaries predate these geometry commands.

| Command | Arguments | Behavior |
| --- | --- | --- |
| `setAreas` | `areas`: complete array of area records | Replace/add/remove named areas atomically; retain desired IDs and styles in the supplied records. An empty array removes all named areas. |
| `translateArea` | `areaId`, `dx`, `dz` | Translate every polygon vertex by one half-grid-snapped delta; furniture stays fixed. |
| `resizeAreaCorner` | `areaId`, `vertexIndex`, `x`, `z` | Resize an axis-aligned four-vertex rectangle at the indexed corner, keeping the opposite corner fixed. Crossing the anchor is rejected. |
| `setAreaVertex` | `areaId`, `vertexIndex`, `x`, `z` | Change one vertex of a rectangle, concave or diagonal area; validate the resulting polygon. |
| `setExtent` | `centerX`, `centerZ`, `extentWidth`, `extentDepth` | Move/resize the enclosing cafe and its cardinal walls, snapping edges at half-grid. Furniture stays fixed; areas exactly matching the old enclosure follow the new one. |
| `placeOnWall` | `furnitureId`, `wallId`, `along`, `elevation` | Create available original wall furniture with full-grid-snapped coordinates and the wall's inward orientation. Returns the new instance ID. |
| `setWallAttachment` | `name`: existing instance ID; `wallId`, `along`, `elevation` | Move/re-attach one wall decoration and validate its original footprint. The current bridge uses `name` for this command's target ID. |
| `setWallOpenings` | `openings`: complete array of opening records | Replace the enclosure's openings atomically; reject invalid spans or conflicts with decorations. An empty array removes all openings. |
| `viewWall` | `wallId` | Focus the runtime camera on that wall; does not edit the document or create an undo step. |

All coordinates and dimensions are metres. `vertexIndex` is zero-based in the
stored perimeter order. Area records have `id`, `name`, `color`, `flooringId`,
`wallpaperId` and `vertices` (`{x,y,z}` with `y:0`); empty style IDs use the cafe
Interior defaults. `setAreas` validates the coordinates as supplied, while drag
and coordinate-edit commands snap at half-grid. An opening record has `id`,
`wallId`, `along` (horizontal center), `width`, `bottom` and `height`.

For `minX = centerX - width/2`, `maxX = centerX + width/2` and the equivalent Z
edges, the wall coordinate system is:

| `wallId` | Plane | `along = 0` and positive direction | Inward `quarterTurns` |
| --- | --- | --- | --- |
| `left` | X = minX | minZ, increasing Z | 0 |
| `back` | Z = maxZ | minX, increasing X | 1 |
| `right` | X = maxX | maxZ, decreasing Z | 2 |
| `front` | Z = minZ | maxX, decreasing X | 3 |

Pass explicit finite `elevation` to the wall commands: it is mounting-center
height, not distance from the top of the decoration. This API's along-wall origin
is different from the retained Editor reconstruction API's world-axis coordinate.
The existing `position.y` stores elevation and `wallAlong` stores the snapped
along-wall center. New and explicitly adjusted attachments use declared/original
bounds to validate edges, wall height and openings. Migrated v1 furniture retains
`legacyWallFootprint` until explicitly adjusted; see [the geometry contract](CONTRACTS.md#portable-geometry-and-atomic-migration).

```sh
python3 bin/cafe-agent.py setExtent --revision 3 \
  --arguments '{"centerX":1.2,"centerZ":0,"extentWidth":12,"extentDepth":12}'
python3 bin/cafe-agent.py placeOnWall --revision 4 \
  --arguments '{"furnitureId":"10100","wallId":"left","along":3.6,"elevation":2.4}'
python3 bin/cafe-agent.py setWallAttachment --revision 5 \
  --arguments '{"name":"INSTANCE_ID_FROM_PLACE","wallId":"left","along":4.2,"elevation":3.0}'
```

Use the actual revision returned by the previous command and an available catalog
wall item. Failures return codes such as `AREA_OVERLAP`, `INVALID_POLYGON`,
`AREA_OUTSIDE_EXTENT`, `ATTACHMENT_CONFLICT`, `OUTSIDE_WALL`, `INVALID_ELEVATION`,
`OPENING_CONFLICT` or `REVISION_CONFLICT`; inspect `message` and `affectedIds`.
`capture` still produces deterministic room-fit framing, even after `viewWall`.

## Acceptance

The corrected 0.1.1 implementation passed 209 Unity tests and integrated synthetic Linux Player UI checks for area selection, immediate dragging, orbit, pan and the vertical-view toggle in simple and dense layouts. Source features, compilation, geometry tests, packaging integrity and a cross-platform build are separate from target-machine acceptance. Windows/macOS rendering, installation, native physical input/dialogs, save/reopen/recovery, clean-machine upgrade preservation, performance and human visual/timing approval require their own evidence. Follow [verification gates](VERIFICATION.md); local development evidence is intentionally excluded from this repository.
