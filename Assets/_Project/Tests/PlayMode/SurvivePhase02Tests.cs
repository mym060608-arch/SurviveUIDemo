using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 02 Play Mode validation for the Overview visual system.
///
/// Keeps the Phase 01 convention of a single Boot -> Hub scene load per test
/// session (the suite is run separately from SurvivePhase01Tests so every
/// session still performs exactly one full Boot flow).
///
/// Covers: Overview structure presence, live GameSession binding, survival
/// bar geometry proportional to session values, planet texture presence,
/// navigation active state and hidden-by-default debug UI.
/// </summary>
public class SurvivePhase02Tests
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
    public IEnumerator BootToHub_OverviewVisualSystem_LiveState()
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

        Assert.IsNotNull(app, "AppController did not become ready in Hub");
        VisualElement root = app.UiRoot;
        Assert.IsNotNull(root, "UI root must exist");

        // Let the panel complete a few layout passes before measuring geometry.
        for (int i = 0; i < 6; i++)
        {
            yield return null;
        }

        // ---- Overview is the only visible screen, default state bound ----
        Assert.AreEqual(AppScreen.Overview, app.CurrentScreen, "initial screen is Overview");
        Assert.AreEqual(1, app.VisibleScreenCount, "exactly one visible screen");
        Assert.AreEqual("74", LabelText(root, ElementNames.EnergyValue), "default Energy");
        Assert.AreEqual("51", LabelText(root, ElementNames.FoodValue), "default Food");
        Assert.AreEqual("20", LabelText(root, ElementNames.MineralsValue), "default Minerals");
        Assert.AreEqual("8", LabelText(root, ElementNames.FuelValue), "default Fuel");
        Assert.AreEqual("5", LabelText(root, ElementNames.AlloyValue), "default Alloy");
        Assert.AreEqual("NEREID-04", LabelText(root, ElementNames.DestinationValue), "destination hero");

        // ---- Phase 02 visual composition structure is present ----
        string[] structure =
        {
            ElementNames.OverviewTopBar,
            ElementNames.ShipStatusPanel,
            ElementNames.ShipSurvivalGroup,
            ElementNames.ShipResourceGroup,
            ElementNames.DestinationPanel,
            ElementNames.PlanetStage,
            ElementNames.PlanetDisc,
            ElementNames.ObjectivePanel,
            ElementNames.MissionStatusBlock
        };

        foreach (string name in structure)
        {
            Assert.IsNotNull(root.Q<VisualElement>(name), "missing overview element " + name);
        }

        // Survival bars are geometrically proportional to session values.
        AssertBarRatio(root, ElementNames.EnergyBarFill, 74f, "energy bar");
        AssertBarRatio(root, ElementNames.FoodBarFill, 51f, "food bar");

        // Procedural planet texture is applied to the center visual.
        VisualElement planet = root.Q<VisualElement>(ElementNames.PlanetDisc);
        Assert.IsNotNull(planet, "planet disc exists");
        Assert.IsNotNull(planet.style.backgroundImage.value.texture,
            "planet disc has a procedural texture");

        // Primary CTA exists, enabled and labeled.
        Button cta = root.Q<Button>(ElementNames.OpenPreparationButton);
        Assert.IsNotNull(cta, "primary CTA exists");
        Assert.IsTrue(cta.enabledSelf, "primary CTA enabled");
        Label ctaLabel = cta.Q<Label>(null, "cta-label");
        Assert.IsNotNull(ctaLabel, "CTA label element exists");
        Assert.AreEqual("PREPARE EXPEDITION  »", ctaLabel.text, "CTA label text");

        // Navigation: Overview is the active tab, all four tabs exist.
        Assert.IsTrue(HasClass(root, ElementNames.NavOverview, "tab-active"), "Overview tab active");
        Assert.IsFalse(HasClass(root, ElementNames.NavPreparation, "tab-active"), "Preparation tab inactive");
        Assert.IsFalse(HasClass(root, ElementNames.NavProcessing, "tab-active"), "Processing tab inactive");
        Assert.IsFalse(HasClass(root, ElementNames.NavOutpost, "tab-active"), "Outpost tab inactive");

        // Debug UI is hidden by default (never in the competition screenshot).
        VisualElement debugPanel = root.Q<VisualElement>(ElementNames.DebugPanel);
        Assert.IsNotNull(debugPanel, "debug panel exists");
        Assert.AreEqual(DisplayStyle.None, debugPanel.resolvedStyle.display, "debug panel hidden by default");

        // ---- Live shared-state updates flow into Overview ----
        GameSession session = GameSession.Instance;
        Assert.IsNotNull(session, "session exists");

        session.AddMinerals(10);
        yield return null;
        Assert.AreEqual("30", LabelText(root, ElementNames.MineralsValue), "Minerals live update");

        session.ResetSession();
        yield return null;
        Assert.AreEqual("20", LabelText(root, ElementNames.MineralsValue), "Minerals after reset");
        Assert.AreEqual("74", LabelText(root, ElementNames.EnergyValue), "Energy after reset");
        Assert.AreEqual("8", LabelText(root, ElementNames.FuelValue), "Fuel after reset");
        Assert.AreEqual("5", LabelText(root, ElementNames.AlloyValue), "Alloy after reset");
        AssertBarRatio(root, ElementNames.EnergyBarFill, 74f, "energy bar after reset");

        // A later screen still keeps Overview consistent when revisited.
        app.ShowScreen(AppScreen.Preparation);
        yield return null;
        Assert.AreEqual(1, app.VisibleScreenCount, "one visible screen on Preparation");
        app.ShowScreen(AppScreen.Overview);
        yield return null;
        Assert.AreEqual(1, app.VisibleScreenCount, "one visible screen back on Overview");
        Assert.AreEqual("20", LabelText(root, ElementNames.MineralsValue), "Overview consistent on return");
    }

    private static void AssertBarRatio(VisualElement root, string fillName, float expectedPercent, string label)
    {
        VisualElement fill = root.Q<VisualElement>(fillName);
        Assert.IsNotNull(fill, "missing bar fill " + fillName);
        Assert.IsNotNull(fill.parent, "bar fill has a track parent");

        float track = fill.parent.resolvedStyle.width;
        float filled = fill.resolvedStyle.width;
        Assert.IsTrue(track > 1f, label + " track measured");
        float ratio = filled / track * 100f;
        Assert.IsTrue(Mathf.Abs(ratio - expectedPercent) <= 5f,
            label + " fill ratio expected ~" + expectedPercent + "% but was " + ratio.ToString("0") + "%");
    }

    private static bool HasClass(VisualElement root, string elementName, string className)
    {
        VisualElement element = root.Q<VisualElement>(elementName);
        return element != null && element.ClassListContains(className);
    }

    private static string LabelText(VisualElement root, string name)
    {
        Label label = root.Q<Label>(name);
        Assert.IsNotNull(label, "missing label element " + name);
        return label.text;
    }
}
