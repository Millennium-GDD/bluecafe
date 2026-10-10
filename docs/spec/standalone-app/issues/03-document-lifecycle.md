# Single cafe replacement and recovery

Parent: [Standalone app specification](../map.md)
Label: wayfinder:grilling
Type: grilling
Status: resolved
Assignee: krisspy (Codex session)
Blocked by: none

## Question

Only one cafe is open. Define New/Open/preset/close behavior with Save, Discard
and Cancel; untitled saves, failed saving, failed/cancelled loading, and when the
replacement is committed. Decide autosave versus explicit save, recovery after a
crash, missing-asset layout opening and legacy migration. Identify exactly which
state belongs in the portable document versus session/settings/recovery.

## Comments

Charted 2026-10-10. Resolved through the user exchange on single-document saving and automatic recovery.

### Historical saving discussion — 2026-10-10 (superseded by Answer)

The user accepts Save storing named cafes in Presets → My cafes; first Save asks
for a name, subsequent saves update that cafe. Import/Export handles portable
layout files without bundling game assets.

At this point the user asked whether separate recovery was normal; later
approval settles it in the Answer below.

## Answer

Resolved 2026-10-10. One cafe is open. Save writes a named cafe into Presets →
My cafes; first Save requests a name and later saves update that cafe. Import and
Export round-trip portable versioned layouts without bundling asset files.

New/Open/preset replacement and app close prompt Save/Discard/Cancel for dirty
work. Cancel or a failed/cancelled Save keeps the current document. Preflight a
replacement before committing it; failed loading preserves the open cafe and its
undo history. Discard is intentional and invalidates that document's unsaved
recovery copy; do not revive discarded edits on next launch.

Automatic recovery is separate from Save and does not update the saved cafe.
Offer recovery after abnormal termination; restoring it opens unsaved work that
can then be saved explicitly. Use the name automatic recovery in UI rather than
imply saved status. Recovery write failure must not discard the active document
or falsely report that recovery is current. Atomic writes and document revisions
prevent incomplete files or stale background writes from replacing newer state.

The portable document holds cafe bounds, stable room/wall/instance identities,
room geometry and styles, original furniture IDs and transforms, and necessary
attachment data. Machine-local asset paths, window/workspace/selection state,
undo history and recovery bookkeeping belong to app/session storage. Undo/redo
covers the active session; durable edit-history replay is not assumed.

Follow established scope: preserve original IDs/transforms during legacy migration;
reject unsupported/malformed layouts before replacing the current cafe. Distinguish
invalid IDs from known unavailable assets: retain known unavailable instances and
show their unavailable state without substitute furniture. Imported layouts can
be edited without embedding their game assets.

Recovery frequency/retention defaults are recorded in
[Standalone delivery sequence and acceptance](06-delivery-gates.md); unavailable
assets follow [Local pack validation and relocation](07-pack-validation.md). None of these decisions establish runtime implementation or
verified crash recovery.

### Document identity and preset opening

My cafes → Open edits that saved cafe and Save updates it. Open as new cafe,
opening an Example and importing a portable layout create an untitled/unsaved
copy with a fresh local saved-cafe identity; subsequent Save asks for a name and
does not overwrite the source preset/file. Preserve furniture/room/wall IDs and
transforms in the copied design; saved-cafe identity is separate from content
identities. Export leaves saved-cafe identity unchanged. All replacement paths
still use Save/Discard/Cancel and detached load validation. Name collisions show
an explicit replace-or-rename choice; do not overwrite a different saved cafe
because it has the same display name.
