using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 03 Play Mode validation for Expedition Preparation.
///
/// Follows the project convention of exactly one Boot -> Hub scene load per
/// test session (suite is run via its own -testFilter). Validates state
/// semantics first (single authoritative GameSession loadout), then that the
/// UI renders that state: default loadout, cargo, optional equipment toggles,
/// capacity guard, confirmation, edit-after-confirm, navigation persistence
/// and Reset integration.
/// </summary>
public class SurvivePhase03Tests
{
    private const int MaxWaitFrames = 600;

    private static IEnumerator WaitUntil(System.Func<bool> condition, int maxFrames = MaxWaitFrames)
    {
        int frames = 0;
        while (!condition() && frames < maxFrames)
        {
            frames++;
            yield return null;
        }
    }

    private static string ActiveSceneName()
    {
        return SceneManager.GetActiveScene().name;
    }

    [UnityTest]
    public IEnumerator BootToHub_Preparation_LoadoutJourney()
    {
        // Deterministic precondition.
        GameSession.Ensure().ResetSession();

        SceneManager.LoadScene("00_Boot");
        yield return WaitUntil(() => ActiveSceneName() == BootController.HubSceneName);
        Assert.AreEqual(BootController.HubSceneName, ActiveSceneName(), "Boot must reach Hub");

        AppController app = null;
        yield return WaitUntil(() =>
        {
            app = Object.FindAnyObjectByType<AppController>();
            return app != null && app.IsReady;
        });

        Assert.IsNotNull(app, "AppController not ready");
        VisualElement root = app.UiRoot;
        GameSession session = GameSession.Instance;
        Assert.IsNotNull(session, "session exists");

        for (int i = 0; i < 6; i++)
        {
            yield return null;
        }

        // ---- Phase 03 structure is present ----
        string[] structure =
        {
            ElementNames.MissionSummary,
            ElementNames.PreparationDestination,
            ElementNames.EnvironmentPanel,
            ElementNames.EquipmentPanel,
            ElementNames.CargoPanel,
            ElementNames.ReadinessPanel
        };

        foreach (string name in structure)
        {
            Assert.IsNotNull(root.Q<VisualElement>(name), "missing preparation element " + name);
        }

        // ---- Show Preparation: default loadout ----
        app.ShowScreen(AppScreen.Preparation);
        yield return null;
        yield return null;
        Assert.AreEqual(AppScreen.Preparation, app.CurrentScreen, "Preparation active");
        Assert.AreEqual(1, app.VisibleScreenCount, "one visible screen");

        Assert.AreEqual("45 / 100", LabelText(root, ElementNames.CargoValue), "default cargo 45/100");
        Assert.AreEqual("就绪", LabelText(root, ElementNames.LoadoutStateLabel), "default state ready (就绪)");
        Assert.AreEqual("45 / 100", LabelText(root, ElementNames.ReadinessCargoValue), "readiness cargo");

        Button batteryRow = root.Q<Button>(ElementNames.BatteryRowButton);
        Button droneRow = root.Q<Button>(ElementNames.DroneRowButton);
        Button confirm = root.Q<Button>(ElementNames.ConfirmLoadoutButton);
        Assert.IsNotNull(batteryRow, "battery row exists");
        Assert.IsNotNull(droneRow, "drone row exists");
        Assert.IsNotNull(confirm, "confirm button exists");

        Button harvesterRow = root.Q<Button>(ElementNames.HarvesterRowButton);
        Assert.IsTrue(harvesterRow.ClassListContains("row-required"), "harvester row required");
        Assert.IsTrue(harvesterRow.ClassListContains("row-selected"), "harvester always carried");
        Assert.IsFalse(batteryRow.ClassListContains("row-selected"), "battery not selected by default");
        Assert.IsFalse(droneRow.ClassListContains("row-selected"), "drone not selected by default");

        AssertBarRatio(root, ElementNames.CargoBarFill, 45f, "cargo bar defaults");
        Assert.IsTrue(confirm.enabledSelf, "confirm enabled for valid unconfirmed loadout");

        // Optional equipment must not touch Overview gameplay resources.
        int energyBefore = session.Energy;

        // ---- Select BATTERY PACK ----
        Assert.IsTrue(session.TrySetBatterySelected(true), "select battery accepted");
        yield return null;
        Assert.AreEqual("70 / 100", LabelText(root, ElementNames.CargoValue), "cargo after battery");
        Assert.IsTrue(batteryRow.ClassListContains("row-selected"), "battery row selected");
        Assert.AreEqual("已选择", ChipText(batteryRow), "battery chip selected (已选择)");
        Assert.AreEqual("就绪", LabelText(root, ElementNames.ReadinessPowerValue), "power readiness (就绪)");
        AssertBarRatio(root, ElementNames.CargoBarFill, 70f, "cargo bar after battery");

        // ---- Select SURVEY DRONE ----
        Assert.IsTrue(session.TrySetDroneSelected(true), "select drone accepted");
        yield return null;
        Assert.AreEqual("90 / 100", LabelText(root, ElementNames.CargoValue), "cargo after drone");
        Assert.AreEqual("就绪", LabelText(root, ElementNames.ReadinessSurveyValue), "survey readiness (就绪)");
        Assert.IsTrue(droneRow.ClassListContains("row-selected"), "drone row selected");

        // ---- Capacity guard exists and rejects impossible cargo ----
        Assert.IsFalse(session.CanCarry(GameSession.BatteryWeightKg),
            "cannot add another battery at 90/100");
        Assert.IsTrue(session.CanCarry(5), "small additions still allowed at 90/100");
        Assert.AreEqual(90, session.CargoWeight, "cargo derived state");

        // ---- Confirm locks the package ----
        Assert.IsTrue(session.TryConfirmLoadout(), "confirm accepted for valid loadout");
        yield return null;
        Assert.IsTrue(session.LoadoutConfirmed, "session confirmed");
        Assert.AreEqual("已确认", LabelText(root, ElementNames.LoadoutStateLabel), "state confirmed (已确认)");
        Assert.AreEqual("配置已确认", confirm.text, "confirm button relabeled after locking");
        Assert.IsFalse(confirm.enabledSelf, "confirm disabled once locked");

        // ---- Editing after confirmation clears it ----
        Assert.IsTrue(session.TrySetBatterySelected(false), "deselect battery accepted");
        yield return null;
        Assert.IsFalse(session.LoadoutConfirmed, "editing clears confirmation");
        Assert.AreEqual("65 / 100", LabelText(root, ElementNames.CargoValue), "cargo after battery removed");
        Assert.AreEqual("就绪", LabelText(root, ElementNames.LoadoutStateLabel), "back to ready (就绪)");
        Assert.AreEqual("确认配置", confirm.text, "confirm button relabeled back");
        Assert.IsTrue(confirm.enabledSelf, "confirm re-enabled");

        // ---- Navigation persistence: leave and return ----
        app.ShowScreen(AppScreen.Overview);
        yield return null;
        Assert.AreEqual(AppScreen.Overview, app.CurrentScreen, "Overview reached");
        Assert.AreEqual(energyBefore, session.Energy, "gameplay resources unaffected by loadout edits");

        app.ShowScreen(AppScreen.Preparation);
        yield return null;
        yield return null;
        Assert.AreEqual(AppScreen.Preparation, app.CurrentScreen, "Preparation reached again");
        Assert.AreEqual("65 / 100", LabelText(root, ElementNames.CargoValue), "cargo preserved across navigation");
        Assert.IsTrue(session.DroneSelected, "drone selection preserved");
        Assert.IsFalse(session.BatterySelected, "battery deselection preserved");

        // ---- Reset restores preparation defaults ----
        session.ResetSession();
        yield return null;
        Assert.AreEqual("45 / 100", LabelText(root, ElementNames.CargoValue), "cargo restored after reset");
        Assert.AreEqual("就绪", LabelText(root, ElementNames.LoadoutStateLabel), "state restored after reset (就绪)");
        Assert.IsFalse(session.BatterySelected, "battery reset");
        Assert.IsFalse(session.DroneSelected, "drone reset");
        Assert.IsFalse(session.LoadoutConfirmed, "confirmation reset");
        Assert.IsFalse(batteryRow.ClassListContains("row-selected"), "battery row UI reset");
        Assert.IsFalse(droneRow.ClassListContains("row-selected"), "drone row UI reset");
        Assert.IsTrue(confirm.enabledSelf, "confirm enabled after reset");
        Assert.AreEqual("74", LabelText(root, ElementNames.EnergyValue), "phase01 resources reset intact");
    }

    private static void AssertBarRatio(VisualElement root, string fillName, float expectedPercent, string label)
    {
        VisualElement fill = root.Q<VisualElement>(fillName);
        Assert.IsNotNull(fill, "missing bar fill " + fillName);
        Assert.IsNotNull(fill.parent, "bar fill has track parent");
        float track = fill.parent.resolvedStyle.width;
        float filled = fill.resolvedStyle.width;
        Assert.IsTrue(track > 1f, label + " track measured");
        float ratio = filled / track * 100f;
        Assert.IsTrue(Mathf.Abs(ratio - expectedPercent) <= 4f,
            label + " expected ~" + expectedPercent + "% but was " + ratio.ToString("0") + "%");
    }

    private static string ChipText(VisualElement row)
    {
        Label chip = row.Q<Label>(null, "equipment-chip");
        Assert.IsNotNull(chip, "row chip exists");
        return chip.text;
    }

    private static string LabelText(VisualElement root, string name)
    {
        Label label = root.Q<Label>(name);
        Assert.IsNotNull(label, "missing label element " + name);
        return label.text;
    }
}
