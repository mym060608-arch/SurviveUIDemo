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
/// Scoped verification for the Processing / 资源加工 page only (Phase 09B
/// PASS 5). Nothing else is loaded, asserted or captured here.
///
/// It covers the three things this pass is allowed to touch:
///
///   * STRUCTURE - the page keeps its authoritative read-outs (库存 / 配方 /
///     数量 / 进度 / 产出 / 效率) and mounts the new recipe list, the quantity
///     control and the code-drawn refinery visuals exactly once;
///   * BEHAVIOUR - the real gamepad route drives the page: D-Pad / left stick
///     walks 加工列表 <-> 加工数量, left / right adjusts inside the legal range
///     (never below 1, never above the stock), A confirms the run, B leaves the
///     page, a running batch locks the parameters, and the pump refuses a run
///     the stock cannot pay for;
///   * EVIDENCE - 1920x1080 runtime frames of idle / quantity focus / active /
///     complete / insufficient for human review.
/// </summary>
public class SurvivePhase09BProcessingTests
{
    private const string Output =
        @"D:\dsh_gpt_unity_blender\export\evidence\phase09b\pass5_processing";

    private const int ShotWidth = 1920;
    private const int ShotHeight = 1080;

    /// <summary>Hub app of the current test (set by <see cref="LoadProcessing"/>).</summary>
    private AppController _app;

    /// <summary>Virtual pad driving the real input route (removed in TearDown).</summary>
    private Gamepad _pad;

    [TearDown]
    public void RemoveVirtualPad()
    {
        if (_pad != null && _pad.added)
        {
            InputSystem.RemoveDevice(_pad);
        }

        _pad = null;
    }

    [UnityTest]
    public IEnumerator Processing_Idle_Structure_DefaultFocus_AndCapture()
    {
        yield return LoadProcessing();
        AppController app = _app;
        VisualElement root = ScreenRoot(app);
        ProcessingScreen page = app.ProcessingView;
        Assert.IsNotNull(page, "processing page controller");

        // ---- information architecture (nothing the page carried was dropped) ----
        Assert.IsNotNull(root.Q<VisualElement>(ElementNames.ProcessingRecipeList), "recipe list");
        Assert.IsNotNull(root.Q<Button>(ElementNames.ProcessingRecipeRow), "recipe row");
        Assert.AreEqual("合金", root.Q<Label>(ElementNames.ProcessingRecipeName).text, "recipe name");
        Assert.AreEqual("矿物 → 合金", root.Q<Label>(ElementNames.ProcessingRecipeFlow).text, "recipe flow");
        Assert.IsNotNull(root.Q<VisualElement>(ElementNames.ProcessingQuantityRow), "quantity row");
        Assert.IsNotNull(root.Q<Button>(ElementNames.ProcessingQuantityDown), "quantity -");
        Assert.IsNotNull(root.Q<Button>(ElementNames.ProcessingQuantityUp), "quantity +");
        Assert.IsNotNull(root.Q<VisualElement>(ElementNames.ProcessingCorePanel), "core panel");
        Assert.IsNotNull(root.Q<VisualElement>(ElementNames.ProcessingInventoryPanel), "inventory panel");
        Assert.IsNotNull(root.Q<VisualElement>(ElementNames.ProcessingOutputPanel), "output panel");
        Assert.IsNotNull(root.Q<VisualElement>(ElementNames.ProcessingPromptHost), "prompt host");

        // Retired UI: the fixed 5/10/15 batch buttons no longer exist anywhere.
        Assert.AreEqual(0, root.Query<Button>(className: "batch-button").ToList().Count,
            "the old fixed batch buttons are gone");

        // ---- the pass's own visuals are mounted exactly once ----
        Assert.IsNotNull(page.Core, "refinery core element");
        Assert.AreEqual(1, root.Q("RefineryCoreHost").childCount, "refinery core mounted once");
        Assert.AreEqual(1, root.Q("ProcessingActivityStrip").childCount, "activity strip mounted once");
        Assert.AreEqual(1, root.Q("FlowOreIcon").childCount, "ore mark mounted once");
        Assert.AreEqual(1, root.Q("FlowFuelIcon").childCount, "fuel mark mounted once");
        Assert.AreEqual(1, root.Q("FlowAlloyIcon").childCount, "alloy mark mounted once");
        Assert.AreEqual(1, root.Q("ProcessingRecipeIcon").childCount, "recipe mark mounted once");
        Assert.AreEqual(1, root.Q("InventoryOreIcon").childCount, "inventory mineral mark mounted once");
        Assert.AreEqual(1, root.Q("FlowLineIn").childCount, "inlet flow line mounted once");
        Assert.AreEqual(1, root.Q("FlowLineOut").childCount, "outlet flow line mounted once");

        // ---- idle state: minimum quantity, no run, expected output shown ----
        Assert.AreEqual("01", root.Q<Label>(ElementNames.ProcessingQuantityValue).text, "quantity opens at 1");
        Assert.AreEqual("准备就绪", root.Q<Label>(ElementNames.ProcessingStatusLabel).text, "idle status");
        Assert.AreEqual("—", root.Q<Label>(ElementNames.ProcessingProgressPercent).text, "idle progress");
        Assert.AreEqual("预计产出", root.Q<Label>(ElementNames.ProcessingOutputCaption).text, "preview caption");
        Assert.AreEqual("+1", root.Q<Label>(ElementNames.ProcessingFuelGainValue).text, "fuel preview for 1");
        Assert.AreEqual("+1", root.Q<Label>(ElementNames.ProcessingAlloyGainValue).text, "alloy preview for 1");
        Assert.AreEqual(RefineryCoreElement.CoreState.Idle, page.Core.State, "core idles");
        Assert.IsFalse(page.QuantityFocused, "the list owns the focus first");

        // ---- controller prompt: the keys that really work right now ----
        yield return Press(_pad, GamepadButton.DpadUp);
        Assert.IsFalse(page.QuantityFocused, "the list walk never steals the quantity focus");
        Assert.AreEqual(3, page.PromptBar.VisibleRowCount, "idle prompt rows");
        StringAssert.Contains("选择", page.PromptBar.PromptText, "prompt offers the list walk");
        StringAssert.Contains("确认加工", page.PromptBar.PromptText, "prompt offers the confirm action");
        StringAssert.Contains("返回", page.PromptBar.PromptText, "prompt offers back");

        // ---- keyboard/gamepad focus ring defaults to the recipe list ----
        VisualElement recipeRow = root.Q<Button>(ElementNames.ProcessingRecipeRow);
        Assert.IsTrue(recipeRow.ClassListContains(UiFocusNavigator.DefaultFocusClass),
            "the recipe list owns the default focus ring");

        yield return ParkPointer(root);
        // Re-arm the pad AFTER the pointer is parked: the review frame must show
        // the controller glyphs this pass is verified with.
        yield return Press(_pad, GamepadButton.DpadUp);
        StringAssert.Contains("选择", page.PromptBar.PromptText, "pad glyphs on the review frame");
        Directory.CreateDirectory(Output);
        yield return Capture(app.Document.panelSettings, Path.Combine(Output, "processing_idle.png"));
    }

    [UnityTest]
    public IEnumerator Processing_QuantityFocus_AdjustAndClamp_AndCapture()
    {
        yield return LoadProcessing();
        AppController app = _app;
        VisualElement root = ScreenRoot(app);
        ProcessingScreen page = app.ProcessingView;
        Label quantity = root.Q<Label>(ElementNames.ProcessingQuantityValue);
        Button down = root.Q<Button>(ElementNames.ProcessingQuantityDown);
        Button up = root.Q<Button>(ElementNames.ProcessingQuantityUp);

        // Down enters the quantity control: the ring moves and the prompt
        // switches from 选择 to 数量 (the axis the control answers to).
        yield return Press(_pad, GamepadButton.DpadDown);
        Assert.IsTrue(page.QuantityFocused, "down enters the quantity control");
        Assert.IsTrue(root.Q<VisualElement>(ElementNames.ProcessingQuantityRow)
            .ClassListContains(UiFocusNavigator.DefaultFocusClass), "quantity control shows the focus");
        StringAssert.Contains("数量", page.PromptBar.PromptText, "prompt now names the quantity axis");

        // Left / right step the amount, and the amount follows the stock.
        yield return Press(_pad, GamepadButton.DpadRight);
        Assert.AreEqual("02", quantity.text, "right raises the run size");
        yield return Press(_pad, GamepadButton.DpadRight);
        Assert.AreEqual("03", quantity.text, "right raises it again");
        Assert.AreEqual("+3", root.Q<Label>(ElementNames.ProcessingFuelGainValue).text,
            "the expected output follows the quantity");
        Assert.AreEqual("+3", root.Q<Label>(ElementNames.ProcessingAlloyGainValue).text,
            "the expected alloy follows the quantity");
        Assert.IsTrue(down.enabledSelf, "the minus arrow is live above the minimum");

        yield return Press(_pad, GamepadButton.DpadLeft);
        yield return Press(_pad, GamepadButton.DpadLeft);
        Assert.AreEqual("01", quantity.text, "left lowers the run size");
        // The minimum is a hard floor: one more step cannot produce 0.
        yield return Press(_pad, GamepadButton.DpadLeft);
        Assert.AreEqual("01", quantity.text, "the quantity never goes below 1");
        Assert.IsFalse(down.enabledSelf, "the minus arrow disables at the minimum");

        // The ceiling is min(module maximum, real stock) - never an impossible run.
        for (int i = 0; i < GameSession.ProcessBatchMax + 4; i++)
        {
            yield return Press(_pad, GamepadButton.DpadRight);
        }

        int expectedMax = Mathf.Min(GameSession.ProcessBatchMax, GameSession.Instance.Minerals);
        Assert.AreEqual(expectedMax.ToString("00"), quantity.text,
            "the quantity stops at the stock / module maximum");
        Assert.IsFalse(up.enabledSelf, "the plus arrow disables at the maximum");
        Assert.IsTrue(GameSession.Instance.CanStartBatch(expectedMax),
            "the selected maximum is a run the stock can actually pay for");

        // Back to a readable value for the capture, with the control focused.
        for (int i = 0; i < 3; i++)
        {
            yield return Press(_pad, GamepadButton.DpadLeft);
        }

        Assert.AreEqual("12", quantity.text, "three steps back down");
        yield return ParkPointer(root);
        // Down is a no-op while the quantity control owns the focus, so this only
        // re-arms the pad for the review frame.
        yield return Press(_pad, GamepadButton.DpadDown);
        Directory.CreateDirectory(Output);
        yield return Capture(app.Document.panelSettings,
            Path.Combine(Output, "processing_quantity_focus.png"));
    }

    [UnityTest]
    public IEnumerator Processing_Active_RotatingCore_AndCapture()
    {
        yield return LoadProcessing();
        AppController app = _app;
        VisualElement root = ScreenRoot(app);
        ProcessingScreen page = app.ProcessingView;
        GameSession session = GameSession.Instance;

        // Choose 3 minerals through the real pad, then confirm with A.
        yield return Press(_pad, GamepadButton.DpadDown);
        yield return Press(_pad, GamepadButton.DpadRight);
        yield return Press(_pad, GamepadButton.DpadRight);
        Assert.AreEqual("03", root.Q<Label>(ElementNames.ProcessingQuantityValue).text,
            "three minerals selected");

        yield return Press(_pad, GamepadButton.South);
        Assert.IsTrue(session.IsProcessing, "A started the run through the page");
        Assert.AreEqual(3, session.ActiveBatchMinerals, "the run uses the selected quantity");
        Assert.AreEqual(17, session.Minerals, "the run cost is deducted once");

        // A running batch locks the parameters it would corrupt.
        yield return Press(_pad, GamepadButton.South);
        Assert.AreEqual(3, session.ActiveBatchMinerals, "a second A cannot restart the run");
        Assert.AreEqual(17, session.Minerals, "a second A cannot charge twice");
        Assert.AreEqual(RefineryCoreElement.CoreState.Running, page.Core.State, "the core is live");
        Assert.AreEqual(1, page.PromptBar.VisibleRowCount, "a live run only leaves Back operable");
        Assert.IsFalse(page.PromptBar.PromptText.Contains("确认加工"),
            "the prompt hides the confirm key while it cannot work");

        // The rotor really turns and the rails really travel while running.
        float firstAngle = page.Core.RotorAngle;
        yield return new WaitForSecondsRealtime(0.3f);
        Assert.AreNotEqual(firstAngle, page.Core.RotorAngle, "the refinery core rotates while refining");
        Assert.IsTrue(page.Core.Flash01 <= 0.001f, "no completion flash during the run");

        session.AdvanceProcessing(session.RefineryProcessSeconds * 0.45f);
        for (int i = 0; i < 3; i++) yield return null;
        Assert.AreEqual("加工中", root.Q<Label>(ElementNames.ProcessingStatusLabel).text, "status 加工中");
        Assert.AreEqual("加工中", root.Q<Button>(ElementNames.ProcessingStartButton).text, "confirm shows 加工中");
        Assert.IsFalse(root.Q<Button>(ElementNames.ProcessingStartButton).enabledSelf,
            "the confirm action is disabled while running");
        StringAssert.Contains("矿物 ×3", root.Q<Label>(ElementNames.ProcessingCoreNote).text,
            "the refinery note names the live run");

        yield return ParkPointer(root);
        yield return Press(_pad, GamepadButton.DpadUp);
        Directory.CreateDirectory(Output);
        yield return Capture(app.Document.panelSettings, Path.Combine(Output, "processing_active.png"));
    }

    [UnityTest]
    public IEnumerator Processing_Complete_SettledResult_AndCapture()
    {
        yield return LoadProcessing();
        AppController app = _app;
        VisualElement root = ScreenRoot(app);
        ProcessingScreen page = app.ProcessingView;
        GameSession session = GameSession.Instance;

        Assert.IsTrue(session.TryStartProcess(5), "a 5 mineral run");
        for (int i = 0; i < 3; i++) yield return null;
        Assert.AreEqual("05", root.Q<Label>(ElementNames.ProcessingQuantityValue).text,
            "the control follows the live run");
        yield return Press(_pad, GamepadButton.South);
        Assert.AreEqual(5, session.ActiveBatchMinerals, "A cannot restart the live run");

        session.AdvanceProcessing(session.RefineryProcessSeconds);
        for (int i = 0; i < 3; i++) yield return null;

        Assert.AreEqual(GameSession.RefineryProcessState.Complete, session.ProcessState, "run complete");
        Assert.AreEqual(13, session.Fuel, "fuel settled (8 + 5)");
        Assert.AreEqual(10, session.Alloy, "alloy settled (5 + 5)");
        Assert.AreEqual("加工完成", root.Q<Label>(ElementNames.ProcessingStatusLabel).text, "status 加工完成");
        Assert.AreEqual("100%", root.Q<Label>(ElementNames.ProcessingProgressPercent).text, "progress 100%");
        Assert.AreEqual("本次产出", root.Q<Label>(ElementNames.ProcessingOutputCaption).text, "settled caption");
        Assert.AreEqual("+5", root.Q<Label>(ElementNames.ProcessingFuelGainValue).text, "settled fuel");
        Assert.AreEqual("+5", root.Q<Label>(ElementNames.ProcessingAlloyGainValue).text, "settled alloy");
        Assert.AreEqual("13", root.Q<Label>(ElementNames.ProcessingFuelValue).text, "inventory fuel");
        Assert.AreEqual("10", root.Q<Label>(ElementNames.ProcessingAlloyValue).text, "inventory alloy");
        Assert.AreEqual(RefineryCoreElement.CoreState.Complete, page.Core.State, "the core acknowledges");
        Assert.Greater(page.Core.Flash01, 0f, "the completion flash is armed");
        Assert.IsTrue(root.Q<VisualElement>(ElementNames.ProcessingCorePanel)
            .ClassListContains("stage-complete"), "the settled result is emphasised");
        Assert.IsTrue(root.Q<Button>(ElementNames.ProcessingStartButton).enabledSelf,
            "the page offers the next run");

        yield return ParkPointer(root);
        yield return Press(_pad, GamepadButton.DpadUp);
        Directory.CreateDirectory(Output);
        yield return Capture(app.Document.panelSettings, Path.Combine(Output, "processing_complete.png"));
    }

    [UnityTest]
    public IEnumerator Processing_Insufficient_Minerals_CannotStart_AndCapture()
    {
        yield return LoadProcessing();
        AppController app = _app;
        VisualElement root = ScreenRoot(app);
        GameSession session = GameSession.Instance;

        // Drain the real stock through the authoritative flow: 20 -> 5 -> 0.
        Assert.IsTrue(session.TryStartProcess(15), "drain with a 15 run");
        session.AdvanceProcessing(session.RefineryProcessSeconds);
        Assert.IsTrue(session.TryStartProcess(5), "drain with the remaining 5");
        session.AdvanceProcessing(session.RefineryProcessSeconds);
        Assert.AreEqual(0, session.Minerals, "no minerals left");
        for (int i = 0; i < 3; i++) yield return null;

        Assert.AreEqual("0", root.Q<Label>(ElementNames.ProcessingMineralsValue).text, "storage shows 0");
        Assert.AreEqual("原料不足", root.Q<Button>(ElementNames.ProcessingStartButton).text,
            "the confirm action reports the missing input");
        Assert.IsFalse(root.Q<Button>(ElementNames.ProcessingStartButton).enabledSelf,
            "the confirm action is disabled without stock");
        Assert.IsFalse(root.Q<Button>(ElementNames.ProcessingQuantityUp).enabledSelf,
            "the quantity cannot be raised without stock");
        Assert.IsFalse(root.Q<Button>(ElementNames.ProcessingQuantityDown).enabledSelf,
            "the quantity cannot be lowered below the minimum");
        Assert.IsFalse(PromptOffersConfirm(app), "the prompt hides a key that cannot work");

        // A is a real no-op here: nothing may be started, nothing may be charged.
        yield return Press(_pad, GamepadButton.South);
        Assert.IsFalse(session.IsProcessing, "A cannot start a run without minerals");
        Assert.AreEqual(0, session.Minerals, "a rejected run never charges");
        Assert.AreEqual(GameSession.RefineryProcessState.Complete, session.ProcessState,
            "the previous settled result is untouched");

        yield return ParkPointer(root);
        yield return Press(_pad, GamepadButton.DpadUp);
        Directory.CreateDirectory(Output);
        yield return Capture(app.Document.panelSettings, Path.Combine(Output, "processing_insufficient.png"));
    }

    [UnityTest]
    public IEnumerator Processing_Back_ReturnsToPreviousScreen()
    {
        yield return LoadProcessing();
        AppController app = _app;

        app.ShowScreen(AppScreen.Outpost);
        yield return null;
        app.ShowScreen(AppScreen.Processing);
        for (int i = 0; i < 3; i++) yield return null;
        Assert.AreEqual(AppScreen.Processing, app.CurrentScreen, "processing owns the screen");

        yield return Press(_pad, GamepadButton.East);
        Assert.AreEqual(AppScreen.Outpost, app.CurrentScreen, "B returns to the previous screen");
        Assert.AreEqual(1, app.VisibleScreenCount, "exactly one screen stays visible");
    }

    [UnityTest]
    public IEnumerator Processing_MousePath_StillWorks()
    {
        yield return LoadProcessing();
        AppController app = _app;
        VisualElement root = ScreenRoot(app);
        ProcessingScreen page = app.ProcessingView;
        GameSession session = GameSession.Instance;

        // The pad additions must not take the mouse away: the same controls
        // answer a real pointer click.
        yield return UiToolkitPointerSimulator.Click(root.Q<Button>(ElementNames.ProcessingQuantityUp));
        for (int i = 0; i < 4; i++) yield return null;
        Assert.AreEqual("02", root.Q<Label>(ElementNames.ProcessingQuantityValue).text,
            "a mouse click on the arrow raises the run size");

        yield return UiToolkitPointerSimulator.Click(root.Q<Button>(ElementNames.ProcessingQuantityDown));
        for (int i = 0; i < 4; i++) yield return null;
        Assert.AreEqual("01", root.Q<Label>(ElementNames.ProcessingQuantityValue).text,
            "a mouse click on the arrow lowers it again");

        yield return UiToolkitPointerSimulator.Click(root.Q<Button>(ElementNames.ProcessingRecipeRow));
        for (int i = 0; i < 4; i++) yield return null;
        Assert.IsTrue(page.QuantityFocused, "clicking the recipe row hands the focus to the quantity control");

        yield return UiToolkitPointerSimulator.Click(root.Q<Button>(ElementNames.ProcessingStartButton));
        for (int i = 0; i < 4; i++) yield return null;
        Assert.IsTrue(session.IsProcessing, "a mouse click on 确认加工 starts the run");
        Assert.AreEqual(1, session.ActiveBatchMinerals, "the click uses the selected quantity");
    }

    // ---------- helpers ----------

    private static bool PromptOffersConfirm(AppController app)
    {
        return app.ProcessingView != null
               && app.ProcessingView.PromptBar != null
               && app.ProcessingView.PromptBar.PromptText.Contains("确认加工");
    }

    /// <summary>Hub -> Processing, waiting for the hub app to finish binding, and
    /// installs the virtual pad that drives the real input route.</summary>
    private IEnumerator LoadProcessing()
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

        app.ShowScreen(AppScreen.Processing);
        for (int i = 0; i < 6; i++) yield return null;

        Assert.AreEqual(AppScreen.Processing, app.CurrentScreen, "processing is the active screen");
        Assert.AreEqual(1, app.VisibleScreenCount, "exactly one screen is drawn");
        _app = app;
    }

    private static VisualElement ScreenRoot(AppController app)
    {
        VisualElement root = app.UiRoot.Q<VisualElement>(ElementNames.ProcessingScreen);
        Assert.IsNotNull(root, "processing screen root");
        return root;
    }

    /// <summary>One real pad press: queue the button, let the page tick, release,
    /// and let the release settle so a held direction cannot repeat.</summary>
    private static IEnumerator Press(Gamepad pad, GamepadButton button)
    {
        InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(button));
        for (int i = 0; i < 3; i++) yield return null;

        InputSystem.QueueStateEvent(pad, new GamepadState());
        for (int i = 0; i < 3; i++) yield return null;
    }

    /// <summary>Parks the OS pointer on a surface with no hover styling, so the
    /// review frame shows the resting page and not an accidental mouse hover.</summary>
    private static IEnumerator ParkPointer(VisualElement root)
    {
        Label neutral = root.Q<Label>(null, "process-subtitle");
        if (neutral != null)
        {
            yield return UiToolkitPointerSimulator.Hover(neutral);
        }

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
        Assert.Greater(new FileInfo(path).Length, 100000, "visible processing capture");
    }
}
