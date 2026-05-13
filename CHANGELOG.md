# Changelog

All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

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
