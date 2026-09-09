using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Deterministic UI Toolkit pointer events (validation/probe instrumentation).
///
/// Dispatches the SAME target-less PointerMove/Down/Up event stream the
/// WorldUiPointerRouter forwards for physical consoles: UI Toolkit's own
/// dispatcher runs its PreDispatch element-under-pointer pass and PostDispatch
/// CommitElementUnderPointers pass, so PointerEnter/Leave, :hover pseudo
/// states and Button Clickable capture all behave like real input. This makes
/// screen-space shipboard overlays (deployment confirmation / shared loadout)
/// clickable in automated runs through the genuine UI Toolkit event path.
///
/// Inert by itself: only called explicitly by probes/tests.
/// </summary>
public static class UiToolkitPointerSimulator
{
    private const int MousePointerId = 0;
    private const int ButtonLeft = 0;
    private const string PointerTypeMouse = "mouse";

    private static readonly Dictionary<string, PropertyInfo> PropertyCache =
        new Dictionary<string, PropertyInfo>();

    /// <summary>Element centre in panel layout pixels (worldBound centre with
    /// a resolved-style fallback for not-yet-laid-out elements).</summary>
    public static Vector2 CentreOf(VisualElement element)
    {
        Rect bounds = element.worldBound;
        if (bounds.width < 1f || bounds.height < 1f)
        {
            bounds = new Rect(element.resolvedStyle.left, element.resolvedStyle.top,
                element.resolvedStyle.width, element.resolvedStyle.height);
        }

        return bounds.center;
    }

    /// <summary>Full real click on the element (move + down + up at its
    /// centre). Requires the element to belong to a live panel.</summary>
    public static bool Click(VisualElement element)
    {
        VisualElement root = RootOf(element);
        if (root == null)
        {
            return false;
        }

        Vector2 position = CentreOf(element);
        DispatchPointer<PointerMoveEvent>(root, position, -1, 0, 0);
        DispatchPointer<PointerDownEvent>(root, position, ButtonLeft,
            1 << ButtonLeft, 1);
        DispatchPointer<PointerUpEvent>(root, position, ButtonLeft,
            1 << ButtonLeft, 1);
        return true;
    }

    /// <summary>Real hover (move only) at the element centre.</summary>
    public static bool Hover(VisualElement element)
    {
        VisualElement root = RootOf(element);
        if (root == null)
        {
            return false;
        }

        DispatchPointer<PointerMoveEvent>(root, CentreOf(element), -1, 0, 0);
        return true;
    }

    private static VisualElement RootOf(VisualElement element)
    {
        IPanel panel = element.panel;
        return panel != null && panel.visualTree != null ? panel.visualTree : null;
    }

    // ---- Event synthesis (same target-less stream as WorldUiPointerRouter) ----

    private static void DispatchPointer<T>(VisualElement root,
        Vector2 position, int button, int pressedButtons, int clickCount)
        where T : PointerEventBase<T>, new()
    {
        using (T evt = EventBase<T>.GetPooled())
        {
            Set(evt, "pointerId", MousePointerId);
            Set(evt, "pointerType", PointerTypeMouse);
            Set(evt, "isPrimary", true);
            Set(evt, "position", new Vector3(position.x, position.y, 0f));
            Set(evt, "deltaPosition", Vector3.zero);
            Set(evt, "button", button);
            Set(evt, "pressedButtons", pressedButtons);
            Set(evt, "clickCount", clickCount);
            Set(evt, "modifiers", EventModifiers.None);
            root.SendEvent(evt);
        }
    }

    private static void Set(object evt, string property, object value)
    {
        // PropertyInfo is per constructed event type, so the cache key must
        // include the concrete event type.
        string key = evt.GetType().Name + "." + property;
        PropertyInfo propertyInfo;
        if (!PropertyCache.TryGetValue(key, out propertyInfo))
        {
            propertyInfo = evt.GetType().GetProperty(property,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (propertyInfo == null)
            {
                throw new MissingMemberException(evt.GetType().FullName, property);
            }

            PropertyCache[key] = propertyInfo;
        }

        propertyInfo.SetValue(evt, value, null);
    }
}
