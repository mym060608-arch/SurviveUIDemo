using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Shared controller focus for ordinary UI pages (Phase 09B PASS 1).
///
/// This is the ONE focus implementation the later PASSes build on (wrist,
/// overview / preparation, processing, outpost, pause). It exists because the
/// title screen and the pause menu each grew their own private list walk, and a
/// console UI needs more than one list: a page can carry a grid, several rows of
/// buttons and tab groups.
///
/// Contracts it implements (Phase09B §3):
///
///   * Left stick / D-Pad (and the arrow keys) move the focus.
///   * Confirm activates the focused element; Back leaves the page.
///   * LB / RB switch tab group when the page has one.
///   * Opening a page gives a sensible default focus (<see cref="FocusDefault"/>).
///   * disabled / hidden elements can NEVER take focus.
///   * the focus can never reach a dead end (it wraps inside the active group).
///   * the mouse and the pad can take over at any time - the navigator only owns
///     the KEYBOARD/PAD focus ring, a real pointer click still works because the
///     elements stay ordinary clickable controls.
///
/// Focus authority: like the title screen, this navigator NEVER calls
/// VisualElement.Focus(). It owns the focus ring through a style class, so Unity's
/// built-in focus / submit path stays out of the way and there is exactly one
/// focus truth (`Focused`).
/// </summary>
public sealed class UiFocusNavigator
{
    /// <summary>Hold time before a held direction starts repeating.</summary>
    public const float RepeatDelay = 0.32f;

    /// <summary>Repeat rate while a direction stays held.</summary>
    public const float RepeatRate = 0.14f;

    /// <summary>Default style class applied to the focused element.</summary>
    public const string DefaultFocusClass = "ui-focus";

    private readonly List<VisualElement> _items = new List<VisualElement>();
    private readonly Dictionary<VisualElement, Action> _actions =
        new Dictionary<VisualElement, Action>();
    private readonly List<string> _tabOrder = new List<string>();
    private readonly Dictionary<string, List<VisualElement>> _tabs =
        new Dictionary<string, List<VisualElement>>();

    private string _activeTab;
    private VisualElement _focused;
    private int _lastDirectionX;
    private int _lastDirectionY;
    private float _nextRepeatTime;

    /// <summary>Creates a navigator for one page. Disabled until the page opens.</summary>
    public UiFocusNavigator(string focusClass = DefaultFocusClass)
    {
        FocusClass = string.IsNullOrEmpty(focusClass) ? DefaultFocusClass : focusClass;
    }

    /// <summary>Style class that marks the focused element.</summary>
    public string FocusClass { get; }

    /// <summary>False while the page is closed: Tick() then does nothing.</summary>
    public bool Enabled { get; set; }

    /// <summary>The focus wraps inside the active group (never a dead end).</summary>
    public bool Wrap { get; set; } = true;

    /// <summary>
    /// Optional layer this page owns. When set, <see cref="ConsumeBack"/> only
    /// reports a Back press while this page really owns it, and consumes it - so a
    /// page can never react to a Back that belongs to the pause above it.
    /// </summary>
    public UiInput.UiLayer? OwnerLayer { get; set; }

    /// <summary>Raised whenever the focused element changes (may be null).</summary>
    public event Action<VisualElement> FocusedChanged;

    /// <summary>The focused element, or null while nothing can take focus.</summary>
    public VisualElement Focused => _focused;

    /// <summary>Number of registered items in the active group.</summary>
    public int Count => ActiveGroup.Count;

    /// <summary>Index of the focused element inside the active group (-1 = none).</summary>
    public int FocusedIndex => _focused == null ? -1 : ActiveGroup.IndexOf(_focused);

    /// <summary>Name of the active tab group ("" when the page has no tabs).</summary>
    public string ActiveTab => _activeTab ?? string.Empty;

    /// <summary>Registers the page's focusable elements in reading order.</summary>
    public void SetItems(IEnumerable<VisualElement> items)
    {
        _items.Clear();
        if (items != null)
        {
            foreach (VisualElement item in items)
            {
                if (item != null)
                {
                    _items.Add(item);
                }
            }
        }

        if (_focused != null && !_items.Contains(_focused))
        {
            // A dropped item must not keep the focus ring painted on screen: the
            // element leaves the operable set, so it leaves the focus look too.
            _focused.RemoveFromClassList(FocusClass);
            _focused = null;
        }
    }

    /// <summary>Registers one element with the action Confirm runs on it.</summary>
    public void Register(VisualElement element, Action onConfirm = null)
    {
        if (element == null)
        {
            return;
        }

        if (!_items.Contains(element))
        {
            _items.Add(element);
        }

        if (onConfirm != null)
        {
            _actions[element] = onConfirm;
        }
    }

    /// <summary>Removes every registered element (the page is being rebuilt).</summary>
    public void Clear()
    {
        _items.Clear();
        _actions.Clear();
        _tabs.Clear();
        _tabOrder.Clear();
        _activeTab = null;
        if (_focused != null)
        {
            _focused.RemoveFromClassList(FocusClass);
            _focused = null;
        }
    }

    /// <summary>
    /// Registers a tab group. The first registered group becomes active; switching
    /// groups moves the focus into the new group, so LB/RB can never leave the
    /// focus on an element of a hidden tab.
    /// </summary>
    public void AddTab(string id, IEnumerable<VisualElement> items)
    {
        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        var group = new List<VisualElement>();
        if (items != null)
        {
            foreach (VisualElement item in items)
            {
                if (item != null && !group.Contains(item))
                {
                    group.Add(item);
                }
            }
        }

        if (!_tabs.ContainsKey(id))
        {
            _tabOrder.Add(id);
        }

        _tabs[id] = group;
        if (_activeTab == null)
        {
            _activeTab = id;
        }
    }

    /// <summary>True when the page registered at least two tab groups.</summary>
    public bool HasTabs => _tabOrder.Count > 1;

    /// <summary>Switches to the next tab group and focuses its first element.</summary>
    public bool NextTab()
    {
        return SwitchTab(1);
    }

    /// <summary>Switches to the previous tab group and focuses its last element.</summary>
    public bool PreviousTab()
    {
        return SwitchTab(-1);
    }

    private bool SwitchTab(int direction)
    {
        if (!HasTabs)
        {
            return false;
        }

        int index = _tabOrder.IndexOf(_activeTab);
        if (index < 0)
        {
            index = 0;
        }

        index = ((index + direction) % _tabOrder.Count + _tabOrder.Count) % _tabOrder.Count;
        _activeTab = _tabOrder[index];

        // Enter the new group at the edge the player came from (RB enters at the
        // first row, LB at the last), which is what a tab strip feels like.
        List<VisualElement> group = ActiveGroup;
        for (int i = 0; i < group.Count; i++)
        {
            VisualElement candidate = direction > 0
                ? group[i]
                : group[group.Count - 1 - i];
            if (IsFocusable(candidate))
            {
                return SetFocus(candidate);
            }
        }

        return false;
    }

    /// <summary>The elements of the active tab group, or every item without tabs.</summary>
    public List<VisualElement> ActiveGroup
    {
        get
        {
            List<VisualElement> group;
            if (_activeTab != null && _tabs.TryGetValue(_activeTab, out group))
            {
                return group;
            }

            return _items;
        }
    }

    /// <summary>True when the element can take focus right now.</summary>
    public static bool IsFocusable(VisualElement element)
    {
        if (element == null || !element.enabledSelf || !element.visible)
        {
            return false;
        }

        if (element.resolvedStyle.display == DisplayStyle.None
            || element.resolvedStyle.visibility == Visibility.Hidden)
        {
            return false;
        }

        if (element.pickingMode == PickingMode.Ignore)
        {
            return false;
        }

        Rect bounds = element.worldBound;
        return bounds.width >= 1f && bounds.height >= 1f;
    }

    /// <summary>
    /// Gives the page its default focus: the first focusable element of the active
    /// group, or the previously focused one when it is still valid (a page that is
    /// reopened keeps the player's place).
    /// </summary>
    public bool FocusDefault()
    {
        if (IsFocusable(_focused) && ActiveGroup.Contains(_focused))
        {
            return SetFocus(_focused);
        }

        List<VisualElement> group = ActiveGroup;
        for (int i = 0; i < group.Count; i++)
        {
            if (IsFocusable(group[i]))
            {
                return SetFocus(group[i]);
            }
        }

        SetFocus(null);
        return false;
    }

    /// <summary>
    /// Moves the focus to a specific element (no-op when it cannot focus).
    ///
    /// The element must belong to the CURRENTLY operable group: "only elements that
    /// really act can take focus" is a property of the navigator, not a property of
    /// whoever calls it, so a page can never park the focus on a read-out by asking
    /// for it directly.
    /// </summary>
    public bool SetFocus(VisualElement element)
    {
        if (element != null && (!IsFocusable(element) || !ActiveGroup.Contains(element)))
        {
            return false;
        }

        if (ReferenceEquals(_focused, element))
        {
            return _focused != null;
        }

        if (_focused != null)
        {
            _focused.RemoveFromClassList(FocusClass);
        }

        _focused = element;
        if (_focused != null)
        {
            // Only the focus class is applied - the navigator never reorders the
            // page's children (BringToFront would rewrite the visual tree order).
            _focused.AddToClassList(FocusClass);
        }

        FocusedChanged?.Invoke(_focused);
        return _focused != null;
    }

    /// <summary>
    /// Moves the focus in a direction. The vector follows the semantic convention
    /// of <see cref="UiInput.NavigationVector"/>: +x = right, +y = UP.
    /// </summary>
    public bool Move(Vector2 direction)
    {
        if (Mathf.Approximately(direction.x, 0f) && Mathf.Approximately(direction.y, 0f))
        {
            return false;
        }

        bool horizontal = Mathf.Abs(direction.x) >= Mathf.Abs(direction.y);
        float sign = horizontal ? Mathf.Sign(direction.x) : Mathf.Sign(direction.y);

        List<VisualElement> group = ActiveGroup;
        if (group.Count == 0)
        {
            return false;
        }

        if (_focused == null)
        {
            return FocusDefault();
        }

        Vector2 origin = CentreOf(_focused);
        VisualElement best = null;
        float bestScore = float.MaxValue;
        VisualElement wrapCandidate = null;
        float wrapScore = float.MaxValue;
        float wrapBand = float.MaxValue;

        foreach (VisualElement candidate in group)
        {
            if (ReferenceEquals(candidate, _focused) || !IsFocusable(candidate))
            {
                continue;
            }

            Vector2 centre = CentreOf(candidate);
            float dx = centre.x - origin.x;
            // Panel space grows downwards, so "up" (+y) means a smaller y.
            float dy = -(centre.y - origin.y);
            float primary = horizontal ? dx * sign : dy * sign;
            float band = horizontal ? Mathf.Abs(dy) : Mathf.Abs(dx);

            if (primary > 1f)
            {
                float score = primary + band * 2f;
                if (score < bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }
            else if (primary < -1f)
            {
                // Farthest element in the OPPOSITE direction: this is the wrap
                // target, used only when nothing lies ahead.
                float score = -primary + band * 2f;
                if (score < wrapScore)
                {
                    wrapCandidate = candidate;
                    wrapScore = score;
                    wrapBand = band;
                }
            }
        }

        if (best != null)
        {
            return SetFocus(best);
        }

        if (!Wrap || wrapCandidate == null)
        {
            return false;
        }

        // Wrap only inside the same lane (a row / column band), so the focus never
        // jumps to a different column just because the list ended.
        float lane = horizontal
            ? Mathf.Max(_focused.worldBound.height, 24f) * 1.5f
            : Mathf.Max(_focused.worldBound.width, 24f) * 1.5f;
        if (wrapBand > lane)
        {
            return false;
        }

        return SetFocus(wrapCandidate);
    }

    /// <summary>
    /// Activates the focused element: the registered action when there is one,
    /// otherwise a real UI Toolkit click at the element's centre. Both paths run
    /// the page's own handler, so Confirm and a mouse click can never diverge.
    /// </summary>
    public bool Confirm()
    {
        VisualElement target = _focused;
        if (target == null || !IsFocusable(target))
        {
            return false;
        }

        Action action;
        if (_actions.TryGetValue(target, out action) && action != null)
        {
            action();
            return true;
        }

        return UiToolkitPointerSimulator.Click(target);
    }

    /// <summary>
    /// True when this page owns a Back press this frame. The press is CONSUMED
    /// here, so a page that uses the navigator cannot forget to consume it and
    /// leak the same press into the global Pause.
    /// </summary>
    public bool ConsumeBack()
    {
        if (!OwnerLayer.HasValue || !UiInput.BackPressedForLayer(OwnerLayer.Value))
        {
            return false;
        }

        UiInput.ConsumeBackForFrame();
        return true;
    }

    /// <summary>
    /// Per-frame input: tabs, directional movement with repeat and Confirm. The
    /// page calls this only while it owns the frame.
    /// </summary>
    public void Tick()
    {
        if (!Enabled)
        {
            return;
        }

        int tab = UiInput.TabDirection();
        if (tab > 0)
        {
            NextTab();
        }
        else if (tab < 0)
        {
            PreviousTab();
        }

        Vector2 navigation = UiInput.NavigationVector();
        UpdateRepeat(navigation);

        if (UiInput.ConfirmPressed())
        {
            Confirm();
        }
    }

    private void UpdateRepeat(Vector2 navigation)
    {
        int x = Mathf.RoundToInt(navigation.x);
        int y = Mathf.RoundToInt(navigation.y);
        if (x == 0 && y == 0)
        {
            _lastDirectionX = 0;
            _lastDirectionY = 0;
            return;
        }

        float now = Time.unscaledTime;
        if (x != _lastDirectionX || y != _lastDirectionY)
        {
            _lastDirectionX = x;
            _lastDirectionY = y;
            _nextRepeatTime = now + RepeatDelay;
            Move(new Vector2(navigation.x, navigation.y));
            return;
        }

        if (now >= _nextRepeatTime)
        {
            _nextRepeatTime = now + RepeatRate;
            Move(new Vector2(navigation.x, navigation.y));
        }
    }

    /// <summary>Panel-space centre of an element (layout or resolved fallback).</summary>
    private static Vector2 CentreOf(VisualElement element)
    {
        Rect bounds = element.worldBound;
        if (bounds.width < 1f || bounds.height < 1f)
        {
            bounds = new Rect(element.resolvedStyle.left, element.resolvedStyle.top,
                element.resolvedStyle.width, element.resolvedStyle.height);
        }

        return bounds.center;
    }
}
