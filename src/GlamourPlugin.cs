using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Glamour
{
    // Transmog: any of your items can look like another item of the same kind, keeping its own
    // stats. The look is chosen per item in a panel next to the inventory and saved in the item
    // (ItemData.m_customData), so it survives relogging, upgrading and repairing, and stays when
    // the item changes hands.
    //
    // How the look reaches other players: Humanoid.SetupVisEquipment hands VisEquipment the hash
    // of every equipped item's prefab (SetChestItem(hash), SetLeftItem(hash, variant, ...) ...).
    // The owner's VisEquipment writes those hashes into its ZDO, and every other client - modded
    // or not - draws the character from the ZDO. The mod swaps the hash (and variant) before it
    // gets there, for the local player only, so players without the mod see the look too, and
    // the server needs nothing. There is no RPC and no version check: install it if you want it.
    //
    // A look that is not in a viewer's game (an item from a mod they do not have) shows as an
    // empty slot to them; vanilla looks are seen by everyone.
    [BepInPlugin(Guid, Name, Version)]
    public partial class GlamourPlugin : BaseUnityPlugin
    {
        public const string Guid = "j1ga.glamour";
        public const string Name = "Glamour";
        public const string Version = "0.1.0";

        // ItemData.m_customData key: "<prefab>" or "<prefab>|<variant>" for the look, "-" for hidden.
        private const string DataKey = "j1ga.glamour";
        private const string HiddenValue = "-";

        private static GlamourPlugin _instance;

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<bool> _weapons;
        private static ConfigEntry<bool> _tooltip;

        // The equipment of the local player while its SetupVisEquipment runs; null otherwise.
        private static Outfit _dressing;

        private static readonly FieldInfo _visEquipmentField = AccessTools.Field(typeof(Humanoid), "m_visEquipment");
        private static readonly MethodInfo _setupVisEquipment = AccessTools.Method(typeof(Humanoid), "SetupVisEquipment");

        private readonly HashSet<string> _loggedErrors = new HashSet<string>();
        private Harmony _harmony;

        private sealed class Outfit
        {
            public ItemDrop.ItemData Left, Right, LeftBack, RightBack, Chest, Legs, Helmet, Shoulder, Utility, Trinket;
        }

        private void Awake()
        {
            _instance = this;
            _enabled = Config.Bind("01 General", "Enabled", true,
                "Off: every item shows its own look again (the looks stay saved in the items).");
            _weapons = Config.Bind("01 General", "Weapons", true,
                "Weapons, shields, torches and tools can take a look too (of the same kind and grip only).");
            _tooltip = Config.Bind("01 General", "Tooltip", true, "The item tooltip says which look the item wears.");
            _enabled.SettingChanged += delegate { Refresh(); };
            _weapons.SettingChanged += delegate { Refresh(); };
            BindUiConfig();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(GlamourPlugin).Assembly);
            Logger.LogInfo(Name + " " + Version + " loaded.");
        }

        private void OnDestroy()
        {
            if (_harmony != null) _harmony.UnpatchSelf();
            DestroyUi();
        }

        private void Update()
        {
            try
            {
                UpdateUi();
            }
            catch (Exception e)
            {
                Fail("Update", e);
            }
        }

        // ------------------------------------------------------------------
        // What can look like what
        // ------------------------------------------------------------------

        // Why item cannot take the look of an item with the shared data look, or null when it can.
        private static string WhyNot(ItemDrop.ItemData item, ItemDrop.ItemData.SharedData look)
        {
            ItemDrop.ItemData.SharedData a = item.m_shared;
            if (!HasLookSlot(a.m_itemType) || item.m_dropPrefab == null) return "cannot take a look";
            if (a.m_itemType != look.m_itemType) return "a different kind of item";
            if (IsHand(a.m_itemType))
            {
                if (!_weapons.Value) return "looks for weapons are off (Weapons in the config)";
                // The grip decides the animations; the look of another grip would be held wrong.
                if (a.m_animationState != look.m_animationState) return "held differently";
                // The fishing line is tied to the rod's own model.
                if (a.m_animationState == ItemDrop.ItemData.AnimationState.FishingRod) return "fishing rods keep their own look";
            }
            return null;
        }

        private static bool HasLookSlot(ItemDrop.ItemData.ItemType type)
        {
            switch (type)
            {
                case ItemDrop.ItemData.ItemType.Helmet:
                case ItemDrop.ItemData.ItemType.Chest:
                case ItemDrop.ItemData.ItemType.Legs:
                case ItemDrop.ItemData.ItemType.Shoulder:
                case ItemDrop.ItemData.ItemType.Utility:
                case ItemDrop.ItemData.ItemType.Trinket:
                    return true;
                default:
                    return IsHand(type);
            }
        }

        private static bool IsHand(ItemDrop.ItemData.ItemType type)
        {
            switch (type)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Shield:
                case ItemDrop.ItemData.ItemType.Torch:
                case ItemDrop.ItemData.ItemType.Tool:
                    return true;
                default:
                    return false;
            }
        }

        private static bool CanHide(ItemDrop.ItemData.ItemType type)
        {
            return type == ItemDrop.ItemData.ItemType.Helmet || type == ItemDrop.ItemData.ItemType.Shoulder
                || type == ItemDrop.ItemData.ItemType.Utility || type == ItemDrop.ItemData.ItemType.Trinket;
        }

        // ------------------------------------------------------------------
        // The look stored in the item
        // ------------------------------------------------------------------
        private static string GetLook(ItemDrop.ItemData item)
        {
            if (item == null || item.m_customData == null) return null;
            string value;
            return item.m_customData.TryGetValue(DataKey, out value) && !string.IsNullOrEmpty(value) ? value : null;
        }

        private static void SetLook(ItemDrop.ItemData item, string value)
        {
            if (item.m_customData == null) item.m_customData = new Dictionary<string, string>();
            if (value == null) item.m_customData.Remove(DataKey);
            else item.m_customData[DataKey] = value;
            if (item.m_equipped) Refresh();
        }

        private static string LookValue(string prefab, int variant)
        {
            return variant >= 0 ? prefab + "|" + variant : prefab;
        }

        private static void ParseLook(string value, out string prefab, out int variant)
        {
            variant = -1;
            int bar = value.IndexOf('|');
            if (bar < 0)
            {
                prefab = value;
                return;
            }
            prefab = value.Substring(0, bar);
            int v;
            if (int.TryParse(value.Substring(bar + 1), out v)) variant = v;
        }

        // The look of an equipped item as VisEquipment takes it. False: show the item itself.
        private static bool Resolve(ItemDrop.ItemData item, out string prefab, out int variant)
        {
            prefab = null;
            variant = -1;
            string value = GetLook(item);
            if (value == null) return false;
            ItemDrop.ItemData.ItemType type = item.m_shared.m_itemType;
            if (IsHand(type) && !_weapons.Value) return false;
            if (value == HiddenValue)
            {
                if (!CanHide(type)) return false;
                prefab = "";
                return true;
            }
            ParseLook(value, out prefab, out variant);
            // A look whose item is gone (its mod removed): the item's own look.
            if (ObjectDB.instance == null || ObjectDB.instance.GetItemPrefab(prefab) == null) return false;
            return true;
        }

        // The display name of a stored look; null for none.
        private static string LookName(string value)
        {
            if (value == null) return null;
            if (value == HiddenValue) return "hidden";
            string prefab;
            int variant;
            ParseLook(value, out prefab, out variant);
            GameObject go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            if (drop == null) return prefab + " (missing)";
            string name = Localize(drop.m_itemData.m_shared.m_name);
            if (variant >= 0 && drop.m_itemData.m_shared.m_variants > 1) name += " (style " + (variant + 1) + ")";
            return name;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        // Sets the local player's equipment up again, so a changed look shows (and reaches the
        // ZDO) at once.
        private static void Refresh()
        {
            try
            {
                Player player = Player.m_localPlayer;
                if (player == null || _visEquipmentField == null || _setupVisEquipment == null) return;
                VisEquipment vis = _visEquipmentField.GetValue(player) as VisEquipment;
                if (vis == null) return;
                _setupVisEquipment.Invoke(player, new object[] { vis, false });
            }
            catch (Exception e)
            {
                if (_instance != null) _instance.Fail("Refresh", e);
            }
        }

        private static string Localize(string text)
        {
            return Localization.instance != null ? Localization.instance.Localize(text) : text;
        }

        private static string ItemName(ItemDrop.ItemData item)
        {
            return Localize(item.m_shared.m_name);
        }

        private void Fail(string where, Exception e)
        {
            string key = where + ": " + e.GetType().Name + ": " + e.Message;
            if (_loggedErrors.Add(key)) Logger.LogError(key + "\n" + e.StackTrace);
        }

        // ------------------------------------------------------------------
        // Harmony
        // ------------------------------------------------------------------

        // The only place the equipment reaches VisEquipment. For the local player (or the
        // character preview in the main menu, where there is no local player) the equipped items
        // are noted while it runs, and the VisEquipment setters below swap the looks in.
        [HarmonyPatch(typeof(Humanoid), "SetupVisEquipment")]
        private static class Humanoid_SetupVisEquipment_Patch
        {
            private static void Prefix(Humanoid __instance, out Outfit __state,
                ItemDrop.ItemData ___m_leftItem, ItemDrop.ItemData ___m_rightItem,
                ItemDrop.ItemData ___m_hiddenLeftItem, ItemDrop.ItemData ___m_hiddenRightItem,
                ItemDrop.ItemData ___m_chestItem, ItemDrop.ItemData ___m_legItem, ItemDrop.ItemData ___m_helmetItem,
                ItemDrop.ItemData ___m_shoulderItem, ItemDrop.ItemData ___m_utilityItem, ItemDrop.ItemData ___m_trinketItem)
            {
                __state = _dressing;
                Player player = __instance as Player;
                if (!_enabled.Value || player == null || (Player.m_localPlayer != null && !ReferenceEquals(player, Player.m_localPlayer)))
                {
                    _dressing = null;
                    return;
                }
                Outfit outfit = new Outfit();
                outfit.Left = ___m_leftItem;
                outfit.Right = ___m_rightItem;
                outfit.LeftBack = ___m_hiddenLeftItem;
                outfit.RightBack = ___m_hiddenRightItem;
                outfit.Chest = ___m_chestItem;
                outfit.Legs = ___m_legItem;
                outfit.Helmet = ___m_helmetItem;
                outfit.Shoulder = ___m_shoulderItem;
                outfit.Utility = ___m_utilityItem;
                outfit.Trinket = ___m_trinketItem;
                _dressing = outfit;
            }

            private static Exception Finalizer(Exception __exception, Outfit __state)
            {
                _dressing = __state;
                return __exception;
            }
        }

        private static void Swap(ItemDrop.ItemData item, ref int itemHash)
        {
            string prefab;
            int variant;
            if (item == null || !Resolve(item, out prefab, out variant)) return;
            itemHash = prefab.Length == 0 ? 0 : prefab.GetStableHashCode();
        }

        private static void Swap(ItemDrop.ItemData item, ref int itemHash, ref int itemVariant)
        {
            string prefab;
            int variant;
            if (item == null || !Resolve(item, out prefab, out variant)) return;
            itemHash = prefab.Length == 0 ? 0 : prefab.GetStableHashCode();
            itemVariant = variant >= 0 ? variant : 0;
        }

        [HarmonyPatch(typeof(VisEquipment), "SetLeftItem")]
        private static class VisEquipment_SetLeftItem_Patch
        {
            private static void Prefix(ref int __0, ref int __1)
            {
                if (_dressing != null) Swap(_dressing.Left, ref __0, ref __1);
            }
        }

        [HarmonyPatch(typeof(VisEquipment), "SetRightItem")]
        private static class VisEquipment_SetRightItem_Patch
        {
            private static void Prefix(ref int __0)
            {
                if (_dressing != null) Swap(_dressing.Right, ref __0);
            }
        }

        [HarmonyPatch(typeof(VisEquipment), "SetLeftBackItem")]
        private static class VisEquipment_SetLeftBackItem_Patch
        {
            private static void Prefix(ref int __0, ref int __1)
            {
                if (_dressing != null) Swap(_dressing.LeftBack, ref __0, ref __1);
            }
        }

        [HarmonyPatch(typeof(VisEquipment), "SetRightBackItem")]
        private static class VisEquipment_SetRightBackItem_Patch
        {
            private static void Prefix(ref int __0)
            {
                if (_dressing != null) Swap(_dressing.RightBack, ref __0);
            }
        }

        [HarmonyPatch(typeof(VisEquipment), "SetChestItem")]
        private static class VisEquipment_SetChestItem_Patch
        {
            private static void Prefix(ref int __0)
            {
                if (_dressing != null) Swap(_dressing.Chest, ref __0);
            }
        }

        [HarmonyPatch(typeof(VisEquipment), "SetLegItem")]
        private static class VisEquipment_SetLegItem_Patch
        {
            private static void Prefix(ref int __0)
            {
                if (_dressing != null) Swap(_dressing.Legs, ref __0);
            }
        }

        [HarmonyPatch(typeof(VisEquipment), "SetHelmetItem")]
        private static class VisEquipment_SetHelmetItem_Patch
        {
            private static void Prefix(ref int __0)
            {
                if (_dressing != null) Swap(_dressing.Helmet, ref __0);
            }
        }

        [HarmonyPatch(typeof(VisEquipment), "SetShoulderItem")]
        private static class VisEquipment_SetShoulderItem_Patch
        {
            private static void Prefix(ref int __0, ref int __1)
            {
                if (_dressing != null) Swap(_dressing.Shoulder, ref __0, ref __1);
            }
        }

        [HarmonyPatch(typeof(VisEquipment), "SetUtilityItem")]
        private static class VisEquipment_SetUtilityItem_Patch
        {
            private static void Prefix(ref int __0)
            {
                if (_dressing != null) Swap(_dressing.Utility, ref __0);
            }
        }

        // The trinket goes by prefab name, not hash.
        [HarmonyPatch(typeof(VisEquipment), "SetTrinketItem")]
        private static class VisEquipment_SetTrinketItem_Patch
        {
            private static void Prefix(ref string __0)
            {
                string prefab;
                int variant;
                if (_dressing == null || _dressing.Trinket == null || !Resolve(_dressing.Trinket, out prefab, out variant)) return;
                __0 = prefab;
            }
        }

        // "Look: <item>" / "Look: hidden" at the end of the tooltip.
        [HarmonyPatch]
        private static class ItemData_GetTooltip_Patch
        {
            private static MethodBase TargetMethod()
            {
                foreach (MethodInfo m in typeof(ItemDrop.ItemData).GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != "GetTooltip" || m.ReturnType != typeof(string)) continue;
                    ParameterInfo[] p = m.GetParameters();
                    if (p.Length > 0 && p[0].ParameterType == typeof(ItemDrop.ItemData)) return m;
                }
                return null;
            }

            private static void Postfix(ItemDrop.ItemData item, ref string __result)
            {
                if (!_tooltip.Value || item == null) return;
                string name = LookName(GetLook(item));
                if (name == null) return;
                string state = _enabled.Value ? "" : " (Glamour is off)";
                __result += "\n\n<color=#C9A0FF>Look: " + name + state + "</color>";
            }
        }
    }
}
