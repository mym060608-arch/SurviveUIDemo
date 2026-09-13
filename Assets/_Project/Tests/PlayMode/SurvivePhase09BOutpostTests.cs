using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Scoped verification for the Outpost / 据点 page only (Phase 09B PASS 6).
/// Nothing else is loaded, asserted or captured here.
///
/// It covers the three things this pass is allowed to touch:
///
///   * STRUCTURE - the page keeps its authoritative read-outs (据点状态 / 合金 /
///     建设信息 / 模块拓扑 / 模块详情) and mounts the three code-drawn module
///     marks exactly once;
///   * BEHAVIOUR - the user's controller map on the real input route: the right
///     stick moves the ONE shared pointer (<see cref="UiPointerState"/>), X
///     selects the module under it, A upgrades through the authoritative
///     session, B leaves the page, and a blocked upgrade changes nothing;
///   * EVIDENCE - 1920x1080 runtime frames of idle / cursor / selected /
///     upgraded / blocked for human review.
/// </summary>
public class SurvivePhase09BOutpostTests
{
    private const string Output =
        @"D:\dsh_gpt_unity_blender\export\evidence\phase09b\pass6_outpost";

    private const int ShotWidth = 1920;
    private const int ShotHeight = 1080;

    private AppController _app;
    private Gamepad _pad;

    [TearDown]
    public void RemoveVirtualPad()
    {
        if (_pad != null && _pad.added)
        {
            InputSystem.RemoveDevice(_pad);
        }

        _pad = null;
        UiPointerState.ResetForAutomation();
    }

    [UnityTest]
    public IEnumerator Outpost_Structure_Idle_AndCapture()
    {
        yield return LoadOutpost();
        AppController app = _app;
        VisualElement root = ScreenRoot(app);
        OutpostScreen page = app.OutpostView;

        // ---- information architecture ----
        Assert.IsNotNull(root.Q<VisualElement>(ElementNames.OutpostStatusPanel), "status panel");
        Assert.IsNotNull(root.Q<VisualElement>(ElementNames.OutpostTopology), "topology");
        Assert.IsNotNull(root.Q<VisualElement>(ElementNames.OutpostDetailPanel), "detail panel");
        Assert.IsNotNull(root.Q<VisualElement>(ElementNames.OutpostPromptHost), "prompt host");

        // The top row is only the page name: no system strip, no English.
        Assert.AreEqual(0, root.Query<VisualElement>(className: "outpost-topbar").ToList().Count,
            "no system brand strip on the page");
        Assert.AreEqual("据点", root.Q<Label>(null, "outpost-title").text, "page title");
        Assert.AreEqual(0, root.Query<Label>(className: "outpost-subtitle").ToList().Count,
            "no english subtitle");
        Assert.AreEqual(0, root.Query<VisualElement>(className: "outpost-head-data").ToList().Count,
            "no english header data");

        // Left column: status + alloy + construction info.
        Assert.AreEqual("据点状态", HeaderText(root, ElementNames.OutpostStatusPanel), "status header");
        Assert.AreEqual("资源", GroupLabel(root, 0), "resource group label");
        Assert.AreEqual("建设信息", GroupLabel(root, 1), "construction group label");
        Assert.AreEqual("5", LabelText(root, ElementNames.OutpostAlloyValue), "alloy stock");
        Assert.AreEqual("LV.2", LabelText(root, ElementNames.OutpostCapValue), "module cap");

        // Three module cards, each with its own code-drawn mark.
        Button core = root.Q<Button>(ElementNames.CoreModuleNode);
        Button harvester = root.Q<Button>(ElementNames.HarvesterModuleNode);
        Button refinery = root.Q<Button>(ElementNames.RefineryModuleNode);
        Assert.IsNotNull(core, "core card");
        Assert.IsNotNull(harvester, "harvester card");
        Assert.IsNotNull(refinery, "refinery card");
        Assert.AreEqual("核心模块", NodeTitle(core), "core title");
        Assert.AreEqual("采集模块", NodeTitle(harvester), "harvester title");
        Assert.AreEqual("精炼模块", NodeTitle(refinery), "refinery title");
        Assert.AreEqual(1, root.Q("CoreNodeIcon").childCount, "core mark mounted once");
        Assert.AreEqual(1, root.Q("HarvesterNodeIcon").childCount, "harvester mark mounted once");
        Assert.AreEqual(1, root.Q("RefineryNodeIcon").childCount, "refinery mark mounted once");

        // Right column: detail + before/after + cost + condition + action.
        Assert.AreEqual("精炼模块", LabelText(root, ElementNames.ModuleDetailName), "default selection");
        Assert.AreEqual("LV.1 ▶ LV.2", LabelText(root, ElementNames.ModuleDetailLevels), "level step");
        Assert.AreEqual("2.0s", LabelText(root, ElementNames.ModuleCurrentEffectValue), "current effect");
        Assert.AreEqual("1.6s", LabelText(root, ElementNames.ModuleNextEffectValue), "next effect");
        Assert.AreEqual("3 合金", LabelText(root, ElementNames.ModuleUpgradeCostValue), "cost");
        Button upgrade = root.Q<Button>(ElementNames.OutpostUpgradeButton);
        Assert.AreEqual("升级", upgrade.text, "the action reads 升级 only");
        Assert.IsTrue(upgrade.enabledSelf, "action enabled at default");

        // The prompt names the whole controller map, no dead key.
        Assert.IsNotNull(page.PromptBar, "shared prompt bar");
        StringAssert.Contains("光标", page.PromptBar.PromptText, "prompt offers the cursor");
        StringAssert.Contains("选择", page.PromptBar.PromptText, "prompt offers select");
        StringAssert.Contains("升级", page.PromptBar.PromptText, "prompt offers upgrade");
        StringAssert.Contains("返回", page.PromptBar.PromptText, "prompt offers back");

        yield return ParkPointerAway(root, page);
        Directory.CreateDirectory(Output);
        yield return Capture(app.Document.panelSettings, Path.Combine(Output, "outpost_idle.png"));
    }

    [UnityTest]
    public IEnumerator Outpost_RightStick_MovesSharedCursor_AndCapture()
    {
        yield return LoadOutpost();
        AppController app = _app;
        VisualElement root = ScreenRoot(app);
        OutpostScreen page = app.OutpostView;
        Button core = root.Q<Button>(ElementNames.CoreModuleNode);

        // The pad takes the ONE shared pointer over (never a second cursor).
        Vector2 start = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        UiPointerState.PlaceForAutomation(start, UiPointerState.Source.Mouse);
        yield return PushStick(_pad, new Vector2(1f, 0f), 0.35f);
        Assert.IsTrue(UiPointerState.GamepadOwns, "the right stick owns the shared pointer");
        Assert.Greater(UiPointerState.Position.x, start.x + 40f, "the cursor moved right");

        Vector2 after = UiPointerState.Position;
        yield return PushStick(_pad, new Vector2(0f, 1f), 0.35f);
        Assert.Greater(UiPointerState.Position.y, after.y + 40f, "the cursor moved up");

        // Park the pointer straight on the core card: the page resolves it.
        UiPointerState.PlaceForAutomation(ScreenPointOf(root, core), UiPointerState.Source.Gamepad);
        for (int i = 0; i < 4; i++) yield return null;
        Assert.AreEqual(GameSession.OutpostModule.Core, page.CursorModule,
            "the card under the cursor is resolved by the panel's own hit test");
        Assert.IsTrue(core.ClassListContains(OutpostScreen.CursorOverClass),
            "the card under the cursor shows its frame");

        // The page no longer fights for the OS cursor: it hides it and draws the
        // project's dot / dot+ring inside the game UI, so the stock arrow can
        // never appear - not for a frame, not permanently.
        Assert.IsTrue(page.CursorOwned, "the page owns the cursor while it is open");
        Assert.IsNotNull(page.CursorOverlay, "the page draws its own cursor");
        Assert.IsTrue(page.CursorOverlay.IsHovering,
            "the drawn cursor shows its ring over an operable card");
        Assert.AreSame(UiCursorOverlay.InvisibleTexture,
            app.UiRoot.Q<VisualElement>(ElementNames.OutpostStatusPanel).style.cursor.value.texture,
            "page surfaces carry the transparent system cursor, so the stock arrow can never show");
        Assert.AreSame(UiCursorOverlay.InvisibleTexture,
            app.UiRoot.style.cursor.value.texture,
            "the document root carries the transparent system cursor too");
        Assert.AreEqual(PickingMode.Ignore,
            app.UiRoot.Q<Button>(ElementNames.CoreModuleNode).pickingMode,
            "the page is unpickable while it owns the pointer (no hover -> no cursor application)");
        Assert.AreEqual(PickingMode.Ignore,
            app.UiRoot.Q<Button>(ElementNames.NavOverview).pickingMode,
            "the hub tabs are suspended too, so no hover can restore the stock cursor");
        float overlayLeft = page.CursorOverlay.style.left.value.value;
        float overlayTop = page.CursorOverlay.style.top.value.value;
        Assert.AreNotEqual(0f, overlayLeft + overlayTop,
            "the drawn cursor is placed on the shared pointer position");

        Directory.CreateDirectory(Output);
        yield return Capture(app.Document.panelSettings, Path.Combine(Output, "outpost_cursor.png"));
    }

    [UnityTest]
    public IEnumerator Outpost_XSelectsModule_AndCapture()
    {
        yield return LoadOutpost();
        AppController app = _app;
        VisualElement root = ScreenRoot(app);
        OutpostScreen page = app.OutpostView;
        Button harvester = root.Q<Button>(ElementNames.HarvesterModuleNode);

        // Aim the shared cursor at the harvester card, then press X.
        UiPointerState.PlaceForAutomation(ScreenPointOf(root, harvester),
            UiPointerState.Source.Gamepad);
        for (int i = 0; i < 4; i++) yield return null;
        Assert.AreEqual(GameSession.OutpostModule.Harvester, page.CursorModule, "cursor on 采集模块");

        yield return Press(_pad, GamepadButton.West);
        Assert.AreEqual(GameSession.OutpostModule.Harvester, page.SelectedModule,
            "X selected the module under the cursor");
        Assert.IsTrue(harvester.ClassListContains("node-selected"), "harvester card is selected");
        Assert.AreEqual("采集模块", LabelText(root, ElementNames.ModuleDetailName), "detail follows X");
        Assert.AreEqual("采集效率", LabelText(root, ElementNames.ModuleDetailEffectTitle), "effect title");
        Assert.AreEqual("100%", LabelText(root, ElementNames.ModuleCurrentEffectValue), "current 100%");
        Assert.AreEqual("125%", LabelText(root, ElementNames.ModuleNextEffectValue), "next 125%");
        Assert.AreEqual("3 合金", LabelText(root, ElementNames.ModuleUpgradeCostValue), "cost 3 alloy");

        Directory.CreateDirectory(Output);
        yield return Capture(app.Document.panelSettings, Path.Combine(Output, "outpost_selected.png"));
    }

    [UnityTest]
    public IEnumerator Outpost_AUpgradesSelected_AndCapture()
    {
        yield return LoadOutpost();
        AppController app = _app;
        VisualElement root = ScreenRoot(app);
        GameSession session = GameSession.Instance;

        // X on the harvester card, then A upgrades it through the session.
        UiPointerState.PlaceForAutomation(
            ScreenPointOf(root, root.Q<Button>(ElementNames.HarvesterModuleNode)),
            UiPointerState.Source.Gamepad);
        for (int i = 0; i < 4; i++) yield return null;
        yield return Press(_pad, GamepadButton.West);
        Assert.AreEqual(GameSession.OutpostModule.Harvester, app.OutpostView.SelectedModule,
            "harvester selected");

        yield return Press(_pad, GamepadButton.South);
        Assert.AreEqual(2, session.HarvesterLevel, "A upgraded the presented module");
        Assert.AreEqual(2, session.Alloy, "the authoritative cost was charged once (5 - 3)");
        Assert.AreEqual("2", LabelText(root, ElementNames.OutpostAlloyValue), "alloy read-out follows");
        Assert.AreEqual("LV.2", LabelText(root, ElementNames.HarvesterLevelValue), "status follows");
        Assert.AreEqual("125%", LabelText(root, ElementNames.ModuleCurrentEffectValue),
            "the new tier is live");
        Assert.AreEqual("150%", LabelText(root, ElementNames.ModuleNextEffectValue), "and its next tier");
        Assert.AreEqual(125, session.HarvesterEfficiencyPercent, "authoritative efficiency 125%");

        Directory.CreateDirectory(Output);
        yield return Capture(app.Document.panelSettings, Path.Combine(Output, "outpost_upgraded.png"));
    }

    [UnityTest]
    public IEnumerator Outpost_BlockedUpgrade_ChangesNothing_AndCapture()
    {
        yield return LoadOutpost();
        AppController app = _app;
        VisualElement root = ScreenRoot(app);
        OutpostScreen page = app.OutpostView;
        GameSession session = GameSession.Instance;

        // Spend the alloy: the harvester costs 3, leaving 2 < the refinery's 3.
        Assert.AreEqual(GameSession.UpgradeFailureReason.None,
            session.TryUpgrade(GameSession.OutpostModule.Harvester), "harvester LV.2");
        for (int i = 0; i < 3; i++) yield return null;

        // The refinery is now unaffordable: select it with the pad.
        UiPointerState.PlaceForAutomation(
            ScreenPointOf(root, root.Q<Button>(ElementNames.RefineryModuleNode)),
            UiPointerState.Source.Gamepad);
        for (int i = 0; i < 4; i++) yield return null;
        yield return Press(_pad, GamepadButton.West);
        Assert.AreEqual(GameSession.OutpostModule.Refinery, page.SelectedModule, "refinery selected");

        Button upgrade = root.Q<Button>(ElementNames.OutpostUpgradeButton);
        Assert.AreEqual("升级", upgrade.text, "the action keeps its single label");
        Assert.IsFalse(upgrade.enabledSelf, "the action is disabled while blocked");
        Assert.AreEqual("合金不足", LabelText(root, ElementNames.ModuleUpgradeRequirement),
            "the 条件 row carries the blocking reason");
        Assert.IsFalse(page.PromptBar.PromptText.Contains("升级"),
            "the prompt hides a key that cannot work");

        // A is a real no-op: nothing may be spent, no level may move.
        yield return Press(_pad, GamepadButton.South);
        Assert.AreEqual(1, session.RefineryLevel, "A never upgrades a blocked module");
        Assert.AreEqual(2, session.Alloy, "A never charges for a blocked upgrade");
        Assert.AreEqual(2, session.HarvesterLevel, "the harvester keeps its LV.2");

        Directory.CreateDirectory(Output);
        yield return Capture(app.Document.panelSettings, Path.Combine(Output, "outpost_blocked.png"));
    }

    [UnityTest]
    public IEnumerator Outpost_Back_ReturnsToPreviousScreen()
    {
        yield return LoadOutpost();
        AppController app = _app;

        app.ShowScreen(AppScreen.Overview);
        yield return null;
        app.ShowScreen(AppScreen.Outpost);
        for (int i = 0; i < 3; i++) yield return null;
        Assert.AreEqual(AppScreen.Outpost, app.CurrentScreen, "outpost owns the screen");

        yield return Press(_pad, GamepadButton.East);
        Assert.AreEqual(AppScreen.Overview, app.CurrentScreen, "B returns to the previous screen");
        Assert.AreEqual(1, app.VisibleScreenCount, "exactly one screen stays visible");
        Assert.IsFalse(UiPointerState.HasBounds, "leaving the page released the cursor region");
        Assert.AreEqual(PickingMode.Position,
            app.UiRoot.Q<Button>(ElementNames.CoreModuleNode).pickingMode,
            "leaving the page restores normal picking");
        Assert.AreEqual(PickingMode.Position,
            app.UiRoot.Q<Button>(ElementNames.NavOverview).pickingMode,
            "the hub tabs are pickable again once the page is left");
    }

    // ---------- helpers ----------

    private IEnumerator LoadOutpost()
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

        _pad = InputSystem.AddDevice<Gamepad>();
        for (int i = 0; i < 3; i++) yield return null;

        app.ShowScreen(AppScreen.Outpost);
        for (int i = 0; i < 6; i++) yield return null;

        Assert.AreEqual(AppScreen.Outpost, app.CurrentScreen, "outpost is the active screen");
        Assert.AreEqual(1, app.VisibleScreenCount, "exactly one screen is drawn");
        _app = app;
    }

    private static VisualElement ScreenRoot(AppController app)
    {
        VisualElement root = app.UiRoot.Q<VisualElement>(ElementNames.OutpostScreen);
        Assert.IsNotNull(root, "outpost screen root");
        return root;
    }

    private static string LabelText(VisualElement root, string name)
    {
        Label label = root.Q<Label>(name);
        Assert.IsNotNull(label, "missing label element " + name);
        return label.text;
    }

    private static string HeaderText(VisualElement root, string panelName)
    {
        VisualElement panel = root.Q<VisualElement>(panelName);
        Assert.IsNotNull(panel, "panel " + panelName);
        Label header = panel.Q<Label>(null, "panel-header-text");
        Assert.IsNotNull(header, panelName + " has a header");
        return header.text;
    }

    /// <summary>The nth "group-label" of the page (alloy / construction groups).</summary>
    private static string GroupLabel(VisualElement root, int index)
    {
        var groups = root.Query<Label>(className: "group-label").ToList();
        Assert.Greater(groups.Count, index, "group label " + index + " exists");
        return groups[index].text;
    }

    private static string NodeTitle(Button node)
    {
        Label title = node.Q<Label>(null, "node-title");
        Assert.IsNotNull(title, "node title");
        return title.text;
    }

    /// <summary>Screen-pixel centre (bottom-left origin) of a page element.</summary>
    private static Vector2 ScreenPointOf(VisualElement root, VisualElement element)
    {
        Rect panelRoot = root.panel.visualTree.worldBound;
        float scaleX = Screen.width / Mathf.Max(1f, panelRoot.width);
        float scaleY = Screen.height / Mathf.Max(1f, panelRoot.height);
        Vector2 centre = element.worldBound.center;
        return new Vector2(centre.x * scaleX, Screen.height - centre.y * scaleY);
    }

    /// <summary>Parks the shared pointer on a surface with no hover styling so
    /// the review frame shows the resting page (both the pointer state and the
    /// real device position are parked, so a live mouse cannot contradict it).</summary>
    private static IEnumerator ParkPointerAway(VisualElement root, OutpostScreen page)
    {
        VisualElement neutral = root.Q<Label>(null, "outpost-title");
        Assert.IsNotNull(neutral, "neutral parking surface");
        yield return UiToolkitPointerSimulator.Hover(neutral);
        UiPointerState.PlaceForAutomation(ScreenPointOf(root, neutral),
            UiPointerState.Source.Mouse);
        for (int i = 0; i < 4; i++) yield return null;
        Assert.IsNull(page.CursorModule, "the parked pointer is over no module card");
    }

    /// <summary>One real pad press: queue the button, let the page tick, release.</summary>
    private static IEnumerator Press(Gamepad pad, GamepadButton button)
    {
        InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(button));
        for (int i = 0; i < 3; i++) yield return null;

        InputSystem.QueueStateEvent(pad, new GamepadState());
        for (int i = 0; i < 3; i++) yield return null;
    }

    /// <summary>Holds the RIGHT stick for a real interval: the shared pointer
    /// integrates by time, so the movement is measured in seconds, not frames.</summary>
    private static IEnumerator PushStick(Gamepad pad, Vector2 direction, float seconds)
    {
        InputSystem.QueueStateEvent(pad, new GamepadState { rightStick = direction });
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        InputSystem.QueueStateEvent(pad, new GamepadState());
        for (int i = 0; i < 3; i++) yield return null;
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
        Assert.Greater(new FileInfo(path).Length, 100000, "visible outpost capture");
    }
}
