# Glamour

**English** · [Русский](README-RU.md)

A Valheim mod: transmog. Any armor piece, weapon or shield can look like another item of the
same kind and keep its own stats. Helmets, capes, belts and trinkets can be hidden. Client-side
only: **players without the mod see your look too**, and the host and a dedicated server need
nothing.

Version history is in `CHANGELOG.md`.

## How to use

Open the inventory and press the **Glamour** button at the top of the screen, left of the
minimap (`ButtonPlace = Inventory` puts it beside the inventory panel). The window shows:

- **at the top, your equipped items** — helmet, chest, legs, cape, belt, trinket and what is in
  your hands — each drawn in the look it wears; click one to choose it;
- **below, the looks it can take**: *Own look*, *Hidden* (helmet, cape, belt, trinket), then
  every item of the same kind you have found, by name. Click one and your character wears it
  at once. The current look is framed.

Hover a cell for its name. The window can be dragged anywhere and remembers the spot; the item
tooltip in the inventory says which look an item wears.

- **The look belongs to the item.** It is saved in the item (`ItemData.m_customData`), so it
  stays after relogging, upgrading and repairing, and when the item goes into a chest or to
  another player. A new item wears its own look until you give it one.
- **Same kind only.** A chest piece takes the look of a chest piece, a cape of a cape. Weapons,
  shields, torches and tools also need the same grip (the same animations): a sword can look
  like an axe or a mace, not like a spear or a bow. Fishing rods keep their own look.
- **Which looks.** By default the looks of items you have picked up at least once (the game
  remembers them) or carry now — you do not need to keep the item. `Looks = All` offers every
  item of the kind. Shields and capes with styles offer each style.

## How it works

`Humanoid.SetupVisEquipment` is the one place the game passes your equipment to the model: for
every slot it calls `VisEquipment.SetChestItem(hash)`, `SetLeftItem(hash, variant, quality)`
and so on, with the hash of the equipped item's prefab. Your `VisEquipment` writes those hashes
into your character's ZDO, and every other client — modded or not — draws you from the ZDO.

While `SetupVisEquipment` runs for the local player, a prefix notes the equipped items; prefixes
on the ten `VisEquipment` setters swap each hash (and the variant of shields and capes) for
the look's, or for 0 to hide the slot. The swapped hash is what goes into the ZDO, so the
look travels by the game's own sync. There is no RPC and no Jotunn version check.

The window is built from the game's own parts: its cells are copies of the inventory grid's
cell (frame, icon, selection frame, the game's tooltip), its buttons copies of the container's
*Take all* button, its text in the game's font. It lives under the inventory's root, so it
shows and hides with the inventory.

**What players without the mod see.** Vanilla looks: exactly what you see. A look from another
mod's item (Therzie, Southsil...): an empty slot for a player who lacks that mod, because their
game has no such prefab.

**What stays the item's own.** Stats, armor, set bonuses, durability, attacks and their
animations — only the model changes. A look whose item is gone (its mod removed) falls back
to the item's own look; the look stays saved.

## Compatibility

- **HudLayout**: the button and the window register through its API (by reflection, HudLayout
  stays optional) as *Glamour button* and *Glamour window*. Open the inventory and the window,
  then HudLayout's editor, to move, scale or fade them.

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
| 02 Panel | `Looks` | `Found` | `Found`: items you have picked up or carry; `All`: every item of the kind |
| 02 Panel | `ButtonPlace` | `Minimap` | `Minimap`: at the top of the screen, left of the minimap; `Inventory`: beside the inventory panel's top right corner |
| 02 Panel | `ButtonOffset` | `0, 0` | moves the button from that place, in pixels |
| 02 Panel | `PanelOffset` | `0, 0` | where the window opens; dragging the window sets it |
| 02 Panel | `ApplyToAllOfKind` | `false` | on: a look chosen for an item goes onto every item of the same kind you carry (same slot; held items also the same grip) — own look and *Hidden* too |

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
