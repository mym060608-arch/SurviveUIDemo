using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 01 Play Mode validation:
/// Boot -> Hub -> shared GameSession -> processing/outpost mutations -> UI
/// cross-screen consistency. Every test establishes its own preconditions.
/// </summary>
public class SurvivePhase01Tests
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

    private static int GameSessionCount()
    {
        return Object.FindObjectsByType<GameSession>().Length;
    }

    [UnityTest]
    public IEnumerator SessionState_FlowCycle_AddsProcessesUpgradesResets()
    {
        // Establish preconditions deterministically.
        GameSession session = GameSession.Ensure();
        session.ResetSession();

        // Default state.
        Assert.AreEqual(74, session.Energy, "default Energy");
        Assert.AreEqual(51, session.Food, "default Food");
        Assert.AreEqual(20, session.Minerals, "default Minerals");
        Assert.AreEqual(8, session.Fuel, "default Fuel");
        Assert.AreEqual(5, session.Alloy, "default Alloy");
        Assert.AreEqual(1, session.RefineryLevel, "default RefineryLevel");
        yield return null;

        // +10 Minerals.
        session.AddMinerals(10);
        Assert.AreEqual(30, session.Minerals, "Minerals after +10");

        // Process 5 Minerals -> +2 Fuel +1 Alloy.
        Assert.IsTrue(session.TryProcessMinerals(), "first process should succeed");
        Assert.AreEqual(25, session.Minerals, "Minerals after one process");
        Assert.AreEqual(10, session.Fuel, "Fuel after one process");
        Assert.AreEqual(6, session.Alloy, "Alloy after one process");

        // Upgrade Refinery: -3 Alloy, level+1.
        Assert.IsTrue(session.TryUpgradeRefinery(), "first upgrade should succeed");
        Assert.AreEqual(3, session.Alloy, "Alloy after upgrade");
        Assert.AreEqual(2, session.RefineryLevel, "RefineryLevel after upgrade");

        // Insufficient Alloy: upgrade must fail safely.
        // (Two upgrades are possible from the default 5 Alloy: 5 -> 2 -> 0.)
        Assert.IsTrue(session.TryUpgradeRefinery(), "second upgrade should succeed (Alloy 3)");
        Assert.AreEqual(0, session.Alloy, "Alloy after second upgrade");
        Assert.AreEqual(3, session.RefineryLevel, "RefineryLevel after second upgrade");
        Assert.IsFalse(session.TryUpgradeRefinery(), "third upgrade must fail (Alloy < 3)");
        Assert.AreEqual(3, session.RefineryLevel, "level unchanged on failed upgrade");
        Assert.AreEqual(0, session.Alloy, "Alloy unchanged on failed upgrade");

        // Insufficient Minerals: drain then verify failure is safe.
        // Minerals 25 -> 0 over five processes, the sixth fails.
        for (int i = 0; i < 5; i++)
        {
            Assert.IsTrue(session.TryProcessMinerals(), "process drain step " + i);
        }

        Assert.AreEqual(0, session.Minerals, "Minerals after drain to 0");
        Assert.IsFalse(session.TryProcessMinerals(), "process must fail with Minerals < 5");
        Assert.AreEqual(0, session.Minerals, "Minerals unchanged on failed process");

        // Reset restores defaults.
        session.ResetSession();
        Assert.AreEqual(74, session.Energy, "reset Energy");
        Assert.AreEqual(51, session.Food, "reset Food");
        Assert.AreEqual(20, session.Minerals, "reset Minerals");
        Assert.AreEqual(8, session.Fuel, "reset Fuel");
        Assert.AreEqual(5, session.Alloy, "reset Alloy");
        Assert.AreEqual(1, session.RefineryLevel, "reset RefineryLevel");
    }

    [UnityTest]
    public IEnumerator BootScene_TransitionsToHub_WithSinglePersistentSession()
    {
        GameSession session = GameSession.Ensure();
        session.ResetSession();

        SceneManager.LoadScene("00_Boot");
        yield return WaitUntil(() => ActiveSceneName() == BootController.HubSceneName);

        Assert.AreEqual(BootController.HubSceneName, ActiveSceneName(), "Boot must reach Hub");

        // Wait for the Hub UI application to bind.
        AppController app = null;
        yield return WaitUntil(() =>
        {
            app = Object.FindAnyObjectByType<AppController>();
            return app != null && app.IsReady;
        });

        Assert.IsNotNull(app, "AppController did not become ready in Hub");

        // Exactly one authoritative session.
        Assert.AreEqual(1, GameSessionCount(), "exactly one GameSession must exist");
        Assert.IsNotNull(GameSession.Instance, "GameSession.Instance must exist");
        Assert.AreEqual(20, GameSession.Instance.Minerals, "Boot fresh-launch defaults Minerals");
        Assert.AreEqual(8, GameSession.Instance.Fuel, "Boot fresh-launch defaults Fuel");
        Assert.AreEqual(5, GameSession.Instance.Alloy, "Boot fresh-launch defaults Alloy");
    }

    [UnityTest]
    public IEnumerator Hub_Navigation_ShowsExactlyOneScreen()
    {
        GameSession.Ensure().ResetSession();
        SceneManager.LoadScene("01_Hub");

        AppController app = null;
        yield return WaitUntil(() =>
        {
            app = Object.FindAnyObjectByType<AppController>();
            return app != null && app.IsReady;
        });

        Assert.IsNotNull(app, "AppController did not become ready");
        Assert.AreEqual(AppScreen.Overview, app.CurrentScreen, "initial screen");
        Assert.AreEqual(1, app.VisibleScreenCount, "exactly one visible screen initially");

        app.ShowScreen(AppScreen.Preparation);
        yield return null;
        Assert.AreEqual(AppScreen.Preparation, app.CurrentScreen, "after Preparation");
        Assert.AreEqual(1, app.VisibleScreenCount, "one visible screen after Preparation");

        app.ShowScreen(AppScreen.Processing);
        yield return null;
        Assert.AreEqual(AppScreen.Processing, app.CurrentScreen, "after Processing");
        Assert.AreEqual(1, app.VisibleScreenCount, "one visible screen after Processing");

        app.ShowScreen(AppScreen.Outpost);
        yield return null;
        Assert.AreEqual(AppScreen.Outpost, app.CurrentScreen, "after Outpost");
        Assert.AreEqual(1, app.VisibleScreenCount, "one visible screen after Outpost");

        app.ShowScreen(AppScreen.Overview);
        yield return null;
        Assert.AreEqual(AppScreen.Overview, app.CurrentScreen, "back to Overview");
        Assert.AreEqual(1, app.VisibleScreenCount, "one visible screen back on Overview");
    }

    [UnityTest]
    public IEnumerator ProcessingAndUpgrade_Mutations_ReflectAcrossScreens()
    {
        GameSession session = GameSession.Ensure();
        session.ResetSession();
        SceneManager.LoadScene("01_Hub");

        AppController app = null;
        yield return WaitUntil(() =>
        {
            app = Object.FindAnyObjectByType<AppController>();
            return app != null && app.IsReady;
        });

        Assert.IsNotNull(app, "AppController did not become ready");
        VisualElement root = app.UiRoot;
        Assert.IsNotNull(root, "UI root must exist");

        // Overview shows defaults.
        Assert.AreEqual("74", LabelText(root, ElementNames.EnergyValue), "Overview Energy text");
        Assert.AreEqual("20", LabelText(root, ElementNames.MineralsValue), "Overview Minerals text");

        // Process once from the Processing screen path.
        app.ShowScreen(AppScreen.Processing);
        yield return null;
        Assert.AreEqual("20", LabelText(root, ElementNames.ProcessingMineralsValue), "Processing raw minerals before");

        Assert.IsTrue(session.TryProcessMinerals(), "process should succeed with 20 Minerals");
        Assert.AreEqual("15", LabelText(root, ElementNames.ProcessingMineralsValue), "Processing raw minerals after");
        Assert.AreEqual("10", LabelText(root, ElementNames.ProcessingFuelValue), "Processing fuel after");
        Assert.AreEqual("6", LabelText(root, ElementNames.ProcessingAlloyValue), "Processing alloy after");

        // Overview must show the same values after switching back.
        app.ShowScreen(AppScreen.Overview);
        yield return null;
        Assert.AreEqual("15", LabelText(root, ElementNames.MineralsValue), "Overview Minerals cross-screen");
        Assert.AreEqual("10", LabelText(root, ElementNames.FuelValue), "Overview Fuel cross-screen");
        Assert.AreEqual("6", LabelText(root, ElementNames.AlloyValue), "Overview Alloy cross-screen");

        // Upgrade once from the Outpost screen path.
        app.ShowScreen(AppScreen.Outpost);
        yield return null;
        Assert.AreEqual("6", LabelText(root, ElementNames.OutpostAlloyValue), "Outpost Alloy before upgrade");
        Assert.IsTrue(session.TryUpgradeRefinery(), "upgrade should succeed with 6 Alloy");
        Assert.AreEqual("3", LabelText(root, ElementNames.OutpostAlloyValue), "Outpost Alloy after upgrade");
        Assert.AreEqual("REFINERY LV.2", LabelText(root, ElementNames.RefineryLevelValue), "Outpost refinery level after");

        // Cross-screen persistence again.
        app.ShowScreen(AppScreen.Overview);
        yield return null;
        Assert.AreEqual("3", LabelText(root, ElementNames.AlloyValue), "Overview Alloy after upgrade");

        // Button enablement must follow the shared state.
        Button processButton = root.Q<Button>(ElementNames.ProcessButton);
        Assert.IsNotNull(processButton, "ProcessButton must exist");
        Assert.IsTrue(processButton.enabledSelf, "ProcessButton enabled while Minerals >= 5");

        app.ShowScreen(AppScreen.Outpost);
        yield return null;
        Button upgradeButton = root.Q<Button>(ElementNames.UpgradeRefineryButton);
        Assert.IsNotNull(upgradeButton, "UpgradeRefineryButton must exist");
        Assert.IsTrue(upgradeButton.enabledSelf, "Upgrade button enabled while Alloy >= 3");

        // Second upgrade drains Alloy to 0 -> button disabled, level 3.
        Assert.IsTrue(session.TryUpgradeRefinery(), "second upgrade succeeds (3 Alloy)");
        Assert.AreEqual("0", LabelText(root, ElementNames.OutpostAlloyValue), "Outpost Alloy drained");
        Assert.AreEqual("REFINERY LV.3", LabelText(root, ElementNames.RefineryLevelValue), "Outpost refinery level after drain");
        Assert.IsFalse(upgradeButton.enabledSelf, "Upgrade button disabled while Alloy < 3");

        // Reset returns Overview defaults.
        session.ResetSession();
        Assert.AreEqual("74", LabelText(root, ElementNames.EnergyValue), "Overview Energy after reset");
        Assert.AreEqual("20", LabelText(root, ElementNames.MineralsValue), "Overview Minerals after reset");
        Assert.AreEqual("8", LabelText(root, ElementNames.FuelValue), "Overview Fuel after reset");
        Assert.AreEqual("5", LabelText(root, ElementNames.AlloyValue), "Overview Alloy after reset");
    }

    private static string LabelText(VisualElement root, string name)
    {
        Label label = root.Q<Label>(name);
        Assert.IsNotNull(label, "missing label element " + name);
        return label.text;
    }
}
