# Glamour

**English** · [Русский](README-RU.md)

A Valheim mod: transmog. Any armor piece, weapon or shield can look like another item of the
same kind and keep its own stats. Helmets, capes, belts and trinkets can be hidden. Client-side
only: **players without the mod see your look too**, and the host and a dedicated server need
nothing.

Version history is in `CHANGELOG.md`.

## How to use

Open the inventory (a chest can be open too) and point at items:

| Key | Over an item |
|---|---|
| `T` | first press: pick this item's look; second press over another item of the same kind: put the look on it |
| `T` over the picked item | drop the pick |
| `Shift + T` | back to the item's own look |
| `Ctrl + T` | hide / show a helmet, cape, belt or trinket |

The item's tooltip says which look it wears. Putting an item's own look on it resets it too.

- **The look belongs to the item.** It is saved in the item (`ItemData.m_customData`), so it
  stays after relogging, upgrading and repairing, and when the item goes into a chest or to
  another player. A new item wears its own look until you give it one.
- **Same kind only.** A chest piece takes the look of a chest piece, a cape of a cape. Weapons,
  shields, torches and tools also need the same grip (the same animations): a sword can look
  like an axe or a mace, not like a spear or a bow. Fishing rods keep their own look.
- The look item only has to be at hand when you pick its look: from your inventory or from the
  open chest. Afterwards you may store it, sell it or throw it away.

## How it works

`Humanoid.SetupVisEquipment` is the one place the game passes your equipment to the model: for
every slot it calls `VisEquipment.SetChestItem(hash)`, `SetLeftItem(hash, variant, quality)`
and so on, with the hash of the equipped item's prefab. Your `VisEquipment` writes those hashes
into your character's ZDO, and every other client — modded or not — draws you from the ZDO.

While `SetupVisEquipment` runs for the local player, a prefix notes the equipped items; prefixes
on the ten `VisEquipment` setters swap each hash (and the variant of shields and capes) for
the look's, or for 0 to hide the slot. The swapped hash is what goes into the ZDO, so the
look travels by the game's own sync. There is no RPC and no Jotunn version check.

**What players without the mod see.** Vanilla looks: exactly what you see. A look from another
mod's item (Therzie, Southsil...): an empty slot for a player who lacks that mod, because their
game has no such prefab.

**What stays the item's own.** Stats, armor, set bonuses, durability, attacks and their
animations — only the model changes. A look whose item is gone (its mod removed) falls back
to the item's own look; the look stays saved.

## Compatibility

Built against **Valheim 1.0.20** (network version 41), **BepInEx 5.4.23.5**
(BepInExPack_Valheim 5.4.2351). Do not run it together with another transmog mod (Alpus
Transmog and the like): both swap the same calls. Alpus Transmog also demands that everyone
has it (Jotunn `EveryoneMustHaveMod`); Glamour does not.

## Who needs it

| Who | What |
|---|---|
| A player who wants a look | installs it |
| Other players | nothing — they see the look |
| The host, a dedicated server | nothing |

## Bugs and feedback

https://github.com/tbsj1ga/GlamourValheim/issues

## Installation

Through r2modman / Thunderstore, or put `build/Glamour.dll` into

```
%AppData%\r2modmanPlus-local\Valheim\profiles\Valheim\BepInEx\plugins\Glamour\
```

(or `build.ps1 -Install`).

## Settings

`BepInEx/config/j1ga.glamour.cfg`, or in-game with ConfigurationManager (F1). Changes apply at
once.

| Section | Key | Default | What |
|---|---|---|---|
| 01 General | `Enabled` | `true` | off: every item shows its own look (the looks stay saved) |
| 01 General | `Weapons` | `true` | weapons, shields, torches and tools can take a look |
| 01 General | `Tooltip` | `true` | the tooltip says which look the item wears |
| 02 Keys | `LookKey` | `T` | pick a look / put it on |
| 02 Keys | `ResetKey` | `T + LeftShift` | back to the item's own look |
| 02 Keys | `HideKey` | `T + LeftControl` | hide / show a helmet, cape, belt or trinket |

## Building

```
powershell -ExecutionPolicy Bypass -File .\build.ps1            # build and check references
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Install   # ... and copy into plugins
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Package   # ... and make the Thunderstore zip
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Nexus     # ... zip, and the Nexus Mods kit
```

The compiler is `csc.exe` from the .NET Framework (C# 5); references come straight from the
game folder and the r2modman profile's `BepInEx\core`. After the build, `check-refs.ps1` checks
every reference and Harmony patch target against the game.

## Repository

Branch `main` on GitHub: https://github.com/tbsj1ga/GlamourValheim.

## Credits and AI assistance

Glamour is written from scratch. This mod was developed with the help of an AI assistant
(Claude by Anthropic); the design decisions, in-game testing and releases are the author's.
