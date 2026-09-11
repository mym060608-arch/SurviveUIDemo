/// <summary>
/// The single authoritative "leave the application" action (Phase 08 pause work).
///
/// Exactly TWO product paths may reach it, and both must be an explicit confirm:
///
///   Main Menu -&gt; 退出          (confirm)
///   Pause Menu -&gt; 退出游戏      (confirm)
///
/// Back, Escape, Menu/Start, the D-Pad, the left stick and every other menu item
/// must never terminate the process; they route to a state change instead.
///
/// In a player this ends the application; inside the Unity Editor it leaves Play
/// Mode so the editor process always survives.
/// </summary>
public static class AppQuit
{
    /// <summary>How many times a quit was requested (automation only).</summary>
    public static int RequestCount { get; private set; }

    /// <summary>Who asked last, for diagnostics ("MainMenu.Exit", "Pause.Quit").</summary>
    public static string LastSource { get; private set; } = "<none>";

    /// <summary>
    /// Automation seam: while true the quit only records itself, so a test can
    /// verify WHICH path reached it without ending the editor or the player.
    /// Tests must reset it in teardown.
    /// </summary>
    public static bool SuppressForAutomation { get; set; }

    /// <summary>Requests application termination from an explicit confirm.</summary>
    public static void Request(string source)
    {
        RequestCount++;
        LastSource = source;

        var caller = new System.Diagnostics.StackFrame(1);
        UnityEngine.Debug.Log("[APP] Execute = QuitApplication (request #" + RequestCount
                              + " from " + source + ", caller "
                              + caller.GetMethod().DeclaringType.Name + "."
                              + caller.GetMethod().Name + ")");

        if (SuppressForAutomation)
        {
            return;
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        UnityEngine.Application.Quit();
#endif
    }

    /// <summary>Clears the quit bookkeeping (automation only).</summary>
    public static void ResetBookkeepingForAutomation()
    {
        RequestCount = 0;
        LastSource = "<none>";
        SuppressForAutomation = false;
    }
}
