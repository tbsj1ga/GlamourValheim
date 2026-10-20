# Changelog

**English** · [Русский](CHANGELOG-RU.md)

The version is set in one place — `GlamourPlugin.Version` in `src/GlamourPlugin.cs`.

## 0.1.0

- First release. Armor, weapons and shields can look like another item of the same kind and
  keep their stats; helmets, capes, belts and trinkets can be hidden.
- The look is saved in the item and reaches other players through the game's own equipment
  sync: players without the mod see it, the server needs nothing.
- A Glamour window beside the inventory: your equipped items at the top, the looks they can
  take below (own, hidden, every item of the kind you have found); one click puts a look on.
  Built from the game's own inventory cells, buttons and font; draggable.
- The item tooltip shows the look.
