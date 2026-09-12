using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// One row of the shared controller prompt bar (Phase 09B PASS 1).
///
/// A row is a LABEL plus the logical actions that drive it:
///
///     ControllerPromptRow.Key("确认", UiAction.Confirm)        -> "[A] 确认"
///     ControllerPromptRow.Key("缩放", UiAction.ZoomOut, UiAction.ZoomIn)
///                                                             -> "[LT/RT] 缩放"
///
/// The logical action never changes with the device; only the glyph printed in
/// the brackets does (A / ✕ / E, LT-RT / L2-R2 / 滚轮). A row whose function is
/// not available in the current state is simply NOT added - the contract is
/// "没有对应功能时不要显示该按键", so the bar must never show a dead key.
/// </summary>
public struct ControllerPromptRow
{
    /// <summary>Player-facing text of the row ("光标", "选择", "返回").</summary>
    public string Label;

    /// <summary>Logical actions that drive the row (rendered as glyph/glyph).</summary>
    public UiAction[] Actions;

    /// <summary>True when this row can be shown at all.</summary>
    public bool IsValid => !string.IsNullOrEmpty(Label) && Actions != null
                           && Actions.Length > 0;

    /// <summary>Builds one row from a label and its logical action(s).</summary>
    public static ControllerPromptRow Key(string label, params UiAction[] actions)
    {
        return new ControllerPromptRow
        {
            Label = label,
            Actions = actions
        };
    }
}

/// <summary>
/// The shared dynamic controller prompt bar (Phase 09B PASS 1).
///
/// This is presentation ONLY and it owns no input: it asks
/// <see cref="InputDeviceTracker.PromptGlyph"/> for the glyph of each logical
/// action on the device the player is actually holding, and the owning page
/// decides which rows exist in the current state. That split is what makes the
/// bar reusable by the surface map today and by the wrist, processing and outpost
/// pages in the later PASSes - the pages own semantics, the bar owns wording.
///
/// The bar is built in code (like CompassTapeElement and RadialWristElement)
/// because it must appear in existing documents without an art pass: no UXML and
/// no USS file is touched. It lives OUTSIDE the page's own layer so a page can
/// hide its HUD and still keep its prompt row (the surface map does exactly that
/// while the wrist hologram is open).
///
/// The bar refreshes itself only when the device or the gamepad family actually
/// changes, so a per-frame <see cref="Tick"/> is a comparison, never a rebuild.
/// </summary>
public sealed class ControllerPromptBar : VisualElement
{
    /// <summary>Stable element name for probes and audits.</summary>
    public const string ElementName = "ControllerPromptBar";

    private const float RowFontSize = 13f;
    private const float RowSpacing = 8f;

    private readonly List<Label> _cells = new List<Label>();
    private ControllerPromptRow[] _rows = new ControllerPromptRow[0];
    private Font _font;
    private bool _visible;

    private InputDeviceTracker.Device _builtDevice = InputDeviceTracker.Device.Gamepad;
    private InputDeviceTracker.GamepadFamily _builtFamily = InputDeviceTracker.GamepadFamily.Generic;
    private bool _built;

    /// <summary>The device the current glyphs were built for (diagnostics/tests).</summary>
    public InputDeviceTracker.Device BuiltForDevice => _builtDevice;

    /// <summary>
    /// The bar text exactly as the player reads it, e.g.
    /// "[RS] 光标   [A] 选择   [LT/RT] 缩放   [B] 返回".
    /// </summary>
    public string PromptText
    {
        get
        {
            var builder = new System.Text.StringBuilder();
            foreach (Label cell in _cells)
            {
                if (cell.style.display == DisplayStyle.None)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append("   ");
                }

                builder.Append(cell.text);
            }

            return builder.ToString();
        }
    }

    /// <summary>Number of rows the player can actually read right now.</summary>
    public int VisibleRowCount
    {
        get
        {
            int count = 0;
            foreach (Label cell in _cells)
            {
                if (cell.style.display != DisplayStyle.None)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>
    /// Creates a bar under <paramref name="host"/> once (idempotent: a second call
    /// returns the existing bar instead of stacking a second prompt row).
    /// </summary>
    public static ControllerPromptBar Attach(VisualElement host, Font font = null)
    {
        if (host == null)
        {
            return null;
        }

        ControllerPromptBar existing = host.Q<ControllerPromptBar>(ElementName);
        if (existing != null)
        {
            return existing;
        }

        var bar = new ControllerPromptBar
        {
            name = ElementName,
            pickingMode = PickingMode.Ignore,
            _font = font
        };

        // A small bottom-right strip: it never covers the page's own content and,
        // on the surface, it stays clear of the wrist hologram (which sits on the
        // lower LEFT of the frame).
        bar.style.position = Position.Absolute;
        bar.style.right = 26f;
        bar.style.bottom = 20f;
        bar.style.flexDirection = FlexDirection.Row;
        bar.style.alignItems = Align.Center;
        bar.style.display = DisplayStyle.None;
        host.Add(bar);
        return bar;
    }

    /// <summary>
    /// Replaces the rows. Rows whose actions are empty are dropped, so "this key
    /// has no function here" is expressed by simply not listing it.
    /// </summary>
    public void SetRows(params ControllerPromptRow[] rows)
    {
        var kept = new List<ControllerPromptRow>();
        if (rows != null)
        {
            foreach (ControllerPromptRow row in rows)
            {
                if (row.IsValid)
                {
                    kept.Add(row);
                }
            }
        }

        _rows = kept.ToArray();
        Rebuild(force: true);
    }

    /// <summary>Shows or hides the whole bar (no empty background when hidden).</summary>
    public void SetVisible(bool visible)
    {
        _visible = visible;
        ApplyDisplay();
    }

    /// <summary>True while the bar is drawn.</summary>
    public bool IsShown => _visible && _rows.Length > 0
                           && resolvedStyle.display != DisplayStyle.None;

    /// <summary>
    /// Rebuilds the glyphs when the player switched devices. Cheap enough to call
    /// every frame: it only compares the device and the pad family.
    /// </summary>
    public void Tick()
    {
        Rebuild(force: false);
    }

    private void Rebuild(bool force)
    {
        InputDeviceTracker.Device device = InputDeviceTracker.LastDevice;
        InputDeviceTracker.GamepadFamily family = device == InputDeviceTracker.Device.Gamepad
            ? InputDeviceTracker.CurrentFamily
            : InputDeviceTracker.GamepadFamily.Generic;

        if (!force && _built && device == _builtDevice && family == _builtFamily)
        {
            return;
        }

        _built = true;
        _builtDevice = device;
        _builtFamily = family;

        if (_cells.Count != _rows.Length)
        {
            RebuildCells();
        }

        for (int i = 0; i < _rows.Length; i++)
        {
            _cells[i].text = Format(_rows[i]);
        }

        ApplyDisplay();
    }

    /// <summary>
    /// The bar is drawn only while the owner asked for it AND it has at least one
    /// row: an empty prompt strip must never leave a background pill on screen.
    /// </summary>
    private void ApplyDisplay()
    {
        style.display = _visible && _rows.Length > 0
            ? DisplayStyle.Flex
            : DisplayStyle.None;
    }

    private void RebuildCells()
    {
        Clear();
        _cells.Clear();

        for (int i = 0; i < _rows.Length; i++)
        {
            var cell = new Label
            {
                pickingMode = PickingMode.Ignore
            };

            cell.style.fontSize = RowFontSize;
            cell.style.color = new Color(0.92f, 0.96f, 1f);
            cell.style.backgroundColor = new Color(0.03f, 0.05f, 0.08f, 0.62f);
            cell.style.paddingLeft = 8f;
            cell.style.paddingRight = 8f;
            cell.style.paddingTop = 3f;
            cell.style.paddingBottom = 3f;
            cell.style.marginLeft = RowSpacing;
            cell.style.borderTopLeftRadius = 3f;
            cell.style.borderTopRightRadius = 3f;
            cell.style.borderBottomLeftRadius = 3f;
            cell.style.borderBottomRightRadius = 3f;

            if (_font != null)
            {
                cell.style.unityFont = _font;
            }

            Add(cell);
            _cells.Add(cell);
        }
    }

    /// <summary>Formats one row for the current device: "[A] 确认".</summary>
    public static string Format(ControllerPromptRow row)
    {
        if (!row.IsValid)
        {
            return string.Empty;
        }

        var glyphs = new System.Text.StringBuilder();
        foreach (UiAction action in row.Actions)
        {
            string glyph = InputDeviceTracker.PromptGlyph(action);
            if (string.IsNullOrEmpty(glyph))
            {
                continue;
            }

            if (glyphs.Length > 0)
            {
                glyphs.Append('/');
            }

            glyphs.Append(glyph);
        }

        if (glyphs.Length == 0)
        {
            return string.Empty;
        }

        return "[" + glyphs + "] " + row.Label;
    }

    /// <summary>The standard exploration / console page row set (Phase 09B §5).</summary>
    public static ControllerPromptRow[] PageRows()
    {
        return new[]
        {
            ControllerPromptRow.Key("切换", UiAction.PreviousTab, UiAction.NextTab),
            ControllerPromptRow.Key("确认", UiAction.Confirm),
            ControllerPromptRow.Key("返回", UiAction.Back)
        };
    }

    /// <summary>
    /// The surface map row set: cursor, select, zoom, back. LB/RB and the left
    /// stick are deliberately absent - this map has no tabs/layers and no pan, and
    /// the bar must never advertise a key that does nothing.
    /// </summary>
    public static ControllerPromptRow[] SurfaceMapRows()
    {
        return new[]
        {
            ControllerPromptRow.Key("光标", UiAction.MapCursor),
            ControllerPromptRow.Key("选择", UiAction.Confirm),
            ControllerPromptRow.Key("缩放", UiAction.ZoomOut, UiAction.ZoomIn),
            ControllerPromptRow.Key("返回", UiAction.Back)
        };
    }
}
