using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Runtime audit of the product UI contracts that every automated evidence run
/// has to leave untouched.
///
/// Why this exists: the evidence probes measure the first-person arms by
/// rendering arms-only frames. The first version of that measurement parked the
/// screen-space UI by toggling <c>UIDocument.enabled</c>. In this project that
/// is destructive - no UXML carries a &lt;Style&gt; reference and no UXML hides
/// anything by default, because every controller attaches its style sheets and
/// applies its display state in code at bind time:
///
///   * AppController adds five style sheets and shows one of its four screens,
///   * ShipHudController adds the HUD sheet once,
///   * ShipPauseMenu adds its sheet and hides the pause overlay once.
///
/// A disable/enable cycle rebuilds the visual tree from the UXML alone, so the
/// game came back with default UI Toolkit styling, several screens stacked and
/// an unstyled pause overlay permanently on screen. The rules below turn that
/// silent damage into a failed check.
///
/// The audit is read-only: it never changes product state.
/// </summary>
public static class UiIntegrityAudit
{
    /// <summary>
    /// Text snapshot of the UI state a measurement must not change: the hub
    /// screen authority plus every document's enabled flag, style-sheet count
    /// and display state. Two snapshots taken around an operation have to be
    /// identical.
    /// </summary>
    public static string Snapshot()
    {
        var builder = new StringBuilder();
        AppController app = Object.FindAnyObjectByType<AppController>();
        if (app != null && app.Document != null
            && app.Document.rootVisualElement != null)
        {
            builder.Append("hub ").Append(app.CurrentScreen)
                   .Append(" visible=").Append(app.VisibleScreenCount)
                   .Append(" sheets=").Append(app.Document.rootVisualElement.styleSheets.count);
        }

        var documents = new List<UIDocument>(Object.FindObjectsByType<UIDocument>(
            FindObjectsInactive.Include));
        documents.Sort((left, right) =>
            left.GetEntityId().CompareTo(right.GetEntityId()));
        foreach (UIDocument document in documents)
        {
            VisualElement root = document.rootVisualElement;
            builder.Append(" | ").Append(document.name)
                   .Append(document.enabled ? " on" : " off")
                   .Append(" sheets=").Append(root != null ? root.styleSheets.count : -1)
                   .Append(" display=")
                   .Append(root != null ? root.resolvedStyle.display.ToString() : "none");
        }

        return builder.ToString();
    }

    /// <summary>
    /// The UI contracts every automated frame has to satisfy:
    ///
    ///   * the hub never shows more than ONE screen container at a time,
    ///   * a screen-space document always renders with the style sheets its
    ///     controller attached in code (a rebuilt panel tree silently drops
    ///     them and comes back as default UI Toolkit),
    ///   * the pause overlay is on screen exactly while the pause is open
    ///     (a rebuilt tree leaves an unstyled pause menu permanently visible).
    ///
    /// Returns an empty list when the UI is intact.
    /// </summary>
    public static List<string> Problems()
    {
        var problems = new List<string>();

        AppController app = Object.FindAnyObjectByType<AppController>();
        if (app != null && app.VisibleScreenCount > 1)
        {
            problems.Add("the hub shows " + app.VisibleScreenCount
                         + " screens at once (" + app.CurrentScreen + ")");
        }

        ShipGameController ship = Object.FindAnyObjectByType<ShipGameController>();
        bool pauseOpen = ship != null && ship.PauseMenu != null && ship.PauseMenu.IsOpen;

        foreach (UIDocument document in Object.FindObjectsByType<UIDocument>(
                     FindObjectsInactive.Exclude))
        {
            // Documents that render into their own RenderTexture (console screens,
            // the wrist terminal) are bound on demand and never composite into a
            // screen capture, so they are checked by the phase tests instead.
            if (!document.enabled || document.panelSettings == null
                || document.panelSettings.targetTexture != null)
            {
                continue;
            }

            VisualElement root = document.rootVisualElement;
            if (root == null)
            {
                continue;
            }

            if (root.styleSheets.count == 0)
            {
                problems.Add(document.name + " renders without any style sheet");
            }

            VisualElement pauseRoot = root.Q<VisualElement>("PauseRoot");
            if (pauseRoot != null)
            {
                bool visible = pauseRoot.resolvedStyle.display == DisplayStyle.Flex;
                if (visible != pauseOpen)
                {
                    problems.Add("the pause overlay is "
                                 + (visible ? "visible" : "hidden")
                                 + " while the pause is "
                                 + (pauseOpen ? "open" : "closed"));
                }
            }
        }

        return problems;
    }

    /// <summary>One-line report of the audit result, for a Check message.</summary>
    public static string Describe(List<string> problems)
    {
        return problems == null || problems.Count == 0
            ? "no stacked screens, no unstyled panel"
            : string.Join("; ", problems.ToArray());
    }
}
