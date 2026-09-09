using UnityEngine;

/// <summary>
/// Physical ship console placeholder (Phase 07 Checkpoint A).
///
/// Each of the four consoles (01 总览, 02 出发准备, 03 资源加工, 04 据点) is an
/// IFirstPersonInteractable so the gaze prompt works in ShipExplore mode. In
/// 07A the screen is a static graybox placeholder: E only records a notice.
/// The real RenderTexture UI + ShipConsole interaction mode arrive in
/// Phase 07 Checkpoint B.
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
    public string PromptText => "E  使用 " + _zhName + "终端";

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
        // Phase 07A contract: physical consoles are non-interactive placeholders.
        // Real world-space console interaction mode lands in checkpoint 07B.
        Debug.Log("[ShipConsoleInteractable] console " + _consoleId.ToString("00")
                  + " " + _zhName + " placeholder - ShipConsole mode arrives in 07B");
    }
}
