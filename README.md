# Character Stats

Stats and resource pools for RPG characters. A **StatSheet** on a character holds base values, modifiers (each with a
source, so whatever added them can take them off again) and pools such as stamina, health and exhaustion. It knows
nothing about abilities, movement or items, so it fits any game.

## Requirements

- Unity 6.3 (6000.3) or newer
- **Gameplay Tags** (`com.aidancmays.gameplay-tags`). Add it from disk first: Package Manager only finds it if it is
  already in the project.
- Input System is optional (only for the F5 debug panel's hotkey; it falls back to the old Input Manager).

## Install

Package Manager > **+** > *Add package from disk...*: Gameplay Tags' `package.json`, then this one's.

## Assets (Create > Character Stats)

| Asset | What it is |
|---|---|
| **Stat** | One kind of number: Endurance, Max Stamina, Fire Resistance. Default base, min and max limits. *Inherits From* groups stats, so a modifier on Resistance also applies to Fire Resistance. |
| **Pool** | A quantity that is spent and comes back. Every behaviour is a stat (max, regen, regen delay), so all of it can be modified. Rests at max (health) or zero (exhaustion); can go negative and *overdraw into* another pool; *overflows into* another pool (exhaustion past its cap hurting health); *reserved by* another pool (exhaustion eating the stamina bar); tags while full or overdrawn. |
| **Profile** | What a kind of character starts with: base values, pools, and innate modifiers (max stamina = 10 x endurance). |

## The StatSheet

- `Get(stat)`: the everyday value (cached). `Get(stat, new StatQuery(tags, other))`: the value for one situation, so
  modifiers conditioned on the action's tags or the other party's tags apply.
- `AddModifier(spec, source)`, `AddModifiers(specs, source)`, `RemoveModifiers(source)`. A **StatModifierSpec** is
  Flat, Percent Add, Percent Multiply or Override, optionally scaled by another stat and conditional on tags.
  Final value = (base + Flat) x (1 + sum of Percent Add) x each (1 + Percent Multiply), then Override, then limits.
- `SetBase`, `GetBase`, `StatChanged` and `PoolChanged` events.
- `TryGetPool(definition, out pool)`: a **ResourcePool** with `Current`, `Max`, `Capacity`, `Reserved`,
  `CanPay(cost)`, `Spend(amount, allowOverdraw, source)`, `Restore(amount, source)`, `PriceOf(cost, tags)` (the
  pool's cost multiplier stat, read with the cost's tags), and `Changed`, `FullChanged`, `OverdrawnChanged` events.
- `Capture()` / `Restore(snapshot)` for saves (base values and pool amounts by id; modifiers are re-added by whatever
  granted them).
- `Setup(profile)` for a sheet added from code.

Put a **TagHolder** next to the StatSheet for tag conditions and pool tags.

## Debug panel

Add **Stats Debug Panel** to the character (editor and development builds; F5 toggles): pools as bars, every stat
with base and final value, tags held, and a log of spends and restores. `StatsDebugPanel.DrawSheet(sheet)` draws the
same inside your own overlay.

## Sample

*Stamina and Exhaustion* (Package Manager > Character Stats > Samples): add **Stats Sample Controller** to an empty
GameObject and press Play. Buttons spend stamina, take damage and put on a ring; watch exhaustion build, eat the
stamina bar, block costs at its cap and hurt.
