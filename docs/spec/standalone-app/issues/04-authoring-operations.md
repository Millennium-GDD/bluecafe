# Atomic group editing and attachment behavior

Parent: [Standalone app specification](../map.md)
Label: wayfinder:grilling
Type: grilling
Status: resolved
Assignee: krisspy (Codex session)
Blocked by: 02

## Question

How do mixed floor/wall selections move, rotate and copy? Define pivot, snapping,
relative offsets, attachment changes, invalid destination preview, cancellation,
repeated copies, focus loss and one-operation undo. Decide furniture crossing room
areas and behavior when room/bounds edits invalidate existing placements. What
shared command/result contract must human and agent authoring use? Define room
split/merge style inheritance and handling of differently styled regions.

## Comments

Charted 2026-10-10. Resolved with explicit approval of group rotation and split/merge styles.

## Answer

Resolved 2026-10-10. Rotate a furniture selection as a rigid arrangement in
quarter turns around its plan-space center; retain relative offsets and apply
quarter-turn orientations to every item. The default pivot is the center of the
selection's declared plan-space footprint bounds, not transient renderer bounds.

Use one shared authoring operation for UI and agent requests. Stage candidate
transforms before validation, returning either one committed revision or structured
item-level errors with no mutation. Move/copy snaps the group anchor and preserves
relative offsets; do not independently snap each member and distort the group.
Mixed floor/wall selections obey every item's placement/attachment constraints;
invalid members block the whole operation. No automatic partial movement or guessed
reattachment to another wall. Preview invalid items and the full destination.

Copies retain furniture IDs, relative transforms and rotations but receive new
instance IDs. Originals remain unchanged. Each move/rotate/copy is one undo step;
repeated copies are separate operations. Escape, lost focus/release, document
replacement and cancelled gestures remove only the preview. Undo/redo restores
selection-affecting model operations atomically. These rules extend the previously
approved group editing and input acceptance requirements.

Furniture may cross room-area boundaries; it belongs to the cafe, not a named
room. Overall cafe bounds and actual placement constraints still apply. Resizing
bounds previews conflicts and rejects a commit that would invalidate items;
never silently clips/deletes them.

Room splits inherit original Interior styles. Merge of differently styled rooms
asks which room's Interior choices to retain; matching styles need no prompt.
Furniture stays in place. Split/merge are single undoable operations; cancellation
preserves the prior geometry/styles. Stable room IDs and resulting name/color defaults are specified in
[Runtime architecture and shared agent boundary](05-runtime-boundaries.md).

Implementations must prove center rotation, mixed attachments, repeated copies,
invalid atomic failures, focus loss, split/merge styles and exact persistence.
No tests or runtime acceptance are claimed by this planning resolution.
