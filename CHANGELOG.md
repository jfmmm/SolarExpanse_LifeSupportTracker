# Changelog

All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Fixed
- LEFT column no longer shows a countdown for colonies that produce as much supply as they consume. Time-to-depletion was computed as `supply / consumption`, ignoring the body's own production, so a colony producing 1.3 T/d against 0.31 T/d of consumption still reported ~1.7y remaining even though its stockpile was growing. The runway is now based on net drain (consumption − production), and a non-positive net drain reports an infinite runway (`∞`).
- Status rows with equal time remaining now sort by body name instead of in an arbitrary order. Bodies with an infinite runway all tie at the bottom of the ranking, and `List.Sort` is unstable, so that group could reshuffle between refreshes.

## [1.4.1] - 2026-06-16

### Fixed
- Crashed/destroyed asteroids no longer appear as ghost entries with their colonists still counted. The game keeps destroyed bodies in its master object list (`allObjectInfos`) with crew intact and only hides them visually, so colonists on an asteroid that crashed into a planet kept showing in the panel. Bodies are now filtered out using the game's `IsInGameDestroy` flag.

## [1.4.0] - 2026-05-25

### Added
- ESC key closes the panel.

### Fixed
- LIFE SUPPORT button snaps back to correct relative position after screen resize or fullscreen toggle instead of drifting to its old absolute coordinates.

## [1.3.0] - 2026-05-18
### Added
- INCOME column showing daily colonist income (`42k$/d`, `1.2M$/d`) for each body, between CONS/DAY and LEFT. Uses reflection for safe stable/beta compatibility — shows `—` on stable builds that don't support the property.
- Ship life support now includes cargo supply in addition to scheduled supply, so ships carrying supply crates report correct days remaining.

### Changed
- Refresh is now driven by a `TrackerUpdater` component on the always-active indicator GameObject instead of `Update()` on the panel, which is inactive when closed. Panel refreshes every 5 seconds even while closed so data is current when reopened.

## [1.2.0] - 2026-05-17
### Fixed
- Status tab no longer destroys and recreates all UI rows on every 5-second refresh. Rows are now cached and updated in place, eliminating per-tick GameObject allocation and TMP mesh rebuilds.
- Layout rebuild is now skipped when only cell values change (no rows added or removed), reducing main-thread stalls on large colony lists.
- Removed three `LogInfo` calls that fired on every refresh cycle.

## [1.1.0] - 2026-05-12
### Changed
- SETTINGS tab renamed to ALERT THRESHOLDS.
- Threshold inputs split into years + days fields (e.g. `2 yrs 0 d`) for easier entry of long-duration orbits.
- Threshold values of 0 are now valid — set both WARN and CRIT to 0 to disable alerts for a body.
- Input fields now normalize immediately on focus-out (e.g. typing 730 days shows as 2 yrs 0 d).

## [1.0.0] - 2026-05-12
### Added
- Floating panel showing life support supply levels (food, water, oxygen) for all active colonies.
- Per-body alert thresholds (days until critical / days until warning) with persistent BepInEx config.
- SETTINGS tab for configuring thresholds per body with a global default.
- Color-coded rows: green / yellow / red based on threshold proximity.
- Draggable, resizable panel that clamps to screen bounds on resize.
- Panel follows its toggle button during drag.
