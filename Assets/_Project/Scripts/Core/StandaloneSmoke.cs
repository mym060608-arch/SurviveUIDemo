using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Standalone smoke probe (validation instrumentation only).
///
/// Inert during normal play: it only activates when the player is launched
/// with  -smokeResult &lt;absolute path&gt;. It then drives the authoritative
/// Boot -> Hub -> state-mutation path once, writes a machine-readable result
/// file and quits with a matching exit code.
/// </summary>
public class StandaloneSmoke : MonoBehaviour
{
    private const string SmokeResultFlag = "-smokeResult";

    private string _resultPath;

    private void Awake()
    {
        _resultPath = ParseFlag(SmokeResultFlag);
        if (string.IsNullOrEmpty(_resultPath))
        {
            // Not a smoke launch: remove the probe entirely.
            enabled = false;
            return;
        }
    }

    private void Start()
    {
        if (!string.IsNullOrEmpty(_resultPath))
        {
            StartCoroutine(RunProbe());
        }
    }

    private static string ParseFlag(string flag)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private IEnumerator RunProbe()
    {
        var results = new StringBuilder();
        int failures = 0;

        // Wait until the Hub UI is ready (Boot -> Hub transition finished).
        AppController app = null;
        for (int i = 0; i < 600; i++)
        {
            app = FindFirstObjectByTypeHelper.Find<AppController>();
            if (app != null && app.IsReady && GameSession.Instance != null)
            {
                break;
            }

            yield return null;
        }

        try
        {
            if (app == null || GameSession.Instance == null)
            {
                Fail(results, ref failures, "hub UI never became ready");
            }
            else
            {
                GameSession session = GameSession.Instance;
                session.ResetSession();

                Check(results, ref failures, "defaults minerals=20 fuel=8 alloy=5 refinery=1",
                    session.Minerals == 20 && session.Fuel == 8 && session.Alloy == 5 && session.RefineryLevel == 1);

                session.AddMinerals(10);
                Check(results, ref failures, "after +10 minerals minerals=30", session.Minerals == 30);

                bool processed = session.TryProcessMinerals();
                Check(results, ref failures, "process ok -> minerals=25 fuel=10 alloy=6",
                    processed && session.Minerals == 25 && session.Fuel == 10 && session.Alloy == 6);

                bool upgraded = session.TryUpgradeRefinery();
                Check(results, ref failures, "upgrade ok -> alloy=3 refinery=2",
                    upgraded && session.Alloy == 3 && session.RefineryLevel == 2);

                // Navigation: every screen reachable, exactly one visible.
                app.ShowScreen(AppScreen.Overview);
                Check(results, ref failures, "screen overview active/unique",
                    app.CurrentScreen == AppScreen.Overview && app.VisibleScreenCount == 1);

                app.ShowScreen(AppScreen.Preparation);
                Check(results, ref failures, "screen preparation active/unique",
                    app.CurrentScreen == AppScreen.Preparation && app.VisibleScreenCount == 1);

                app.ShowScreen(AppScreen.Processing);
                Check(results, ref failures, "screen processing active/unique",
                    app.CurrentScreen == AppScreen.Processing && app.VisibleScreenCount == 1);

                app.ShowScreen(AppScreen.Outpost);
                Check(results, ref failures, "screen outpost active/unique",
                    app.CurrentScreen == AppScreen.Outpost && app.VisibleScreenCount == 1);

                // Cross-screen consistency on the Overview after mutations.
                app.ShowScreen(AppScreen.Overview);
                Label overviewMinerals = app.UiRoot?.Q<Label>(ElementNames.MineralsValue);
                Label overviewAlloy = app.UiRoot?.Q<Label>(ElementNames.AlloyValue);
                Check(results, ref failures, "overview reflects shared state (minerals=25 alloy=3)",
                    overviewMinerals != null && overviewMinerals.text == "25"
                    && overviewAlloy != null && overviewAlloy.text == "3");

                // Insufficient-resource safety: one more refinery upgrade must fail.
                bool overUpgrade = session.TryUpgradeRefinery();
                Check(results, ref failures, "second upgrade fails safely (alloy<3)",
                    !overUpgrade && session.RefineryLevel == 2 && session.Alloy == 3);

                session.ResetSession();
                Check(results, ref failures, "reset restores defaults minerals=20 fuel=8 alloy=5",
                    session.Minerals == 20 && session.Fuel == 8 && session.Alloy == 5);
            }
        }
        catch (Exception e)
        {
            failures++;
            results.AppendLine("FAIL :: unexpected exception: " + e);
        }

        results.AppendLine(failures == 0 ? "RESULT: SUCCESS" : "RESULT: FAILED");
        WriteResult(results.ToString(), failures == 0);
        Application.Quit(failures == 0 ? 0 : 1);
    }

    private static void Check(StringBuilder results, ref int failures, string label, bool condition)
    {
        if (condition)
        {
            results.AppendLine("PASS :: " + label);
        }
        else
        {
            failures++;
            results.AppendLine("FAIL :: " + label);
        }
    }

    private static void Fail(StringBuilder results, ref int failures, string label)
    {
        failures++;
        results.AppendLine("FAIL :: " + label);
    }

    private void WriteResult(string content, bool success)
    {
        try
        {
            File.WriteAllText(_resultPath, content);
            Debug.Log("[StandaloneSmoke] wrote result: " + _resultPath
                      + (success ? " SUCCESS" : " FAILED"));
        }
        catch (Exception e)
        {
            Debug.LogError("[StandaloneSmoke] failed to write result file: " + e);
        }
    }
}

/// <summary>Small indirection so the probe code stays clean on newer Unity APIs.</summary>
internal static class FindFirstObjectByTypeHelper
{
    public static T Find<T>() where T : UnityEngine.Object
    {
        return UnityEngine.Object.FindAnyObjectByType<T>();
    }
}
