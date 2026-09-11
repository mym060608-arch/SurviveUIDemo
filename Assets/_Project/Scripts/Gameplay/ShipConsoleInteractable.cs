using UnityEngine;

/// <summary>
/// Physical ship console placeholder (Phase 07 Checkpoint A, role update
/// Phase 07B.2).
///
/// Each of the four consoles (01 总览, 02 仓库总览, 03 资源加工, 04 据点) is an
/// IFirstPersonInteractable so the gaze prompt works in ShipExplore mode. E
/// enters the console-focus mode (ShipConsoleFocusController) which lets the
/// player operate the physical screen's real UI through the world pointer.
/// </summary>
public class ShipConsoleInteractable : MonoBehaviour, IFirstPersonInteractable
{
    [SerializeField]
    private int _consoleId = 1;

    [SerializeField]
    private string _zhName = "总览";

    /// <summary>Console ordinal 1..4 (01 总览 .. 04 据点).</summary>
    public int ConsoleId => _consoleId;

    /// <summary>Chinese primary identity, e.g. 总览.</summary>
    public string ChineseName => _zhName;

    /// <summary>Chinese-first prompt shown while gazing at the console.</summary>
    public string PromptText => InputDeviceTracker.FormatPrompt("使用 " + _zhName + "终端");

    /// <summary>Builder configuration (called once at scene authoring time).</summary>
    public void Configure(int consoleId, string zhName)
    {
        _consoleId = consoleId;
        _zhName = zhName;
    }

    public bool CanInteract()
    {
        return true;
    }

    public string GetInteractionPrompt()
    {
        return PromptText;
    }

    public void Interact()
    {
        // Phase 07B: E on a physical terminal enters the smooth console
        // focus mode. The real interaction then runs through the focused
        // console's world-space UI (pointer router -> UI Toolkit events).
        ShipConsoleFocusController focus =
            Object.FindAnyObjectByType<ShipConsoleFocusController>();
        if (focus == null)
        {
            Debug.LogWarning("[ShipConsoleInteractable] no focus controller "
                             + "in scene - console " + _consoleId.ToString("00")
                             + " ignored E");
            return;
        }

        if (!focus.RequestEnter(this))
        {
            Debug.Log("[ShipConsoleInteractable] console " + _consoleId.ToString("00")
                      + " focus request rejected (another mode active)");
        }
    }
}
