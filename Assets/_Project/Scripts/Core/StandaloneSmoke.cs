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
            // Watchdog guarantees termination if an unexpected exception stops
            // the probe coroutine.
            StartCoroutine(QuitWatchdog());
        }
    }

    private static IEnumerator QuitWatchdog()
    {
        yield return new WaitForSecondsRealtime(120f);
        Debug.LogError("[StandaloneSmoke] watchdog timeout - forcing quit");
        Application.Quit(2);
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

        // No try/catch here: iterator bodies may not contain yield inside a
        // catch scope. Unexpected exceptions surface in the player log while
        // the watchdog guarantees the process terminates.
        {
            if (app == null || GameSession.Instance == null)
            {
                Fail(results, ref failures, "hub UI never became ready");
            }
            else
            {
                GameSession session = GameSession.Instance;
                session.ResetSession();

                Check(results, ref failures, "defaults minerals=20 fuel=8 alloy=5 modules LV1",
                    session.Minerals == 20 && session.Fuel == 8 && session.Alloy == 5
                    && session.RefineryLevel == 1 && session.HarvesterLevel == 1 && session.OutpostLevel == 1);

                // Derived defaults.
                Check(results, ref failures, "derived defaults eff=100 duration=2.0 cap=LV.2",
                    session.HarvesterEfficiencyPercent == 100
                    && Math.Abs(session.RefineryProcessSeconds - 2.0f) < 0.001f
                    && session.SubsystemLevelCap == 2);

                session.AddMinerals(10);
                Check(results, ref failures, "after +10 minerals minerals=30", session.Minerals == 30);

                // Batch refinery: start deducts, completion settles.
                bool started5 = session.TryStartProcess(5);
                session.AdvanceProcessing(session.RefineryProcessSeconds);
                Check(results, ref failures, "batch5 complete -> minerals=25 fuel=10 alloy=6",
                    started5 && session.Minerals == 25 && session.Fuel == 10 && session.Alloy == 6);

                // Phase 05 economy: Refinery LV.1 -> LV.2 costs 3 Alloy.
                bool upgradeOk = session.TryUpgrade(GameSession.OutpostModule.Refinery)
                                 == GameSession.UpgradeFailureReason.None;
                Check(results, ref failures, "refinery upgrade -> alloy=3 level=2 duration=1.6",
                    upgradeOk && session.Alloy == 3 && session.RefineryLevel == 2
                    && Math.Abs(session.RefineryProcessSeconds - 1.6f) < 0.001f);

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

                // Phase 03: Preparation loadout journey (Chinese-first copy).
                app.ShowScreen(AppScreen.Preparation);
                Label cargoLabel = app.UiRoot?.Q<Label>(ElementNames.CargoValue);
                Label stateLabel = app.UiRoot?.Q<Label>(ElementNames.LoadoutStateLabel);
                Button confirmButton = app.UiRoot?.Q<Button>(ElementNames.ConfirmLoadoutButton);
                Check(results, ref failures, "preparation defaults cargo 45/100 state 就绪 confirm enabled",
                    app.CurrentScreen == AppScreen.Preparation && app.VisibleScreenCount == 1
                    && cargoLabel != null && cargoLabel.text == "45 / 100"
                    && stateLabel != null && stateLabel.text == "就绪"
                    && confirmButton != null && confirmButton.enabledSelf);

                bool selectBattery = session.TrySetBatterySelected(true);
                Check(results, ref failures, "select battery -> cargo 70/100",
                    selectBattery && session.CargoWeight == 70
                    && cargoLabel != null && cargoLabel.text == "70 / 100");

                bool selectDrone = session.TrySetDroneSelected(true);
                Check(results, ref failures, "select drone -> cargo 90/100",
                    selectDrone && session.CargoWeight == 90
                    && cargoLabel != null && cargoLabel.text == "90 / 100");

                bool confirmLoadout = session.TryConfirmLoadout();
                Check(results, ref failures, "confirm loadout -> 已确认",
                    confirmLoadout && session.LoadoutConfirmed
                    && stateLabel != null && stateLabel.text == "已确认");

                // Leave Preparation and return: loadout state must persist.
                app.ShowScreen(AppScreen.Overview);
                app.ShowScreen(AppScreen.Preparation);
                Check(results, ref failures, "loadout preserved across navigation",
                    session.BatterySelected && session.DroneSelected && session.LoadoutConfirmed
                    && cargoLabel != null && cargoLabel.text == "90 / 100");

                bool editAfterConfirm = session.TrySetBatterySelected(false);
                Check(results, ref failures, "edit after confirm clears confirmation (cargo 65)",
                    editAfterConfirm && !session.LoadoutConfirmed
                    && cargoLabel != null && cargoLabel.text == "65 / 100");

                // Cross-screen consistency on the Overview after mutations.
                app.ShowScreen(AppScreen.Overview);
                Label overviewMinerals = app.UiRoot?.Q<Label>(ElementNames.MineralsValue);
                Label overviewAlloy = app.UiRoot?.Q<Label>(ElementNames.AlloyValue);
                Check(results, ref failures, "overview reflects shared state (minerals=25 alloy=3)",
                    overviewMinerals != null && overviewMinerals.text == "25"
                    && overviewAlloy != null && overviewAlloy.text == "3");

                // Module blockers: LV.3 needs Core LV.2; Core costs more than
                // the 3 remaining Alloy.
                bool blockedByCore = session.TryUpgrade(GameSession.OutpostModule.Refinery)
                                     == GameSession.UpgradeFailureReason.CoreLevelRequired;
                Check(results, ref failures, "refinery LV3 blocked by core (alloy unchanged)",
                    blockedByCore && session.RefineryLevel == 2 && session.Alloy == 3);
                bool coreLack = session.TryUpgrade(GameSession.OutpostModule.Core)
                                == GameSession.UpgradeFailureReason.InsufficientAlloy;
                Check(results, ref failures, "core upgrade insufficient alloy",
                    coreLack && session.OutpostLevel == 1 && session.Alloy == 3);

                // Outpost UI reflects state: refinery LV.2, new duration, CTA
                // shows the Core prerequisite and is disabled.
                app.ShowScreen(AppScreen.Outpost);
                yield return null;
                yield return null;
                Label outpostAlloy = app.UiRoot?.Q<Label>(ElementNames.OutpostAlloyValue);
                Label refineryStatus = app.UiRoot?.Q<Label>(ElementNames.RefineryLevelValue);
                Label detailDuration = app.UiRoot?.Q<Label>(ElementNames.ModuleCurrentEffectValue);
                Label detailName = app.UiRoot?.Q<Label>(ElementNames.ModuleDetailName);
                Button outpostUpgrade = app.UiRoot?.Q<Button>(ElementNames.OutpostUpgradeButton);
                Check(results, ref failures, "outpost UI shows refinery LV.2 duration 1.6 CTA core-locked",
                    outpostAlloy != null && outpostAlloy.text == "3"
                    && refineryStatus != null && refineryStatus.text == "LV.2"
                    && detailName != null && detailName.text == "精炼模块"
                    && detailDuration != null && detailDuration.text == "1.6s"
                    && outpostUpgrade != null && outpostUpgrade.text == "需要核心升级"
                    && !outpostUpgrade.enabledSelf);

                // Phase 04: Processing journey with real-time completion and
                // the Phase 05 duration integration visible.
                app.ShowScreen(AppScreen.Processing);
                Label mineralsLabel = app.UiRoot?.Q<Label>(ElementNames.ProcessingMineralsValue);
                Label fuelLabel = app.UiRoot?.Q<Label>(ElementNames.ProcessingFuelValue);
                Label statusLabel = app.UiRoot?.Q<Label>(ElementNames.ProcessingStatusLabel);
                Label durationMeta = app.UiRoot?.Q<Label>(ElementNames.ProcessingDurationValue);
                Label refineryMeta = app.UiRoot?.Q<Label>(ElementNames.ProcessingRefineryLevel);
                Button processStart = app.UiRoot?.Q<Button>(ElementNames.ProcessingStartButton);
                Check(results, ref failures, "processing shows refinery LV.2 duration 1.6s",
                    refineryMeta != null && refineryMeta.text == "LV.2"
                    && durationMeta != null && durationMeta.text == "1.6s");

                bool batch10 = session.TryStartProcess(10);
                Check(results, ref failures, "start batch 10 -> minerals=15 state Running",
                    batch10 && session.Minerals == 15 && session.IsProcessing
                    && mineralsLabel != null && mineralsLabel.text == "15"
                    && processStart != null && processStart.text == "加工中"
                    && !processStart.enabledSelf);

                // Navigate away and back while processing: state must persist.
                app.ShowScreen(AppScreen.Overview);
                app.ShowScreen(AppScreen.Preparation);
                app.ShowScreen(AppScreen.Processing);
                Check(results, ref failures, "processing persists across navigation",
                    session.IsProcessing && session.Minerals == 15);

                yield return WaitForProcessDone(session, 9000);
                Check(results, ref failures, "batch10 complete -> fuel=14 alloy=5",
                    session.ProcessState == GameSession.RefineryProcessState.Complete
                    && session.Fuel == 14 && session.Alloy == 5
                    && fuelLabel != null && fuelLabel.text == "14");

                // A second batch from Complete demonstrates re-start; the UI
                // selection (batch 5) matches this run, so "加工完成" is shown.
                bool again = session.TryStartProcess(5);
                Check(results, ref failures, "re-start batch 5 after complete -> minerals=10",
                    again && session.Minerals == 10 && session.IsProcessing);
                yield return WaitForProcessDone(session, 9000);
                Check(results, ref failures, "batch5 complete -> fuel=16 alloy=6 status 加工完成",
                    session.ProcessState == GameSession.RefineryProcessState.Complete
                    && session.Fuel == 16 && session.Alloy == 6
                    && statusLabel != null && statusLabel.text == "加工完成");

                session.ResetSession();
                Check(results, ref failures, "reset restores defaults minerals=20 fuel=8 alloy=5 modules LV1",
                    session.Minerals == 20 && session.Fuel == 8 && session.Alloy == 5
                    && session.RefineryLevel == 1 && session.HarvesterLevel == 1 && session.OutpostLevel == 1
                    && Math.Abs(session.RefineryProcessSeconds - 2.0f) < 0.001f);
            }
        }

        results.AppendLine(failures == 0 ? "RESULT: SUCCESS" : "RESULT: FAILED");
        WriteResult(results.ToString(), failures == 0);
        Application.Quit(failures == 0 ? 0 : 1);
    }

    /// <summary>Waits (bounded) for a running process to settle via the real
    /// session timer; completes immediately if already finished.</summary>
    private static IEnumerator WaitForProcessDone(GameSession session, int maxFrames)
    {
        int frames = 0;
        while (session.IsProcessing && frames < maxFrames)
        {
            frames++;
            yield return null;
        }
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
