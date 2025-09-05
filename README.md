# Stagger Tuner

**Goal:** Make Hard/VeryHard feel fair without forcing sword+board. This mod **only** scales the **player’s stagger threshold** , so you can block/parry more than once without instantly eating dirt. HP damage stays vanilla.

## What it does
- Multiplies `Character.GetStaggerTreshold()` **for players only**.
- Default multiplier: **1.60x** (≈60% larger bar). Configurable.  **note the bar does not appear larger**
- No changes to outgoing/incoming HP damage. No per-hit hooks by default.
- **ServerSync** support:
  - Server can lock and broadcast settings.
  - `ModRequired = true` by default: clients must have the mod.

## Config (BepInEx config)
File: `BepInEx/config/vh.staggertuner.cfg`
- `General.Enabled` (bool, default `true`)
- `Tuning.StaggerThresholdMultiplier` (float, default `1.60`, range `0.50`–`3.00`)
- `Server Sync.Lock Configuration` (bool, default `true` on server)

> This is my first mod so far no issues have come up during testing just was tired of feeling forced into a "meta" on hard/very_hard 


