# Standalone delivery sequence and acceptance

Parent: [Standalone app specification](../map.md)
Label: wayfinder:grilling
Type: grilling
Status: resolved
Assignee: krisspy (Codex session)
Blocked by: 05

## Question

What is the smallest vertical slice that proves the chosen architecture, and
which acceptance gates permit Linux alpha and cross-platform beta? Specify
clean-machine local asset loading, polygon surfaces, safe document replacement,
physical input, persistence/recovery, dense scenes, original-material fidelity,
agent operations and target-platform evidence. Decide measurable responsiveness
and human/agent criteria before estimating a schedule.

## Comments

Charted 2026-10-10. Delivery order and proposed responsiveness/human/agent targets accepted by the user.

## Answer

Accepted by the user on 2026-10-10. Targets are specification gates, not measured
results; operational values below are initial implementation defaults.

Implementation sequence, with evidence at each integration:

1. Runtime spine: extract Core and retain an Editor adapter; create a Linux Player
   build loading the user-provided prepared pack. Prove original floor/wall assets,
   ordinary furniture and audited special-surface rendering without AssetDatabase.
   One UI placement and one local agent placement must share validation, undo and
   JSON persistence. No polygon or broad UI completion claim at this slice.
2. Geometry/document slice: add v2 data with explicit rooms/walls/attachments;
   migrate v1 preserving IDs/transforms. Prove an L-shaped room, a diagonal
   boundary, two adjacent styled areas and a decorated physical wall. Render
   partial modules/corners, derive wallpaper boundaries, reject overlaps/invalid
   wall edits and round-trip the actual rendered scene. This is the main schedule
   risk; do not estimate completion before this evidence and physical input.
3. Linux alpha: complete Build, Rooms, Presets and Assets plus Settings to the
   revised references. On a clean machine without Unity/Python, configure the
   prepared pack, browse full catalogs and Interior pickers, draw/edit areas,
   furnish and group move/rotate/copy, save, close, reopen and export JSON/PNG.
   Native input and dialogs, cancellation/focus loss, unavailable assets and
   automatic recovery must work through the real app.
4. Workflow/visual acceptance: exercise Momo and Abydos inventory/round-trips and
   wall-heavy library/ship compositions. Inspect all four screens and representative
   original material states. Record known defects separately; generated mockups,
   compile results and structural validators do not establish visual acceptance.
   User visual approval remains a separate observed result.
5. Cross-platform beta: repeat installation, path/relink, dialogs/input, room/group
   editing, save/recovery, image/JSON export and the local agent bridge on Windows,
   Linux and macOS. Packaging/upgrade must preserve user data and source packs.
   Upgrades install a new tool build rather than manage game content. Public
   release packaging/signing is target-platform work; do not call it verified
   without target-machine evidence.

Responsiveness gates (targets, not measured claims): record hardware,
resolution, pack fingerprint and layout fixture. At 1920x1080, target 60 fps for
normal editing and at least 30 fps for the densest agreed reference fixture.
Input-to-visible-preview p95 <=100 ms; catalog filter feedback p95 <=200 ms once
loaded. Model/thumbnail work must show progress and permit cancellation; no
unbounded synchronous full-pack load on a UI gesture. Benchmark startup and
first-resource loading separately, then set honest documented startup/cache
budgets from the runtime spine rather than inventing hardware support.

Human/agent gate: an experienced human furnishes the agreed Momo fixture
from an empty cafe in <=15 minutes; an independent agent completes it in <30
minutes using the standalone bridge. Include search, corrections and save/reopen
in timing; capture/render approval is independent. Retain actual times and input
conditions. Adjust fixture quality expectations explicitly if original transforms
are unknown; no automation timing substitutes for a human exercise.

Operational defaults to remove pre-implementation ambiguity: automatic recovery
writes after 2 seconds of idle following a committed edit, with a 30-second
maximum dirty interval during continuous editing. Keep the latest complete
recovery and one previous complete snapshot until Save or explicit Discard;
write errors keep dirty state visible and do not clobber older valid snapshots.
Recovery is revision-checked; these intervals are tunable after actual load tests.
Cache budgets are bounded/configurable and measured in the spine; source pack
files are never rewritten. Initial alpha uses a portable Linux build; beta adds
target-platform installation/uninstallation and a documented manual upgrade path.
An integrated updater is not required to start implementation.

Required failure exercises: unsafe/unknown/duplicate-ID catalogs; missing/corrupt
per-item files and thumbnails; moved asset path; interrupted/stale background load;
invalid/legacy layout imports; cancelled/failed Save/Open/New; group atomic failure;
invalid wall/bounds edit; focus/release loss/Escape; crash during recovery/save;
Discard followed by restart; stale agent revisions; capture of the committed
revision. Verify clean source bytes and exact IDs/transforms/room/style equality
on save/reopen. Reject malformed/nonfinite geometry; show known unavailable IDs.

All evidence records tested commit/tree fingerprint, source metadata/hashes,
fixture, logs, screenshots, timestamps and passed/unverified criteria under
evidence/. Re-run affected checks after integration. Update standalone acceptance
instructions in VERIFICATION.md during implementation while preserving Editor
baselines. No standalone build, test, timings or visual acceptance are claimed by
this planning ticket.

No calendar estimate before the runtime geometry/document and clean-machine input
slices. Once those pass, estimate remaining alpha/beta work from measured progress.
