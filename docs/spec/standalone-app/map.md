# Standalone app specification

Label: wayfinder:map
Status: resolved

## Destination

An implementation-ready BlueCafe standalone desktop specification covering app
behavior, data contracts, acceptance checks and delivery milestones, with no
remaining decisions required to begin the specified work.

## Notes

- Planning only; implementation is a subsequent effort.
- Agreed scope and user decisions are recorded in
  [Complete standalone app](../../STANDALONE.md).
  These are the starting baseline, not tickets resolved during charting.
- Follow the UI references (references retained locally), including
  the documented supersession of the Downloads mockup.
- Consult wayfinder, grilling and domain-modeling each session. Consult
  docs/VERIFICATION.md before implementation and before reporting completion;
  docs/CONTRACTS.md before changing an integration boundary.
- Local Markdown tracker: each issue has Type, Status, Assignee and Blocked by.
  Open, unassigned children whose blockers are resolved form the frontier;
  choose the first by filename. Claim before work. Resolution is appended under
  Answer, status becomes resolved, and a named context link is added below.
- Agreed architecture: Unity desktop host with a shared authoring core, Unity
  presentation, document storage, local asset loading and an agent adapter.
  Boundaries are specified; standalone implementation is not claimed.
- Handoff: [Standalone app specification](spec.md).
- Use established game mechanics and existing approved behavior as defaults;
  investigate factual questions rather than asking the user to answer them. Ask
  only for genuinely new product decisions. Distinguish verified source behavior
  from BlueCafe extensions and provisional defaults.
- Linux alpha first, Windows/macOS beta. Keep source/downloaded content separate.
- The orchestrator owns the live Editor and integration. Delegate independent
  implementation; workers must not launch Editors or mutate scene/asset YAML.

## Decisions so far

- [Local asset input contract](issues/01-local-asset-contract.md): prepared packs only; Assets explains expected contents; no raw-file support claims.

- [Room areas and physical wall relationships](issues/02-room-wall-model.md): non-overlapping simple polygons, unchanged furniture, derived room wallpaper and protected wall attachments.

- [Single cafe replacement and recovery](issues/03-document-lifecycle.md): My cafes saves, portable exports and separate automatic recovery; cancelled/failed replacement preserves work.

- [Atomic group editing and attachment behavior](issues/04-authoring-operations.md): rigid quarter-turn group rotation, atomic validation/copy, and explicit split/merge style inheritance.

- [Local pack validation and relocation](issues/07-pack-validation.md): read-only prepared packs, compatible path resolution, explicit partial availability and safe relinking.

- [Runtime architecture and shared agent boundary](issues/05-runtime-boundaries.md): one Unity host, authoritative model, atomic shared dispatcher and separate runtime/storage/Editor adapters.

- [Standalone delivery sequence and acceptance](issues/06-delivery-gates.md): runtime/geometry proofs, Linux alpha, visual and timed checks, then cross-platform beta.

## Not yet specified

None blocking the specified implementation. Startup/cache budgets and delivery
estimates are measured during the runtime spine; they are execution outputs, not
silently assumed product capabilities. Runtime platform, geometry, renderer,
physical input and recovery feasibility remain unverified until implementation.

## Out of scope

Asset downloading, an acquisition helper and raw game file import/conversion; multiple simultaneously open cafes;
mobile/browser delivery; game avatars, animation and particles; full reconstruction
fidelity as a prerequisite for standalone development. Existing local acquisition
tooling is preserved, but it is not shipped as an app feature.
