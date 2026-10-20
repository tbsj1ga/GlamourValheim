using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Glamour
{
    public partial class GlamourPlugin
    {
        // ------------------------------------------------------------------
        // The panel
        //
        // A "Glamour" button on the player's inventory panel opens a window beside it: at the top
        // the equipped items (each drawn in the look it wears), below the looks the selected item
        // can take - its own look, "hidden" for a helmet, cape, belt or trinket, and every item
        // of the same kind you have found. A click puts the look on.
        //
        // Built from the game's own parts so it looks native: the cells are copies of the
        // inventory grid's element (frame, icon, selection, the game's tooltip), the buttons copies
        // of the container's "Take all" button, the text the game's font, the window the
        // inventory's background. Made once per InventoryGui, under its root, so it shows and
        // hides with the inventory.
        // ------------------------------------------------------------------
        public enum LookSource { Found, All }

        private static ConfigEntry<LookSource> _cfgLooks;
        private static ConfigEntry<Vector2> _cfgButtonOffset;
        private static ConfigEntry<Vector2> _cfgPanelOffset;

        private const int GridColumns = 8;
        private const int GridRows = 4;
        private const float Pad = 14f;

        private InventoryGui _uiGui;
        private RectTransform _button;
        private RectTransform _panel;
        private RectTransform _slotRow;
        private RectTransform _gridContent;
        private ScrollRect _scroll;
        private TextMeshProUGUI _infoText, _hintText;
        private bool _panelOpen;
        private float _cell = 70f;

        private ItemDrop.ItemData _selected;
        private readonly List<ItemDrop.ItemData> _slotItems = new List<ItemDrop.ItemData>();
        private readonly List<ItemDrop.ItemData> _shownSlots = new List<ItemDrop.ItemData>();
        private readonly List<string> _shownLooks = new List<string>();

        private static readonly ItemDrop.ItemData.ItemType[] ArmorSlots =
        {
            ItemDrop.ItemData.ItemType.Helmet, ItemDrop.ItemData.ItemType.Chest, ItemDrop.ItemData.ItemType.Legs,
            ItemDrop.ItemData.ItemType.Shoulder, ItemDrop.ItemData.ItemType.Utility, ItemDrop.ItemData.ItemType.Trinket
        };

        private sealed class LookEntry
        {
            public string Value;      // null: the item's own look
            public string Name;
            public Sprite Icon;
            public bool Dim;
        }

        private void BindUiConfig()
        {
            _cfgLooks = Config.Bind("02 Panel", "Looks", LookSource.Found,
                "Found: the looks of items you have picked up at least once (or carry). All: every item of the kind in the game.");
            _cfgButtonOffset = Config.Bind("02 Panel", "ButtonOffset", new Vector2(8f, 0f),
                "Where the Glamour button sits: offset from the top right corner of the inventory panel.");
            _cfgPanelOffset = Config.Bind("02 Panel", "PanelOffset", new Vector2(0f, 0f),
                "Where the window opens, relative to its place beside the inventory. Dragging the window sets it.");
            _cfgLooks.SettingChanged += delegate { if (_instance != null) _instance.RebuildLooks(); };
            _cfgButtonOffset.SettingChanged += delegate { if (_instance != null && _instance._button != null) _instance._button.anchoredPosition = _cfgButtonOffset.Value; };
        }

        private void DestroyUi()
        {
            if (_button != null) Destroy(_button.gameObject);
            if (_panel != null) Destroy(_panel.gameObject);
            _button = null;
            _panel = null;
            _uiGui = null;
        }

        private void UpdateUi()
        {
            if (!InventoryGui.IsVisible() || Player.m_localPlayer == null) return;
            if (!EnsureUi()) return;
            if (_panel.gameObject.activeSelf != _panelOpen) _panel.gameObject.SetActive(_panelOpen);
            if (!_panelOpen) return;

            // Equip and unequip while the window is open: follow it.
            CollectSlots(_slotItems);
            if (!SameItems(_slotItems, _shownSlots)) RebuildAll();
        }

        private void TogglePanel()
        {
            _panelOpen = !_panelOpen;
            if (_panel == null) return;
            _panel.gameObject.SetActive(_panelOpen);
            if (!_panelOpen) return;
            PlacePanel();
            _panel.SetAsLastSibling();
            RebuildAll();
        }

        // ------------------------------------------------------------------
        // Contents
        // ------------------------------------------------------------------

        // The equipped items with a look slot: the six armor slots (null when empty), then the
        // items in or put away from the hands.
        private static void CollectSlots(List<ItemDrop.ItemData> into)
        {
            into.Clear();
            Player player = Player.m_localPlayer;
            if (player == null || player.GetInventory() == null) return;
            List<ItemDrop.ItemData> equipped = player.GetInventory().GetEquippedItems();
            foreach (ItemDrop.ItemData.ItemType type in ArmorSlots)
            {
                ItemDrop.ItemData found = null;
                foreach (ItemDrop.ItemData item in equipped)
                    if (item.m_shared.m_itemType == type) { found = item; break; }
                into.Add(found);
            }
            foreach (ItemDrop.ItemData item in equipped)
                if (IsHand(item.m_shared.m_itemType) && item.m_dropPrefab != null) into.Add(item);
        }

        private static bool SameItems(List<ItemDrop.ItemData> a, List<ItemDrop.ItemData> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!ReferenceEquals(a[i], b[i])) return false;
            return true;
        }

        private void RebuildAll()
        {
            CollectSlots(_slotItems);
            if (_selected == null || !_slotItems.Contains(_selected) || !_selected.m_equipped)
            {
                _selected = null;
                foreach (ItemDrop.ItemData item in _slotItems)
                    if (item != null) { _selected = item; break; }
            }
            RebuildSlots();
            RebuildLooks();
        }

        private void RebuildSlots()
        {
            _shownSlots.Clear();
            _shownSlots.AddRange(_slotItems);
            if (_slotRow == null) return;
            Clear(_slotRow);
            for (int i = 0; i < _slotItems.Count; i++)
            {
                ItemDrop.ItemData item = _slotItems[i];
                InventoryElement cell = MakeCell(_slotRow, new Vector2(i * _cell, 0f));
                if (cell == null) continue;
                if (item == null)
                {
                    SetIcon(cell, null, false);
                    cell.m_button.interactable = false;
                    continue;
                }
                string look = GetLook(item);
                Sprite icon = item.GetIcon();
                bool dim = false;
                if (look == HiddenValue) dim = true;
                else if (look != null)
                {
                    Sprite lookIcon = LookIcon(look);
                    if (lookIcon != null) icon = lookIcon;
                }
                SetIcon(cell, icon, dim);
                SetShown(cell.m_selected, ReferenceEquals(item, _selected));
                string tip = look != null ? "Looks like: " + LookName(look) : "Own look";
                SetTooltip(cell, ItemName(item), tip);
                ItemDrop.ItemData captured = item;
                cell.m_button.onClick.AddListener(delegate
                {
                    _selected = captured;
                    RebuildSlots();
                    RebuildLooks();
                });
            }
        }

        private void RebuildLooks()
        {
            _shownLooks.Clear();
            if (_gridContent == null) return;
            Clear(_gridContent);
            ItemDrop.ItemData item = _selected;
            if (item == null)
            {
                _infoText.text = "Equip an armor piece or a weapon to give it a look.";
                SetGridHeight(0);
                return;
            }

            string current = GetLook(item);
            _infoText.text = "<b>" + ItemName(item) + "</b>   " + (current != null ? "looks like " + LookName(current) : "own look");
            _hintText.text = _cfgLooks.Value == LookSource.All
                ? "All items of this kind. Stats stay the item's own."
                : "Looks of items you have found. Stats stay the item's own.";

            List<LookEntry> entries = Looks(item);
            for (int i = 0; i < entries.Count; i++)
            {
                LookEntry entry = entries[i];
                InventoryElement cell = MakeCell(_gridContent, new Vector2((i % GridColumns) * _cell, -(i / GridColumns) * _cell));
                if (cell == null) continue;
                SetIcon(cell, entry.Icon, entry.Dim);
                SetShown(cell.m_selected, entry.Value == current);
                SetTooltip(cell, entry.Name, entry.Value == null ? "The item's own look." : (entry.Value == HiddenValue ? "Not shown on the character." : "Click to wear this look."));
                _shownLooks.Add(entry.Value);
                LookEntry captured = entry;
                ItemDrop.ItemData target = item;
                cell.m_button.onClick.AddListener(delegate
                {
                    SetLook(target, captured.Value);
                    RebuildSlots();
                    RebuildLooks();
                });
            }
            SetGridHeight((entries.Count + GridColumns - 1) / GridColumns);
        }

        // Own look, hidden (where allowed), then every look of the same kind, by name.
        private static List<LookEntry> Looks(ItemDrop.ItemData item)
        {
            List<LookEntry> list = new List<LookEntry>();
            LookEntry own = new LookEntry();
            own.Value = null;
            own.Name = "Own look";
            own.Icon = item.GetIcon();
            list.Add(own);
            if (CanHide(item.m_shared.m_itemType))
            {
                LookEntry hidden = new LookEntry();
                hidden.Value = HiddenValue;
                hidden.Name = "Hidden";
                hidden.Icon = item.GetIcon();
                hidden.Dim = true;
                list.Add(hidden);
            }

            ObjectDB db = ObjectDB.instance;
            Player player = Player.m_localPlayer;
            if (db == null || db.m_items == null) return list;
            HashSet<string> carried = new HashSet<string>();
            if (player != null && player.GetInventory() != null)
                foreach (ItemDrop.ItemData it in player.GetInventory().GetAllItems())
                    if (it.m_dropPrefab != null) carried.Add(it.m_dropPrefab.name);

            List<LookEntry> looks = new List<LookEntry>();
            HashSet<string> seen = new HashSet<string>();
            string ownPrefab = item.m_dropPrefab != null ? item.m_dropPrefab.name : "";
            foreach (GameObject go in db.m_items)
            {
                if (go == null || !seen.Add(go.name)) continue;
                ItemDrop drop = go.GetComponent<ItemDrop>();
                if (drop == null || drop.m_itemData == null) continue;
                ItemDrop.ItemData.SharedData s = drop.m_itemData.m_shared;
                if (s == null || s.m_icons == null || s.m_icons.Length == 0) continue;
                if (WhyNot(item, s) != null) continue;
                string name = Localize(s.m_name);
                if (string.IsNullOrEmpty(name) || name.StartsWith("[")) continue;   // no translation: a test or unused item
                if (_cfgLooks.Value == LookSource.Found && !carried.Contains(go.name) && (player == null || !player.IsKnownMaterial(s.m_name))) continue;

                int variants = s.m_variants > 1 ? Math.Min(s.m_variants, s.m_icons.Length) : 0;
                if (variants == 0)
                {
                    if (go.name == ownPrefab) continue;
                    looks.Add(Entry(LookValue(go.name, -1), name, s.m_icons[0]));
                    continue;
                }
                for (int v = 0; v < variants; v++)
                {
                    if (go.name == ownPrefab && v == item.m_variant) continue;
                    looks.Add(Entry(LookValue(go.name, v), name + " (style " + (v + 1) + ")", s.m_icons[v]));
                }
            }
            looks.Sort(delegate(LookEntry x, LookEntry y) { return string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase); });
            list.AddRange(looks);
            return list;
        }

        private static LookEntry Entry(string value, string name, Sprite icon)
        {
            LookEntry e = new LookEntry();
            e.Value = value;
            e.Name = name;
            e.Icon = icon;
            return e;
        }

        private static Sprite LookIcon(string value)
        {
            string prefab;
            int variant;
            ParseLook(value, out prefab, out variant);
            GameObject go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            if (drop == null || drop.m_itemData.m_shared.m_icons == null || drop.m_itemData.m_shared.m_icons.Length == 0) return null;
            Sprite[] icons = drop.m_itemData.m_shared.m_icons;
            return icons[variant >= 0 && variant < icons.Length ? variant : 0];
        }

        // ------------------------------------------------------------------
        // Building
        // ------------------------------------------------------------------
        private bool EnsureUi()
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || gui.m_player == null || gui.m_inventoryRoot == null || gui.m_playerGrid == null) return false;
            if (gui == _uiGui && _button != null && _panel != null) return true;
            DestroyUi();
            _uiGui = gui;

            GameObject element = gui.m_playerGrid.m_elementPrefab;
            RectTransform elementRect = element != null ? element.transform as RectTransform : null;
            float size = elementRect != null && elementRect.rect.width > 1f ? elementRect.rect.width : 64f;
            _cell = size + 6f;

            // The button on the inventory panel
            _button = MakeButton(gui, gui.m_player, "Glamour", TogglePanel);
            _button.anchorMin = _button.anchorMax = new Vector2(1f, 1f);
            _button.pivot = new Vector2(0f, 1f);
            _button.anchoredPosition = _cfgButtonOffset.Value;

            // The window
            float width = Pad * 2f + GridColumns * _cell;
            float titleH = 34f, infoH = 26f, hintH = 24f;
            float height = Pad + titleH + _cell + 8f + infoH + GridRows * _cell + hintH + Pad;
            GameObject panelGo = new GameObject("Glamour", typeof(RectTransform), typeof(Image));
            panelGo.layer = gui.m_inventoryRoot.gameObject.layer;
            _panel = (RectTransform)panelGo.transform;
            _panel.SetParent(gui.m_inventoryRoot, false);
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0f, 1f);
            _panel.sizeDelta = new Vector2(width, height);
            CopyBackground(panelGo.GetComponent<Image>(), gui.m_player);
            panelGo.AddComponent<DragHandle>().Owner = this;

            TMP_Text sample = gui.m_takeAllButton != null ? gui.m_takeAllButton.GetComponentInChildren<TMP_Text>(true) : null;
            TextMeshProUGUI title = MakeText(_panel, "Title", sample, 22f, new Vector2(Pad, -Pad), new Vector2(width - 2f * Pad, titleH));
            title.text = "Glamour";
            title.fontStyle = FontStyles.Bold;
            title.alignment = TextAlignmentOptions.Left;

            RectTransform close = MakeButton(gui, _panel, "X", TogglePanel);
            close.anchorMin = close.anchorMax = new Vector2(1f, 1f);
            close.pivot = new Vector2(1f, 1f);
            close.sizeDelta = new Vector2(36f, 30f);
            close.anchoredPosition = new Vector2(-Pad + 4f, -Pad + 4f);

            float y = -(Pad + titleH);
            _slotRow = MakeRect(_panel, "Slots", new Vector2(Pad, y), new Vector2(width - 2f * Pad, _cell));
            y -= _cell + 8f;
            _infoText = MakeText(_panel, "Info", sample, 17f, new Vector2(Pad, y), new Vector2(width - 2f * Pad, infoH));
            _infoText.alignment = TextAlignmentOptions.Left;
            y -= infoH;

            // The looks: a scrolling grid
            RectTransform viewport = MakeRect(_panel, "Looks", new Vector2(Pad, y), new Vector2(width - 2f * Pad, GridRows * _cell));
            viewport.gameObject.AddComponent<RectMask2D>();
            Image catcher = viewport.gameObject.AddComponent<Image>();   // the scroll wheel needs something to hit between the cells
            catcher.color = new Color(0f, 0f, 0f, 0.001f);
            _gridContent = MakeRect(viewport, "Content", Vector2.zero, new Vector2(width - 2f * Pad, 0f));
            _scroll = viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.content = _gridContent;
            _scroll.viewport = viewport;
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = _cell * 0.5f;
            y -= GridRows * _cell;

            _hintText = MakeText(_panel, "Hint", sample, 14f, new Vector2(Pad, y - 2f), new Vector2(width - 2f * Pad, hintH));
            _hintText.alignment = TextAlignmentOptions.Left;
            _hintText.color = new Color(0.8f, 0.8f, 0.76f);

            panelGo.SetActive(false);
            if (_panelOpen)
            {
                panelGo.SetActive(true);
                PlacePanel();
                RebuildAll();
            }
            return true;
        }

        // Beside the inventory panel's top right corner, plus where it was dragged to.
        private void PlacePanel()
        {
            if (_panel == null || _uiGui == null) return;
            RectTransform root = _panel.parent as RectTransform;
            if (root == null) return;
            Vector3[] corners = new Vector3[4];
            _uiGui.m_player.GetWorldCorners(corners);
            Vector2 local = root.InverseTransformPoint(corners[2]);
            float buttonW = _button != null ? _button.rect.width : 0f;
            _panel.anchoredPosition = local - root.rect.center + new Vector2(_cfgButtonOffset.Value.x + buttonW + 8f, 0f) + _cfgPanelOffset.Value;
        }

        private void SetGridHeight(int rows)
        {
            if (_gridContent == null) return;
            _gridContent.sizeDelta = new Vector2(_gridContent.sizeDelta.x, rows * _cell);
            _gridContent.anchoredPosition = Vector2.zero;
        }

        private InventoryElement MakeCell(RectTransform parent, Vector2 pos)
        {
            GameObject prefab = _uiGui != null ? _uiGui.m_playerGrid.m_elementPrefab : null;
            if (prefab == null) return null;
            GameObject go = Instantiate(prefab, parent, false);
            go.name = "Cell";
            go.SetActive(true);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos + new Vector2(3f, -3f);
            InventoryElement cell = go.GetComponent<InventoryElement>();
            if (cell == null || cell.m_button == null || cell.m_icon == null)
            {
                Destroy(go);
                return null;
            }
            // Only the frame, the icon and the selection stay.
            GameObject keep = cell.m_icon.gameObject;
            HideExtra(cell.m_amount, go, keep);
            HideExtra(cell.m_quality, go, keep);
            HideExtra(cell.m_durability, go, keep);
            HideExtra(cell.m_queued, go, keep);
            HideExtra(cell.m_noteleport, go, keep);
            HideExtra(cell.m_food, go, keep);
            HideExtra(cell.m_equiped, go, keep);
            HideExtra(cell.m_dropFocus, go, keep);
            SetShown(cell.m_selected, false);
            Transform binding = go.transform.Find("binding");
            if (binding != null) binding.gameObject.SetActive(false);
            cell.m_button.onClick = new Button.ButtonClickedEvent();
            cell.m_button.interactable = true;
            return cell;
        }

        private static void SetIcon(InventoryElement cell, Sprite icon, bool dim)
        {
            cell.m_icon.enabled = icon != null;
            cell.m_icon.sprite = icon;
            cell.m_icon.color = dim ? new Color(1f, 1f, 1f, 0.3f) : Color.white;
        }

        private static void SetTooltip(InventoryElement cell, string topic, string text)
        {
            if (cell.m_tooltip == null) return;
            cell.m_tooltip.m_topic = topic;
            cell.m_tooltip.m_text = text;
        }

        private static RectTransform MakeButton(InventoryGui gui, Transform parent, string label, Action onClick)
        {
            Button template = gui.m_takeAllButton;
            GameObject go;
            if (template != null)
            {
                go = Instantiate(template.gameObject, parent, false);
                // The copy keeps the container's gamepad hint: not ours.
                foreach (UIGamePad pad in go.GetComponentsInChildren<UIGamePad>(true)) Destroy(pad);
            }
            else
            {
                go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(parent, false);
                go.GetComponent<Image>().color = new Color(0.3f, 0.25f, 0.2f, 0.9f);
                ((RectTransform)go.transform).sizeDelta = new Vector2(110f, 32f);
            }
            go.name = "Glamour" + (label == "X" ? "Close" : "Button");
            go.SetActive(true);
            Button button = go.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();   // the template's own action (take all) goes
            button.onClick.AddListener(delegate { onClick(); });
            button.interactable = true;
            TMP_Text text = go.GetComponentInChildren<TMP_Text>(true);
            if (text != null) text.text = label;
            return (RectTransform)go.transform;
        }

        private static RectTransform MakeRect(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        private static TextMeshProUGUI MakeText(Transform parent, string name, TMP_Text sample, float fontSize, Vector2 pos, Vector2 size)
        {
            RectTransform rt = MakeRect(parent, name, pos, size);
            TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (sample != null)
            {
                t.font = sample.font;
                t.fontSharedMaterial = sample.fontSharedMaterial;
            }
            t.fontSize = fontSize;
            t.color = new Color(1f, 0.93f, 0.8f);
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }

        // The inventory panel's background: its largest image with a sprite.
        private static void CopyBackground(Image target, RectTransform from)
        {
            Image best = null;
            float bestArea = 0f;
            foreach (Image img in from.GetComponentsInChildren<Image>(true))
            {
                if (img.sprite == null) continue;
                float area = img.rectTransform.rect.width * img.rectTransform.rect.height;
                if (area > bestArea) { bestArea = area; best = img; }
            }
            if (best == null)
            {
                target.color = new Color(0.12f, 0.11f, 0.1f, 0.94f);
                return;
            }
            target.sprite = best.sprite;
            target.type = best.type;
            target.material = best.material;
            target.pixelsPerUnitMultiplier = best.pixelsPerUnitMultiplier;
            Color c = best.color;
            c.a = Mathf.Max(c.a, 0.92f);
            target.color = c;
        }

        private static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--) Destroy(parent.GetChild(i).gameObject);
        }

        private static void HideExtra(Component c, GameObject root, GameObject keep)
        {
            if (c == null) return;
            if (c.gameObject != root && c.gameObject != keep) c.gameObject.SetActive(false);
            else
            {
                Behaviour b = c as Behaviour;
                if (b != null) b.enabled = false;
            }
        }

        private static void SetShown(object o, bool on)
        {
            if (o == null) return;
            GameObject go = o as GameObject;
            if (go != null) { go.SetActive(on); return; }
            Behaviour b = o as Behaviour;
            if (b != null) { b.enabled = on; return; }
            Component c = o as Component;
            if (c != null) c.gameObject.SetActive(on);
        }

        private void SavePanelOffset(Vector2 delta)
        {
            _cfgPanelOffset.Value = _cfgPanelOffset.Value + delta;
        }

        // Drag the window by any free spot; where it ends up is remembered.
        private sealed class DragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public GlamourPlugin Owner;
            private Vector2 _start;

            public void OnBeginDrag(PointerEventData e)
            {
                _start = ((RectTransform)transform).anchoredPosition;
            }

            public void OnDrag(PointerEventData e)
            {
                Canvas canvas = GetComponentInParent<Canvas>();
                float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
                ((RectTransform)transform).anchoredPosition += e.delta / scale;
            }

            public void OnEndDrag(PointerEventData e)
            {
                if (Owner != null) Owner.SavePanelOffset(((RectTransform)transform).anchoredPosition - _start);
            }
        }
    }
}
