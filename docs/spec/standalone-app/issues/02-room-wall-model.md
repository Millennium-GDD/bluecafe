# Room areas and physical wall relationships

Parent: [Standalone app specification](../map.md)
Label: wayfinder:grilling
Type: grilling
Status: resolved
Assignee: krisspy (Codex session)
Blocked by: none

## Question

Rooms are named polygon areas; physical walls, partitions and openings are
separately editable. What are their exact relationships? Decide area overlap and
shared boundaries, uncovered space, holes, floor assignment, which physical wall
faces receive a room wallpaper, wall identity/attachment, and what happens to
surfaces and attached furniture when an area or wall changes. Specify concrete
L-shaped, diagonal, shared-wall and deleted-wall scenarios.

## Comments

Charted 2026-10-10. Resolved through the recorded user exchange.

### Historical discussion — 2026-10-10 (superseded by Answer)

Confirmed: room areas share edges but do not overlap; uncovered space uses cafe
flooring defaults. Furniture belongs to the cafe and remains in place when room
areas move/resize/disappear. Block wall deletion/reshaping when it would invalidate
attached decorations; highlight affected items for correction first.

The user directs us to use established game mechanics where they answer a
question rather than request redundant decisions. The independent per-face
wallpaper override proposal was not approved and is withdrawn from the proposed
baseline. Keep one wallpaper selection per styling scope, consistent with the
existing style replacement model. The approved standalone scope still provides
per-room Interior choices; this is a BlueCafe extension.

Evidence: docs/REFERENCES.md placement layers and CafeModel/CafePreview implement
one cafe wallpaper style across both rear walls. They do not establish native
polygon rooms or independently styled wall faces. Exact in-game replace/confirm
interaction remains uncaptured in docs/REFERENCES.md. Do not call the proposed
multi-room assignment behavior a recovered game mechanic.

At this point in the discussion, wall/style relationships and holes were still
open. The subsequent user approvals and Answer below supersede that partial state.

## Answer

Resolved 2026-10-10 after explicit user approval of the following rules:

- A room is one connected, closed, simple polygon with no internal holes. Support
  rectangles, L-shaped/concave polygons and diagonal edges; gaps/courtyards are
  uncovered space between rooms. Reject self-intersections and degenerate areas.
- Rooms may share boundary edges but cannot overlap in area. Named room flooring
  covers its polygon; uncovered space uses the cafe default.
- Drawing/editing a room does not create, move or remove physical walls. Furniture
  and source-asset partitions belong to the cafe, not to room-area ownership.
  Room edits leave their transforms intact; geometry and styles update atomically.
- Each room selects one wallpaper. Physical wall appearance is derived from the
  adjacent room area on that side, changing along the wall when that adjacent
  area changes. Unassigned/outside areas use the cafe default. No manual per-face
  wallpaper override/assignment UI. Shared walls may derive different room
  wallpaper on opposite sides without duplicating the physical wall.
- Walls and furniture retain stable identity. Room-area edits do not detach wall
  decorations. Physical wall edits/deletion that would invalidate attachments are
  blocked and highlight affected items; users move/remove decorations first.
- Polygon styles and derived wallpaper regions extend BlueCafe; they are not
  claimed recovered native game mechanics. Preserve original source geometry,
  textures and transforms. Fitting/clipping must be proved in runtime rendering,
  including partial modules and diagonal/concave corners, without invented assets.

Scenarios: an L-shaped reading area clips flooring to its area and leaves cafe
furniture unchanged. A diagonal boundary is legal. A wall alongside reading and
counter areas changes wallpaper at their boundary. Deleting a decorated wall is
blocked; deleting a named area retains the wall and items and restores defaults
for the now-uncovered surfaces.

Split/merge style inheritance and previews are resolved in
[Atomic group editing and attachment behavior](04-authoring-operations.md); wall/area
intersection and rendering boundaries are specified in
[Runtime architecture and shared agent boundary](05-runtime-boundaries.md). This decision establishes the behavior, not proof of
implementation or visual acceptance.

### Structural editing flow

Rooms has Areas/Walls modes. Areas retains the approved area toolbar. Walls
provides Select, Draw wall, Edit wall, Add opening and Delete; selecting a wall
shows geometry, height, opening list and affected decorations in the inspector.
Select an opening to move/resize/delete it within its host wall. Validate the
complete preview before one atomic commit; protected decorations block invalid
edits. Deleting a wall removes its openings only after the wall operation passes
attachment checks. Rooms view shows physical wall lines and opening spans
separately from area colors. This exposes the already-agreed separate wall/opening
model; it does not make a room boundary into a wall or a furniture door into an
opening. Build retains furniture tools.
