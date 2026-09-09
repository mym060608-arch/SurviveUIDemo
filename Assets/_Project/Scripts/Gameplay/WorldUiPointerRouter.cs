using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Shared world-space console pointer router (Phase 07B).
///
/// One implementation forwards the REAL mouse into whichever physical console
/// is focused, through the genuine UI Toolkit dispatcher - no fake direct
/// controller calls:
///
///   Mouse.current.position
///     -> focused eye camera ScreenPointToRay
///     -> focused console screen collider
///     -> UV
///     -> panel local pixel (single Y inversion)
///     -> target-less PointerMove/Down/Up events dispatched to the panel
///
/// Dispatching target-less events lets UI Toolkit's own dispatcher run its
/// PreDispatch element-under-pointer pass and PostDispatch
/// CommitElementUnderPointers pass, so PointerLeave/Enter (+ PointerOut/Over)
/// fire and USS :hover pseudo states update exactly like real input; Button
/// Clickable handles pointer capture and click on Up. Releasing outside the
/// screen delivers the Up at the engine's off-screen marker so capture is
/// released without clicking.
///
/// The cursor ring follows the same dot / dot+ring language as the wrist via
/// the shared WristCursorService (hover = over a Button).
/// </summary>
public class WorldUiPointerRouter : MonoBehaviour
{
    private const int MousePointerId = 0;
    private const int ButtonLeft = 0;
    private const string PointerTypeMouse = "mouse";

    private ShipGameController _ship;
    private Camera _camera;
    private WristCursorService _cursor;

    private IPanel _panel;
    private WorldConsoleSurface _surface;
    private Vector2 _lastPanelPos;
    private bool _inside;
    private int _buttons;
    private int _clickCount;
    private bool _overInteractive;

    private static readonly Dictionary<string, PropertyInfo> PropertyCache =
        new Dictionary<string, PropertyInfo>();

    /// <summary>True while the router is forwarding to the focused console.</summary>
    public bool IsForwarding { get; private set; }

    private void Awake()
    {
        _cursor = new WristCursorService();
    }

    private void OnDestroy()
    {
        if (_cursor != null)
        {
            _cursor.Dispose();
            _cursor = null;
        }
    }

    private void OnEnable()
    {
        _ship = UnityEngine.Object.FindAnyObjectByType<ShipGameController>();
    }

    private void Update()
    {
        ForwardHardwareMouse();
    }

    /// <summary>Deterministic single-frame pointer pipeline used by tests:
    /// viewport fraction (0..1) -> screen ray -> screen collider -> UV ->
    /// panel pixels -> real UI Toolkit click (down + up). Returns false when
    /// the point does not land on the focused console's screen.</summary>
    public bool TryClickViewportPoint(Vector2 viewport01)
    {
        if (_ship == null || !_ship.IsConsoleFocusActive)
        {
            return false;
        }

        ShipConsoleFocusController focus = _ship.Focus;
        if (focus == null || focus.State != ShipConsoleFocusController.ShipConsoleFocusState.ShipConsoleFocus)
        {
            return false;
        }

        ResolveTarget(focus);
        if (_surface == null || _panel == null || _camera == null)
        {
            return false;
        }

        Vector2 uv;
        bool hit = RayToSurfaceUv(_camera.ViewportPointToRay(viewport01), out uv);
        if (!hit || uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f)
        {
            return false;
        }

        Vector2 panelPos = UvToPanelLocal(uv);
        DispatchPointer<PointerMoveEvent>(panelPos, Vector2.zero, -1, _buttons, 0);
        DispatchPointer<PointerDownEvent>(panelPos, Vector2.zero, ButtonLeft,
            _buttons | (1 << ButtonLeft), 1);
        DispatchPointer<PointerUpEvent>(panelPos, Vector2.zero, ButtonLeft,
            _buttons | (1 << ButtonLeft), 1);
        return true;
    }

    /// <summary>Deterministic hover: moves the pointer to a viewport point so
    /// UI Toolkit applies genuine :hover styling (no click). Returns false
    /// when the point does not land on the focused console's screen.</summary>
    public bool TryHoverViewportPoint(Vector2 viewport01)
    {
        if (_ship == null || !_ship.IsConsoleFocusActive)
        {
            return false;
        }

        ShipConsoleFocusController focus = _ship.Focus;
        if (focus == null || focus.State != ShipConsoleFocusController.ShipConsoleFocusState.ShipConsoleFocus)
        {
            return false;
        }

        ResolveTarget(focus);
        if (_surface == null || _panel == null || _camera == null)
        {
            return false;
        }

        Vector2 uv;
        if (!RayToSurfaceUv(_camera.ViewportPointToRay(viewport01), out uv)
            || uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f)
        {
            return false;
        }

        Vector2 panelPos = UvToPanelLocal(uv);
        DispatchPointer<PointerMoveEvent>(panelPos, Vector2.zero, -1, _buttons, 0);
        _lastPanelPos = panelPos;
        _inside = true;
        return true;
    }

    /// <summary>Panel local pixel of the focused screen under the given mouse
    /// screen position (used by orientation probes).</summary>
    public bool TryScreenToPanelPixel(Vector2 screenPixel, out Vector2 panelPixel)
    {
        panelPixel = Vector2.zero;
        if (_camera == null || _surface == null)
        {
            return false;
        }

        Vector2 uv;
        if (!RayToSurfaceUv(_camera.ScreenPointToRay(screenPixel), out uv))
        {
            return false;
        }

        panelPixel = UvToPanelLocal(uv);
        return true;
    }

    // ---- Per-frame hardware forwarding (ShipConsoleFocus only) ----

    private void ForwardHardwareMouse()
    {
        if (_ship == null)
        {
            _ship = UnityEngine.Object.FindAnyObjectByType<ShipGameController>();
        }

        if (_ship == null || !_ship.IsConsoleFocusActive)
        {
            ClearHoverState();
            return;
        }

        ShipConsoleFocusController focus = _ship.Focus;
        if (focus == null || focus.State != ShipConsoleFocusController.ShipConsoleFocusState.ShipConsoleFocus)
        {
            // Transitions in/out keep the cursor hidden/locked; nothing to route.
            ClearHoverState();
            return;
        }

        ResolveTarget(focus);
        if (_surface == null || _panel == null || _camera == null)
        {
            ClearHoverState();
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        Vector2 screen = mouse.position.ReadValue();
        bool leftDown = mouse.leftButton.isPressed;
        bool pressedThisFrame = leftDown && (_buttons & (1 << ButtonLeft)) == 0;
        bool releasedThisFrame = !leftDown && (_buttons & (1 << ButtonLeft)) != 0;
        if (pressedThisFrame)
        {
            _buttons |= 1 << ButtonLeft;
            _clickCount = _clickCount >= 1 ? _clickCount + 1 : 1;
        }
        else if (releasedThisFrame)
        {
            _buttons &= ~(1 << ButtonLeft);
        }

        Vector2 uv;
        bool hit = RayToSurfaceUv(_camera.ScreenPointToRay(screen), out uv);
        bool inside = hit && uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
        Vector2 panelPos = inside ? UvToPanelLocal(uv) : Vector2.zero;
        Vector2 delta = inside && _inside ? panelPos - _lastPanelPos : Vector2.zero;

        if (inside)
        {
            if (!_inside || delta.sqrMagnitude > 0.0001f)
            {
                DispatchPointer<PointerMoveEvent>(panelPos, delta, -1, _buttons, 0);
            }

            if (pressedThisFrame)
            {
                DispatchPointer<PointerDownEvent>(panelPos, Vector2.zero,
                    ButtonLeft, _buttons, _clickCount);
            }

            if (releasedThisFrame)
            {
                DispatchPointer<PointerUpEvent>(panelPos, Vector2.zero,
                    ButtonLeft, _buttons, _clickCount);
            }
        }
        else
        {
            if (_inside)
            {
                // Off-screen marker clears hover/over/out bookkeeping.
                DispatchPointer<PointerMoveEvent>(OffscreenPosition(), Vector2.zero,
                    -1, _buttons, 0);
            }

            if (releasedThisFrame)
            {
                DispatchPointer<PointerUpEvent>(OffscreenPosition(), Vector2.zero,
                    ButtonLeft, _buttons, _clickCount);
            }

            if (pressedThisFrame)
            {
                DispatchPointer<PointerDownEvent>(OffscreenPosition(), Vector2.zero,
                    ButtonLeft, _buttons, _clickCount);
            }
        }

        _lastPanelPos = panelPos;
        _inside = inside;
        IsForwarding = true;

        // Cursor ring feedback (dot + teal ring over interactive elements).
        bool overInteractive = inside && IsOverInteractiveElement(panelPos);
        if (overInteractive != _overInteractive)
        {
            _overInteractive = overInteractive;
            if (_cursor != null)
            {
                _cursor.SetHover(overInteractive);
            }
        }
    }

    private void ResolveTarget(ShipConsoleFocusController focus)
    {
        WorldConsoleSurface active = focus.ActiveSurface;
        if (active == null)
        {
            ClearHoverState();
            return;
        }

        if (active != _surface)
        {
            ClearHoverState();
            _surface = active;
            _panel = null;
            _inside = false;
            _buttons = 0;
            _clickCount = 0;
        }

        if (_panel == null && active.Document != null
            && active.Document.rootVisualElement != null)
        {
            _panel = active.Document.rootVisualElement.panel;
        }

        if (_camera == null)
        {
            _camera = _ship != null && _ship.Player != null
                ? _ship.Player.EyeCamera
                : Camera.main;
        }
    }

    private void ClearHoverState()
    {
        if (_panel != null && _inside)
        {
            DispatchPointer<PointerMoveEvent>(OffscreenPosition(), Vector2.zero,
                -1, _buttons, 0);
        }

        _inside = false;
        IsForwarding = false;
        if (_overInteractive)
        {
            _overInteractive = false;
            if (_cursor != null)
            {
                _cursor.SetHover(false);
            }
        }
    }

    // ---- Geometry / coordinate helpers ----

    private bool RayToSurfaceUv(Ray ray, out Vector2 uv)
    {
        uv = Vector2.zero;
        if (_surface == null || _surface.ScreenCollider == null)
        {
            return false;
        }

        RaycastHit hit;
        if (!_surface.ScreenCollider.Raycast(ray, out hit, 300f))
        {
            return false;
        }

        Transform quad = _surface.ScreenRenderer != null
            ? _surface.ScreenRenderer.transform
            : null;
        if (quad == null)
        {
            return false;
        }

        Vector3 local = quad.InverseTransformPoint(hit.point);
        // Unity Quad mesh spans local -0.5..0.5; v grows toward local +Y.
        uv = new Vector2(local.x + 0.5f, local.y + 0.5f);
        return true;
    }

    private Vector2 UvToPanelLocal(Vector2 uv)
    {
        Vector2 layout = _panel != null
            ? new Vector2(_panel.visualTree.layout.width,
                _panel.visualTree.layout.height)
            : new Vector2(WorldConsoleSurface.UiWidth, WorldConsoleSurface.UiHeight);
        float x = Mathf.Clamp01(uv.x) * layout.x;
        float y = (1f - Mathf.Clamp01(uv.y)) * layout.y; // single Y inversion
        return new Vector2(x, y);
    }

    private bool IsOverInteractiveElement(Vector2 panelPos)
    {
        if (_panel == null)
        {
            return false;
        }

        VisualElement hit = _panel.Pick(panelPos);
        for (VisualElement e = hit; e != null; e = e.parent)
        {
            if (e is Button)
            {
                return true;
            }
        }

        return false;
    }

    private static Vector2 OffscreenPosition()
    {
        return new Vector2(int.MinValue, int.MinValue);
    }

    // ---- Real UI Toolkit event synthesis (target-less dispatch) ----

    private void DispatchPointer<T>(Vector2 position, Vector2 delta, int button,
        int pressedButtons, int clickCount) where T : PointerEventBase<T>, new()
    {
        if (_panel == null)
        {
            return;
        }

        using (var evt = (T)EventBase<T>.GetPooled())
        {
            Set(evt, "pointerId", MousePointerId);
            Set(evt, "pointerType", PointerTypeMouse);
            Set(evt, "isPrimary", true);
            Set(evt, "position", new Vector3(position.x, position.y, 0f));
            Set(evt, "deltaPosition", new Vector3(delta.x, delta.y, 0f));
            Set(evt, "button", button);
            Set(evt, "pressedButtons", pressedButtons);
            Set(evt, "clickCount", clickCount);
            Set(evt, "modifiers", EventModifiers.None);
            _panel.visualTree.SendEvent(evt);
        }
    }

    private static void Set(object evt, string property, object value)
    {
        // PropertyInfo is per constructed event type (PointerMove/Down/Up),
        // so the cache key must include the concrete event type.
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
