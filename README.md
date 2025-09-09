# Stagger Tuner

Created out of a want to play Hard or Very_Hard difficulty without feeling like needing to focus on only dodging or playing specific weapon/shield combos.  If I wanted to play Dark Souls I would go play that :-)

## What it does
- Multiplies `Character.GetStaggerTreshold()` **for players only**.
- Default multiplier: **1.60x** (≈60% larger bar). Configurable.  **note the bar does not appear larger**
    - 1.60 multiplier is what felt good for me at hard/very_hard however I could see 1.3 - 1.4 for someone who is at a higher skill level
    - No changes to outgoing/incoming HP damage. No per-hit hooks by default.
- **ServerSync** support:
  - Server can lock and broadcast settings.
  - `ModRequired = true` by default: clients must have the mod.
- Full damage if you miss a parry or block so you will still be punished for getting hit.



This is my first mod hopefully it helps you enjoy the game a bit more

# Contact
Discord: jello_cf