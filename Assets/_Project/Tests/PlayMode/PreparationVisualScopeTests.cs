using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Scoped verification for the Preparation / 出发准备 screen only.
///
/// It covers the three things this pass is allowed to touch:
///
///   * STRUCTURE - the page still binds every element it had (a missing one is
///     a Debug.LogError, and the runner fails on an unexpected error), the
///     existing information architecture is still on screen, and the new
///     telemetry / lamp / marker elements exist;
///   * BEHAVIOUR - equipping and unequipping a device still drives the payload
///     read-out, the bar, the readiness rows and the state lamp through the
///     real pointer path, and the focus never leaves the page;
///   * EVIDENCE - 1920x1080 runtime frames of the default, equipped and
///     mission-detail states for human review.
///
/// No other screen is loaded, asserted or captured here.
/// </summary>
public class PreparationVisualScopeTests
{
    private const string Output =
        @"D:\dsh_gpt_unity_blender\project\SurviveUIDemo\PreparationEvidence";

    private const int ShotWidth = 1920;
    private const int ShotHeight = 1080;

    /// <summary>Hub app of the current test (set by <see cref="LoadPreparation"/>).</summary>
    private AppController _app;

    [UnityTest]
    public IEnumerator Preparation_Structure_DefaultState_AndCapture()
    {
        yield return LoadPreparation();
        AppController app = _app;

        VisualElement root = app.UiRoot.Q<VisualElement>(ElementNames.PreparationScreen);
        Assert.IsNotNull(root, "preparation screen root");

        // Existing information architecture: every panel and read-out is bound.
        Assert.IsNotNull(root.Q<VisualElement>(ElementNames.EnvironmentPanel), "environment panel");
        Assert.IsNotNull(root.Q<VisualElement>(ElementNames.EquipmentPanel), "equipment panel");
        Assert.IsNotNull(root.Q<VisualElement>(ElementNames.CargoPanel), "payload panel");
        Assert.IsNotNull(root.Q<VisualElement>(ElementNames.ReadinessPanel), "readiness panel");
        Assert.IsNotNull(root.Q<VisualElement>("PrepMissionPanel"), "mission panel");
        Assert.IsNotNull(root.Q<Button>(ElementNames.ConfirmLoadoutButton), "confirm button");

        // Telemetry rows keep their values (no data was invented for the pass).
        Assert.AreEqual("-37 °C", EnvValue(root, 0), "temperature");
        Assert.AreEqual("0.72 G", EnvValue(root, 1), "gravity");
        Assert.AreEqual("中等", EnvValue(root, 2), "radiation");
        Assert.AreEqual("稀薄", EnvValue(root, 3), "atmosphere");
        Assert.AreEqual("未知", EnvValue(root, 4), "surface");
        Assert.AreEqual(5, root.Q("EnvironmentPanel").Query<VisualElement>(className: "env-row").ToList().Count,
            "five telemetry rows");
        Assert.IsNotNull(root.Q<VisualElement>("EnvSurfaceState"), "surface scan lamp");

        // Equipment slots keep their identity, weight and required/optional chip.
        Assert.AreEqual("采集器 MK-I", RowLabel(root, ElementNames.HarvesterRowButton, "equipment-name"));
        Assert.AreEqual("备用电池组", RowLabel(root, ElementNames.BatteryRowButton, "equipment-name"));
        Assert.AreEqual("勘测无人机", RowLabel(root, ElementNames.DroneRowButton, "equipment-name"));
        Assert.AreEqual("45", RowLabel(root, ElementNames.HarvesterRowButton, "equipment-weight"));
        Assert.AreEqual("25", RowLabel(root, ElementNames.BatteryRowButton, "equipment-weight"));
        Assert.AreEqual("20", RowLabel(root, ElementNames.DroneRowButton, "equipment-weight"));
        Assert.AreEqual("必需", RowLabel(root, ElementNames.HarvesterRowButton, "equipment-chip"));
        Assert.IsTrue(root.Q<Button>(ElementNames.HarvesterRowButton).ClassListContains("row-selected"),
            "harvester is always carried");

        // Payload + readiness defaults.
        Assert.AreEqual("45 / 100", root.Q<Label>(ElementNames.CargoValue).text, "default payload");
        Assert.AreEqual("就绪", root.Q<Label>(ElementNames.LoadoutStateLabel).text, "default readiness");
        Assert.AreEqual("任务准备度", HeaderText(root, ElementNames.ReadinessPanel), "readiness header");
        Assert.IsTrue(root.Q<VisualElement>("ReadinessState").ClassListContains("ready"),
            "readiness lamp lit on a valid loadout");
        Assert.AreEqual(5, root.Q("ReadinessPanel").Query<VisualElement>(className: "readiness-row").ToList().Count,
            "five readiness checks still shown");
        Assert.IsTrue(ReadinessRow(root, ElementNames.ReadinessPowerValue).ClassListContains("row-active") == false,
            "optional energy check is not lit by default");

        // The pass's own additions are mounted exactly once.
        Assert.IsNotNull(root.Q<PreparationBackdrop>(PreparationBackdrop.ElementName), "backdrop");
        Assert.AreEqual(1, root.Q<VisualElement>("HarvesterSketch").childCount, "harvester sketch drawn once");
        Assert.AreEqual(1, root.Q<VisualElement>("BatterySketch").childCount, "battery sketch drawn once");
        Assert.AreEqual(1, root.Q<VisualElement>("DroneSketch").childCount, "drone sketch drawn once");
        Assert.AreEqual(1, root.Q<VisualElement>("MissionResourceIcon").childCount,
            "mission resource mark drawn once");

        // The controller bar still advertises the four existing operations.
        Assert.AreEqual(4, root.Query<Label>(className: "prep-key-hint").ToList().Count, "four prompt keys");

        // The terminal look is scoped to THIS page: the hub's shared bottom tab
        // strip carries the marker only while Preparation owns the screen.
        VisualElement nav = app.UiRoot.Q<VisualElement>(ElementNames.BottomNav);
        Assert.IsNotNull(nav, "shared bottom nav");
        Assert.IsTrue(nav.ClassListContains("nav-terminal"), "preparation marks the shared nav");
        app.ShowScreen(AppScreen.Overview);
        yield return null;
        Assert.IsFalse(nav.ClassListContains("nav-terminal"), "another screen keeps the original nav");
        app.ShowScreen(AppScreen.Preparation);
        for (int i = 0; i < 2; i++) yield return null;

        // Park the pointer on a surface with no hover styling, so the review
        // frame shows the resting page and not an accidental mouse hover.
        yield return UiToolkitPointerSimulator.Hover(root.Q<VisualElement>("MissionSummary"));
        for (int i = 0; i < 3; i++) yield return null;

        Directory.CreateDirectory(Output);
        yield return Capture(app.Document.panelSettings,
            Path.Combine(Output, "preparation_terminal_default.png"));
    }

    [UnityTest]
    public IEnumerator Preparation_EquipUnequip_PayloadReadiness_AndCapture()
    {
        yield return LoadPreparation();
        AppController app = _app;

        VisualElement root = app.UiRoot.Q<VisualElement>(ElementNames.PreparationScreen);
        Button battery = root.Q<Button>(ElementNames.BatteryRowButton);
        IPanel panel = app.Document.rootVisualElement.panel;

        float baseline = BarPercent(root);

        // ---- equip the optional battery through the real pointer path ----
        yield return UiToolkitPointerSimulator.Click(battery);
        for (int i = 0; i < 4; i++) yield return null;

        Assert.AreEqual("70 / 100", root.Q<Label>(ElementNames.CargoValue).text, "payload after equipping");
        Assert.AreEqual("70 / 100", root.Q<Label>(ElementNames.ReadinessCargoValue).text,
            "readiness payload row follows");
        Assert.AreEqual("已选择", RowLabel(root, ElementNames.BatteryRowButton, "equipment-chip"), "chip state");
        Assert.IsTrue(battery.ClassListContains("row-selected"), "slot shows the carried state");
        Assert.AreEqual("就绪", root.Q<Label>(ElementNames.ReadinessPowerValue).text, "energy check becomes ready");
        Assert.IsTrue(ReadinessRow(root, ElementNames.ReadinessPowerValue).ClassListContains("row-active"),
            "energy check lamp lit");
        Assert.IsTrue(root.Q<VisualElement>("ReadinessState").ClassListContains("ready"), "package stays ready");
        Assert.IsTrue(root.Q<Label>(ElementNames.CargoValue).ClassListContains("cargo-pulse"),
            "payload change is acknowledged");
        Assert.Greater(BarPercent(root), baseline + 20f, "payload bar grew with the load");
        Assert.AreSame(battery, panel.focusController.focusedElement as VisualElement,
            "the operated slot keeps the focus");

        Directory.CreateDirectory(Output);
        yield return Capture(app.Document.panelSettings,
            Path.Combine(Output, "preparation_terminal_battery.png"));

        // ---- unequip it again: payload, readiness and the lamp all return ----
        yield return UiToolkitPointerSimulator.Click(battery);
        for (int i = 0; i < 4; i++) yield return null;

        Assert.AreEqual("45 / 100", root.Q<Label>(ElementNames.CargoValue).text, "payload restored");
        Assert.AreEqual("45 / 100", root.Q<Label>(ElementNames.ReadinessCargoValue).text,
            "readiness payload row restored");
        Assert.AreEqual("可选", RowLabel(root, ElementNames.BatteryRowButton, "equipment-chip"), "chip restored");
        Assert.IsFalse(battery.ClassListContains("row-selected"), "carried state cleared");
        Assert.AreEqual("可选", root.Q<Label>(ElementNames.ReadinessPowerValue).text, "energy check optional again");
        Assert.IsFalse(ReadinessRow(root, ElementNames.ReadinessPowerValue).ClassListContains("row-active"),
            "energy check lamp cleared");
        Assert.IsTrue(root.Q<VisualElement>("ReadinessState").ClassListContains("ready"),
            "package is still shippable");
        Assert.AreEqual(baseline, BarPercent(root), 2f, "payload bar returned to its default fill");
        Assert.AreSame(battery, panel.focusController.focusedElement as VisualElement, "focus still stable");
    }

    [UnityTest]
    public IEnumerator Preparation_MissionDetails_ContextAction_AndCapture()
    {
        yield return LoadPreparation();
        AppController app = _app;

        VisualElement root = app.UiRoot.Q<VisualElement>(ElementNames.PreparationScreen);
        VisualElement details = root.Q<VisualElement>("PrepMissionDetails");
        Button detailsButton = root.Q<Button>("PrepMissionDetailsButton");
        Assert.IsNotNull(details, "mission details container");
        Assert.IsFalse(details.ClassListContains("details-open"), "details start collapsed");

        Gamepad pad = InputSystem.AddDevice<Gamepad>();
        InputSystem.QueueStateEvent(pad,
            new GamepadState().WithButton(GamepadButton.West));
        for (int i = 0; i < 5; i++) yield return null;
        Assert.IsTrue(details.ClassListContains("details-open"), "X opens the mission details");
        Assert.IsNotNull(detailsButton, "details button");
        Assert.AreEqual("收起详情 ‹", detailsButton.text, "details button label");

        InputSystem.QueueStateEvent(pad, new GamepadState());
        for (int i = 0; i < 4; i++) yield return null;
        Assert.IsFalse(pad.buttonWest.isPressed, "X release reached the virtual pad");
        yield return new WaitForSecondsRealtime(0.25f);
        for (int i = 0; i < 45; i++) yield return null;
        Assert.LessOrEqual(root.Q<Label>("PrepDetailSurvey").worldBound.yMax,
            details.worldBound.yMax - 2f,
            "the last mission detail stays inside the expanded content");

        Directory.CreateDirectory(Output);
        yield return Capture(app.Document.panelSettings,
            Path.Combine(Output, "preparation_terminal_details.png"));

        InputSystem.QueueStateEvent(pad,
            new GamepadState().WithButton(GamepadButton.West));
        for (int i = 0; i < 10; i++) yield return null;
        Assert.IsTrue(pad.buttonWest.isPressed, "second X press reached the virtual pad");
        Assert.AreSame(pad, Gamepad.current, "virtual pad remains active for X");
        Assert.IsFalse(details.ClassListContains("details-open"), "X closes the mission details");
        InputSystem.RemoveDevice(pad);
    }

    // ---------- helpers ----------

    /// <summary>Hub -> Preparation, waiting for the hub app to finish binding.</summary>
    private IEnumerator LoadPreparation()
    {
        GameSession session = GameSession.Ensure();
        session.ResetSession();
        SceneManager.LoadScene(BootController.HubSceneName);

        AppController app = null;
        for (int i = 0; i < 600 && (app == null || !app.IsReady); i++)
        {
            app = Object.FindAnyObjectByType<AppController>();
            yield return null;
        }

        Assert.IsNotNull(app, "hub app loaded");
        Assert.IsTrue(app.IsReady, "hub app bound");
        app.ShowScreen(AppScreen.Preparation);
        for (int i = 0; i < 3; i++) yield return null;

        Assert.AreEqual(AppScreen.Preparation, app.CurrentScreen, "preparation is the active screen");
        Assert.AreEqual(1, app.VisibleScreenCount, "exactly one screen is drawn");
        _app = app;
    }

    private static string EnvValue(VisualElement root, int index)
    {
        var rows = root.Q("EnvironmentPanel").Query<VisualElement>(className: "env-row").ToList();
        Assert.Greater(rows.Count, index, "telemetry row " + index + " exists");
        Label value = rows[index].Q<Label>(null, "env-value");
        Assert.IsNotNull(value, "telemetry row " + index + " has a value");
        return value.text;
    }

    private static string RowLabel(VisualElement root, string rowName, string className)
    {
        Button row = root.Q<Button>(rowName);
        Assert.IsNotNull(row, "row " + rowName + " exists");
        Label label = row.Q<Label>(null, className);
        Assert.IsNotNull(label, rowName + " has " + className);
        return label.text;
    }

    private static VisualElement ReadinessRow(VisualElement root, string valueName)
    {
        Label value = root.Q<Label>(valueName);
        Assert.IsNotNull(value, "readiness value " + valueName + " exists");
        Assert.IsNotNull(value.parent, "readiness value " + valueName + " has a row");
        return value.parent;
    }

    private static string HeaderText(VisualElement root, string panelName)
    {
        VisualElement panel = root.Q<VisualElement>(panelName);
        Assert.IsNotNull(panel, "panel " + panelName + " exists");
        Label header = panel.Q<Label>(null, "panel-header-text");
        Assert.IsNotNull(header, panelName + " has a header");
        return header.text;
    }

    /// <summary>Filled share of the payload track, in percent.</summary>
    private static float BarPercent(VisualElement root)
    {
        VisualElement fill = root.Q<VisualElement>(ElementNames.CargoBarFill);
        Assert.IsNotNull(fill, "payload bar fill");
        Assert.IsNotNull(fill.parent, "payload bar track");
        float track = fill.parent.resolvedStyle.width;
        Assert.Greater(track, 1f, "payload track measured");
        return fill.resolvedStyle.width / track * 100f;
    }

    /// <summary>1920x1080 runtime frame of the live panel.</summary>
    private static IEnumerator Capture(PanelSettings panel, string path)
    {
        var target = new RenderTexture(ShotWidth, ShotHeight, 0, RenderTextureFormat.ARGB32);
        target.Create();
        RenderTexture oldTarget = panel.targetTexture;
        RenderTexture oldActive = RenderTexture.active;
        panel.targetTexture = target;
        for (int i = 0; i < 10; i++) yield return null;
        RenderTexture.active = target;
        var texture = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, ShotWidth, ShotHeight), 0, 0);
        texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToPNG());
        RenderTexture.active = oldActive;
        panel.targetTexture = oldTarget;
        Object.Destroy(texture);
        target.Release();
        Object.Destroy(target);
        Assert.Greater(new FileInfo(path).Length, 100000, "visible preparation capture");
    }
}