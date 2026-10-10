# Runtime architecture and shared agent boundary

Parent: [Standalone app specification](../map.md)
Label: wayfinder:grilling
Type: grilling
Status: resolved
Assignee: krisspy (Codex session)
Blocked by: 01, 02, 03, 04, 07

## Question

Confirm module ownership and dependency direction for the Unity host, cafe model,
authoring operations, geometry, rendering, asset loading, persistence and agent
adapter. Decide how to extract reusable logic from CafeApi/CafePreview while
retaining Editor integration, audited rendering and provenance. Define main-thread
execution, revision/conflict semantics, deterministic capture and inspection;
identify Editor-only dependencies that cannot enter the Player build.

## Comments

Charted 2026-10-10. Resolved after review of the shared core and runtime module boundaries.

## Answer

Architecture accepted by the user on 2026-10-10; the following specifies the
implementation boundaries and technical defaults.

One Unity desktop executable, one open cafe, one authoring core. No cloud service,
downloader, extraction helper or separate scene-authoring implementation.

| Module | Owns | May depend on |
| --- | --- | --- |
| BlueCafe.Core | Data model, catalog queries, polygon/placement/attachment validation, atomic commands and session undo | Plain C# and numerical data; no UnityEditor or Unity scene objects |
| BlueCafe.Infrastructure | Read-only pack resolution/verification, document JSON, migrations, My cafes, settings, atomic recovery/cache writes | Core and filesystem/image decoding adapters |
| BlueCafe.Unity | Mesh/material/texture resources, derived surface geometry, scene projection, picking, cameras and captures | Core plus validated asset data; Unity runtime APIs |
| BlueCafe.App | Workspace UI, gestures, dialogs, active session and main-thread command scheduling | Core, Infrastructure and Unity |
| BlueCafe.Agent | Versioned local request/response adapter and CLI bridge | The same App command dispatcher and inspection/capture services |
| BlueCafe.Editor | Existing Editor window/API/export integration during migration | Shared modules plus UnityEditor; never referenced by runtime assemblies |

The model contains a layout version, name, overall bounds/default styles, rooms,
physical walls/openings and furniture instances. Room records have stable ID,
name, editing color, simple polygon and Interior choices. Wall records have stable
ID and explicit geometry/openings; wallpaper regions are derived from adjacent
areas. Attachment records identify wall and local position/elevation rather than
reconstructing wall ownership from world coordinates. Floor-standing game doors,
arches and partitions remain furniture according to original placement metadata;
they are not silently converted into structural wall records.

Use world units for document coordinates, with source catalog gridUnit defining
snapping. Keep game furniture IDs and original part transforms distinct from
instance transforms. Source mesh normals/UVs, original texture bytes and audited
material/render rules remain intact. Surface fitting derives render geometry from
original modules without scaling source furniture. Preserve original source meshes
as inputs; cache fitted geometry separately. Source availability and render defects
remain separate states.

Pure previews are detached candidates. On commit, the dispatcher validates the
whole command against the current document/catalog revision; success changes one
revision and adds one undo entry. Failure returns a stable error code, affected
IDs and a message without changing model/history. Commands include Place, Move,
Rotate, Duplicate, Delete, room/wall operations and style assignment. UI and agent
requests share this path. Undo restores the document state but increments the
session revision so stale commands cannot accidentally become current again.

Core session state owns committed document, current revision, saved revision and
undo/redo. UI selection/hover/gesture/camera state stays separate. Name/style and
boundary edits participate in undo; asset path changes do not rewrite the document.
Splitting preserves the original room ID for one result and allocates another;
merging keeps the active room ID and removes the others atomically. New names
are editable deterministic defaults; colors distinguish areas and are persisted.

The Unity renderer applies changes by stable IDs and reuses shared immutable
resources rather than destroying every object after each edit. Thumbnails and
model resources load on demand with bounded caches. Reading/hashing/parsing can
run in background work; Unity object creation and mutations run on the main
thread. Publish staged results only if document/pack revisions still match.
Disposal/cancellation/focus loss cannot commit a stale gesture or loader result.

Editor-only replacements: runtime document session instead of ScriptableSingleton;
command history instead of Editor Undo; runtime scene objects instead of preview
scenes; contained filesystem mesh/PNG loading instead of AssetDatabase; platform
file/folder dialogs as an App adapter instead of EditorUtility dialogs. Keep .unity
scene export in Editor integration; standalone exports JSON and PNG.

Agent requests are versioned JSON with requestId, command, arguments and optional
expectedRevision; responses include success/error and resulting document revision.
Mutating requests use the same main-thread queue as physical UI input. Inspection
reports stable IDs, availability, geometry and revisions; it does not expose an
independent mutable Unity scene. The CLI sends requests to the running app through
a loopback-only local command bridge. Use length-delimited UTF-8 JSON over a local
TCP connection as the initial transport, bounded messages and a per-session token
stored in a user-local discovery record. The bridge is local automation, not a
public network API. No external service is needed. Transport viability must be
proved in the target Player build before committing to cross-platform delivery.

Capture uses an explicit camera profile/dimensions/crop or deterministic room-fit
profile. It waits for projection of the requested committed revision and returns
that revision with the image result. Room-fit reproducibility and visual fidelity
remain distinct. Semantic reconstruction metadata remains versioned sidecars;
reference constraints never become global creative-placement restrictions.

Migration: extract the reusable model/validators first, retain an Editor adapter,
then replace runtime asset loading and prove original assets in a Player build.
Next add v2 room/wall/document migration and polygon surface fitting, then route
UI/agent commands through the dispatcher. Validate old layouts before changing
session state; preserve existing furniture/instance IDs and world transforms.
Update implemented CONTRACTS.md/API.md only when the shared boundary is actually
integrated and verified; this proposal does not redefine them as implemented.
