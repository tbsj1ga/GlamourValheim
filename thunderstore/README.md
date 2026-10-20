# Glamour

**Transmog for Valheim.** Wear the best armor and look like whatever you like: your carapace
chest can look like a troll leather tunic, your blackmetal sword like a bronze one, your cape
can disappear. Stats, set bonuses and attacks stay the item's own — only the model changes.

**Players without the mod see your look too.** Glamour swaps the item your game reports for
your character's model, and the game sends that to everyone through its own sync. No version
check: install it if you want it; your friends, the host and a dedicated server need nothing.

## How to use

Open the inventory (a chest can be open too) and point at items:

- `T` — pick an item's look; `T` again over another item of the same kind puts the look on it.
- `Shift + T` — back to the item's own look.
- `Ctrl + T` — hide / show a helmet, cape, belt or trinket.

The tooltip shows which look an item wears.

- The look is saved in the item: it stays after relogging, upgrades and repairs, in chests and
  when you give the item away.
- Same kind only: chest for chest, cape for cape. Weapons, shields, torches and tools also need
  the same grip, so the animations fit (a sword can look like an axe, not like a spear).
- The look item is only needed when you pick its look — afterwards it can go.
- A look from another mod's item shows as an empty slot to players who lack that mod; vanilla
  looks are seen by everyone.
- Do not run it together with another transmog mod.

## Settings

`BepInEx/config/j1ga.glamour.cfg`, or in-game with ConfigurationManager (F1):

- `Enabled` — on / off (the looks stay saved).
- `Weapons` — looks for weapons, shields, torches and tools.
- `Tooltip` — the look line in the tooltip.
- `LookKey`, `ResetKey`, `HideKey` — the keys.
