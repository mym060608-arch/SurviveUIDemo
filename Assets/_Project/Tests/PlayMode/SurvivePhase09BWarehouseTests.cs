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
/// Scoped verification for Console02's 仓库 (Warehouse) page only
/// (Phase 09B PASS 9). Nothing else is loaded, asserted or captured here.
///
/// It covers the three things this pass is allowed to touch:
///
///   * STRUCTURE / COPY - the page opens through the real airlock-free route
///     (walk to console 02, focus it with the real interaction), shows the clean
///     仓库 title, exactly two category tabs, the 4 x 3 item grid built from the
///     REAL session resources / equipment, the detail panel and a key line that
///     names only Back (this page has no item action, so no Confirm row);
///   * NAVIGATION - LB / RB page 资源 <-> 装备 through the shared
///     PreviousTab / NextTab semantics, LS / D-Pad move the cell focus, empty
///     slots can never take it, the detail follows the focus live, and the pad
///     and the mouse share ONE focus authority;
///   * BACK - B and Escape leave the console view (never a pause, never a quit).
///
/// Evidence: 1920x1080 frames of the resource page, a selected resource, the
/// equipment page and a selected piece of equipment.
/// </summary>
public class SurvivePhase09BWarehouseTests
{
    private const string Output =
        @"D:\dsh_gpt_unity_blender\export\evidence\phase09b\pass9_warehouse";

    private const int MaxWaitFrames = 900;

    private Gamepad _pad;
    private ShipWarehouseScreenHost _page;

    [SetUp]
    public void SetUp()
    {
        Time.timeScale = 1f;
        InputDeviceTracker.ResetToDefaults();
        AppQuit.ResetBookkeepingForAutomation();
        AppQuit.SuppressForAutomation = true;

        GameSession session = GameSession.Instance;
        if (session != null)
        {
            Object.Destroy(session.gameObject);
        }

        _pad = InputSystem.AddDevice<Gamepad>();
    }

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;

        if (_pad != null && _pad.added)
        {
            InputSystem.RemoveDevice(_pad);
        }

        _pad = null;
        AppQuit.ResetBookkeepingForAutomation();
        InputDeviceTracker.ResetToDefaults();
    }

    // ====================================================================
    // 1. Structure, copy and the default resource page
    // ====================================================================

    [UnityTest]
    public IEnumerator Warehouse_Structure_DefaultResources_AndCapture()
    {
        ShipGameController ship = null;
        yield return EnterWarehouse(value => ship = value);
        ShipWarehouseScreenHost page = _page;
        VisualElement root = page.Root;
        GameSession session = GameSession.Instance;

        // ---- title: 仓库 only ----
        Assert.AreEqual("仓库", LabelText(root, ElementNames.WarehouseTitle), "page title");
        Assert.AreEqual(0, root.Query<Label>(className: "wh-meta").ToList().Count,
            "no decorative English sub-label");
        foreach (Label label in root.Query<Label>().ToList())
        {
            StringAssert.DoesNotContain("CARGO", label.text, "no CARGO copy");
            StringAssert.DoesNotContain("STORAGE", label.text, "no STORAGE copy");
        }

        // ---- exactly two tabs, resources active by default ----
        Assert.AreEqual("resources", page.ActiveTab, "resources is the default tab");
        Button resources = root.Q<Button>(ElementNames.WarehouseResourcesTab);
        Button equipment = root.Q<Button>(ElementNames.WarehouseEquipmentTab);
        Assert.IsNotNull(resources, "resources tab");
        Assert.IsNotNull(equipment, "equipment tab");
        Assert.AreEqual(2, root.Query<Button>(className: "wh-tab").ToList().Count,
            "exactly two category tabs (no third page)");
        Assert.IsTrue(resources.ClassListContains("wh-tab-active"),
            "the resources tab is the raised / cyan one");
        Assert.IsFalse(equipment.ClassListContains("wh-tab-active"),
            "the equipment tab is idle");

        // ---- the grid is built from the REAL session resources ----
        Assert.AreEqual(3, page.ItemCount, "three real resources");
        Assert.AreEqual(9, page.EmptySlotCount, "9 inert empty slots fill the 4 x 3 area");
        Assert.AreEqual(12, root.Query<VisualElement>(className: "wh-cell").ToList().Count,
            "the visible grid is 4 x 3");
        Assert.AreEqual("x" + session.Minerals, CellValue(root, "WarehouseMineralsValue"),
            "mineral count format");
        Assert.AreEqual("x" + session.Fuel, CellValue(root, "WarehouseFuelValue"), "fuel count");
        Assert.AreEqual("x" + session.Alloy, CellValue(root, "WarehouseAlloyValue"), "alloy count");

        // ---- empty slots can never take focus or a click ----
        var empties = root.Query<VisualElement>(className: "wh-cell-empty").ToList();
        Assert.AreEqual(9, empties.Count, "empty slot count");
        foreach (VisualElement empty in empties)
        {
            Assert.AreEqual(PickingMode.Ignore, empty.pickingMode,
                "an empty slot is never clickable");
            Assert.IsFalse(page.Focus.ActiveGroup.Contains(empty),
                "an empty slot is never registered for focus");
        }

        // ---- detail panel follows the default focus (矿物) ----
        Assert.AreEqual("矿物", page.DetailNameText, "default detail name");
        Assert.AreEqual("x" + session.Minerals, page.DetailCountText, "default detail count");
        StringAssert.Contains("常见的天然矿物资源", page.DetailDescriptionText,
            "the description comes from the catalog");
        Assert.IsNotNull(root.Q<VisualElement>(ElementNames.WarehouseDetailIconHost)
            .Q<WarehouseItemIcon>(null), "the enlarged icon is code-drawn");

        // ---- the key line names Back only (device aware) ----
        InputDeviceTracker.NotifyGamepad();
        yield return WaitFrames(4);
        Assert.IsNotNull(page.PromptBar, "shared prompt bar mounted");
        Assert.AreEqual("[B] 返回", page.PromptBar.PromptText, "only Back is advertised");
        foreach (string dead in new[] { "确认", "[A]", "[X]", "[Y]" })
        {
            StringAssert.DoesNotContain(dead, page.PromptBar.PromptText,
                "no key without a function is printed");
        }

        // ---- the paging hint lives in the tab row, device aware ----
        Assert.AreEqual("LB", LabelText(root, ElementNames.WarehouseTabPrevHint), "LB hint");
        Assert.AreEqual("RB", LabelText(root, ElementNames.WarehouseTabNextHint), "RB hint");
        InputDeviceTracker.NotifyKeyboardMouse();
        yield return WaitFrames(3);
        Assert.AreEqual("Q", LabelText(root, ElementNames.WarehouseTabPrevHint),
            "keyboard paging glyph");
        Assert.AreEqual("Tab", LabelText(root, ElementNames.WarehouseTabNextHint),
            "keyboard paging glyph");
        InputDeviceTracker.NotifyGamepad();
        yield return WaitFrames(3);

        Directory.CreateDirectory(Output);
        yield return CaptureConsole(page, Path.Combine(Output, "warehouse_resources.png"));
        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 2. LB / RB page the two categories, and each tab keeps its focus
    // ====================================================================

    [UnityTest]
    public IEnumerator Warehouse_TabNavigation_PagesTwoCategories()
    {
        ShipGameController ship = null;
        yield return EnterWarehouse(value => ship = value);
        ShipWarehouseScreenHost page = _page;
        VisualElement root = page.Root;

        // RB -> 装备.
        yield return PressButton(GamepadButton.RightShoulder);
        Assert.AreEqual("equipment", page.ActiveTab, "RB pages to 装备");
        Assert.IsTrue(root.Q<Button>(ElementNames.WarehouseEquipmentTab)
            .ClassListContains("wh-tab-active"), "the equipment tab is now the raised one");
        Assert.IsFalse(root.Q<Button>(ElementNames.WarehouseResourcesTab)
            .ClassListContains("wh-tab-active"), "the resources tab went idle");
        Assert.AreEqual(4, page.ItemCount, "the equipment page lists the real equipment");

        // Every page: exactly two tabs, never a third.
        Assert.AreEqual(2, root.Query<Button>(className: "wh-tab").ToList().Count,
            "still exactly two tabs");
        Assert.AreEqual(2, page.Focus.HasTabs ? 2 : 0, "the navigator knows exactly two groups");

        // LB -> back to 资源, and the page restores the cell we left on.
        yield return PressButton(GamepadButton.LeftShoulder);
        Assert.AreEqual("resources", page.ActiveTab, "LB pages back to 资源");
        Assert.AreEqual("矿物", page.FocusedItem != null ? page.FocusedItem.Name : "<none>",
            "returning to a tab restores its remembered focus");
        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 3. LS / D-Pad move the cell focus and never enter an empty slot
    // ====================================================================

    [UnityTest]
    public IEnumerator Warehouse_GridFocus_MovesAndSkipsEmptySlots()
    {
        ShipGameController ship = null;
        yield return EnterWarehouse(value => ship = value);
        ShipWarehouseScreenHost page = _page;

        Assert.AreEqual(0, page.FocusedIndex, "the first real cell starts focused");

        yield return PushStick(new Vector2(1f, 0f), 0.25f);
        Assert.AreEqual(1, page.FocusedIndex, "right moves to the next cell");

        yield return PushStick(new Vector2(0f, -1f), 0.25f);
        Assert.AreEqual(1, page.FocusedIndex,
            "down finds no cell below row 1 and keeps the focus on a valid item");

        yield return PushStick(new Vector2(-1f, 0f), 0.25f);
        Assert.AreEqual(0, page.FocusedIndex, "left moves back");

        // Try every direction several times: the focus must never land on an
        // empty slot, and it must never disappear.
        foreach (Vector2 direction in new[]
                 {
                     new Vector2(1f, 0f), new Vector2(0f, -1f), new Vector2(-1f, 0f),
                     new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(1f, 0f)
                 })
        {
            yield return PushStick(direction, 0.25f);
            Assert.IsTrue(page.FocusedIndex >= 0 && page.FocusedIndex < page.ItemCount,
                "the focus stays on a real item after " + direction);
            VisualElement focused = page.Focus.Focused;
            Assert.IsNotNull(focused, "something is focused");
            Assert.IsFalse(focused.ClassListContains(ShipWarehouseScreenHost.EmptyCellClass),
                "an empty slot never takes focus");
        }

        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 4. The detail follows the focus, and the mouse shares the authority
    // ====================================================================

    [UnityTest]
    public IEnumerator Warehouse_DetailFollowsFocus_MouseAndPadAgree_AndCapture()
    {
        ShipGameController ship = null;
        yield return EnterWarehouse(value => ship = value);
        ShipWarehouseScreenHost page = _page;
        VisualElement root = page.Root;
        GameSession session = GameSession.Instance;

        // Pad: focus the fuel cell -> the detail panel follows.
        yield return PushStick(new Vector2(1f, 0f), 0.25f);
        Assert.AreEqual("燃料", page.DetailNameText, "detail follows the pad focus");
        Assert.AreEqual("x" + session.Fuel, page.DetailCountText, "and its real count");
        StringAssert.Contains("主要能源", page.DetailDescriptionText, "fuel description");

        Directory.CreateDirectory(Output);
        yield return CaptureConsole(page, Path.Combine(Output, "warehouse_resource_selected.png"));

        // Mouse: hovering a cell moves the SAME focus (one authority, no second
        // selection state). The keyboard/mouse device must be the last one used.
        InputDeviceTracker.NotifyKeyboardMouse();
        yield return WaitFrames(3);
        VisualElement alloyCell = root.Q<VisualElement>("WarehouseCell_alloy");
        Assert.IsNotNull(alloyCell, "alloy cell");
        UiToolkitPointerSimulator.Hover(alloyCell);
        yield return WaitFrames(4);
        Assert.AreEqual("alloy", page.FocusedItem != null ? page.FocusedItem.Id : "<none>",
            "the mouse took the SAME focus");
        Assert.AreEqual("合金", page.DetailNameText, "the detail follows the mouse too");
        Assert.IsTrue(alloyCell.ClassListContains(ShipWarehouseScreenHost.FocusClass),
            "the shared focus ring moved to the hovered cell");

        // A real click selects through the very same path.
        VisualElement fuelCell = root.Q<VisualElement>("WarehouseCell_fuel");
        Assert.IsTrue(UiToolkitPointerSimulator.Click(fuelCell), "click the fuel cell");
        yield return WaitFrames(4);
        Assert.AreEqual("fuel", page.FocusedItem != null ? page.FocusedItem.Id : "<none>",
            "the click selected the cell");
        Assert.AreEqual("燃料", page.DetailNameText, "the detail agrees with the click");

        // The pad takes over again without any stale second state.
        InputDeviceTracker.NotifyGamepad();
        yield return PushStick(new Vector2(-1f, 0f), 0.25f);
        Assert.AreEqual("mineral", page.FocusedItem != null ? page.FocusedItem.Id : "<none>",
            "the pad moved the same focus back");
        Assert.AreEqual("矿物", page.DetailNameText, "one authority, one detail state");
        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 5. The equipment page shows the real equipment
    // ====================================================================

    [UnityTest]
    public IEnumerator Warehouse_EquipmentPage_AndCapture()
    {
        ShipGameController ship = null;
        yield return EnterWarehouse(value => ship = value);
        ShipWarehouseScreenHost page = _page;
        VisualElement root = page.Root;
        GameSession session = GameSession.Instance;

        yield return PressButton(GamepadButton.RightShoulder);
        Assert.AreEqual("equipment", page.ActiveTab, "装备 page");

        // The real items, in the real state.
        Assert.IsNotNull(root.Q<VisualElement>("WarehouseCell_harvester"), "harvester cell");
        Assert.IsNotNull(root.Q<VisualElement>("WarehouseCell_battery"), "battery cell");
        Assert.IsNotNull(root.Q<VisualElement>("WarehouseCell_drone"), "drone cell");
        Assert.IsNotNull(root.Q<VisualElement>("WarehouseCell_cargo"), "cargo cell");
        Assert.AreEqual("必需", LabelText(root, ShipWarehouseScreenHost.HarvesterStateName),
            "the harvester is required");
        Assert.AreEqual(session.BatterySelected ? "已装备" : "未选择",
            LabelText(root, ShipWarehouseScreenHost.BatteryStateName), "battery state");
        Assert.AreEqual(session.DroneSelected ? "已装备" : "未选择",
            LabelText(root, ShipWarehouseScreenHost.DroneStateName), "drone state");
        Assert.AreEqual(session.CargoWeight + " / " + GameSession.CargoCapacityKg + " KG",
            LabelText(root, ShipWarehouseScreenHost.CargoValueName), "the real payload mass");

        // Focus a piece of equipment: the detail panel shows its state.
        Assert.AreEqual("采集器 MK-I", page.DetailNameText, "default equipment detail");
        StringAssert.Contains("采集", page.DetailDescriptionText, "harvester description");

        Directory.CreateDirectory(Output);
        yield return CaptureConsole(page, Path.Combine(Output, "warehouse_equipment.png"));

        UiToolkitPointerSimulator.Click(root.Q<VisualElement>("WarehouseCell_battery"));
        yield return WaitFrames(4);
        Assert.AreEqual("备用电池组", page.DetailNameText, "battery detail follows the selection");
        Assert.AreEqual("未选择", page.DetailCountText == string.Empty
            ? LabelText(root, ShipWarehouseScreenHost.BatteryStateName)
            : page.DetailCountText, "the state is shown in the detail");

        yield return CaptureConsole(page, Path.Combine(Output, "warehouse_equipment_selected.png"));
        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 6. A session refresh never drops the focus
    // ====================================================================

    [UnityTest]
    public IEnumerator Warehouse_SessionRefresh_KeepsFocusValid()
    {
        ShipGameController ship = null;
        yield return EnterWarehouse(value => ship = value);
        ShipWarehouseScreenHost page = _page;

        yield return PushStick(new Vector2(1f, 0f), 0.25f);
        Assert.AreEqual("fuel", page.FocusedItem != null ? page.FocusedItem.Id : "<none>",
            "fuel focused");

        GameSession session = GameSession.Instance;
        session.AddMinerals(10);
        yield return WaitFrames(4);

        Assert.AreEqual("x" + session.Minerals,
            CellValue(page.Root, ShipWarehouseScreenHost.MineralsValueName),
            "the cell shows the new stock");
        Assert.AreEqual("fuel", page.FocusedItem != null ? page.FocusedItem.Id : "<none>",
            "the refresh keeps the same item focused");
        Assert.IsTrue(page.FocusedIndex >= 0 && page.FocusedIndex < page.ItemCount,
            "and the focus is still inside the grid");
        Assert.AreEqual("燃料", page.DetailNameText, "the detail is still correct");
        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 7. B / Escape leave the console view (never a pause)
    // ====================================================================

    [UnityTest]
    public IEnumerator Warehouse_BackAndEscape_LeaveTheConsole()
    {
        ShipGameController ship = null;
        yield return EnterWarehouse(value => ship = value);
        ShipWarehouseScreenHost page = _page;

        Assert.AreEqual(UiInput.UiLayer.ConsoleFocus, UiInput.ActiveBackLayer,
            "the focused warehouse terminal owns Back");

        UiInput.InjectEscapeForAutomation();
        yield return WaitFrames(3);
        yield return WaitUntil(() => !ship.Focus.FocusActive);
        Assert.IsFalse(ship.Focus.FocusActive, "Escape leaves the warehouse view");
        Assert.IsFalse(ship.PauseMenu.IsOpen, "Escape on the terminal never pauses");
        Assert.AreEqual(1f, Time.timeScale, 0.0001f, "and never freezes time");
        Assert.AreEqual(0, AppQuit.RequestCount, "and never quits");

        // B does exactly the same from the pad.
        yield return FocusConsole(ship);
        yield return PressButton(GamepadButton.East);
        yield return WaitUntil(() => !ship.Focus.FocusActive);
        Assert.IsFalse(ship.Focus.FocusActive, "B leaves the warehouse view");
        Assert.IsFalse(ship.PauseMenu.IsOpen, "B never opens the pause");
        Assert.AreEqual(0, AppQuit.RequestCount, "B never quits");
        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // helpers
    // ====================================================================

    private IEnumerator EnterWarehouse(System.Action<ShipGameController> onShip)
    {
        GameSession.Ensure().ResetSession();
        SceneRouting.RequestShipEntry(SceneRouting.ShipEntry.Default);
        SceneManager.LoadScene(GameSession.ShipInteriorSceneName);
        yield return WaitUntil(() => ActiveSceneName() == GameSession.ShipInteriorSceneName);

        ShipGameController ship = null;
        yield return WaitUntil(() =>
        {
            ship = Object.FindAnyObjectByType<ShipGameController>();
            return ship != null && ship.IsReady && ship.Player != null;
        });

        Assert.IsNotNull(ship, "ship controller ready");
        onShip?.Invoke(ship);

        yield return FocusConsole(ship);
        _page = WarehouseOf(ship);
        Assert.IsNotNull(_page, "the warehouse page is bound");
        yield return WaitUntil(() => _page.IsBound);
        yield return WaitFrames(4);
    }

    /// <summary>Walks to console 02 and focuses it with the real interaction route.</summary>
    private IEnumerator FocusConsole(ShipGameController ship)
    {
        WorldConsoleSurface surface = SurfaceOf(ship);
        Assert.IsNotNull(surface, "console 02 (仓库) exists");
        yield return WaitUntil(() => surface.IsReady);

        Vector3 position = surface.transform.position;
        Vector3 forward = surface.transform.forward;
        ship.Player.TeleportTo(position + forward * 1.7f);
        ship.Player.SetFacing(surface.transform.eulerAngles.y + 180f, 0f);
        yield return WaitFrames(3);
        Physics.SyncTransforms();

        ShipConsoleInteractable console = surface.GetComponent<ShipConsoleInteractable>();
        Assert.IsNotNull(console, "the warehouse console interactable exists");

        bool entered = false;
        for (int f = 0; f < 300 && !entered; f++)
        {
            ship.Interactor.RefreshTarget();
            if (ReferenceEquals(ship.Interactor.CurrentTarget, console)
                && ship.Interactor.HasValidTarget)
            {
                entered = ship.Interactor.TryInteract();
            }

            if (!entered)
            {
                yield return null;
            }
        }

        Assert.IsTrue(entered, "E entered the warehouse terminal");
        yield return WaitUntil(() => ship.Focus.FocusActive);
        yield return WaitFrames(6);
    }

    private static WorldConsoleSurface SurfaceOf(ShipGameController ship)
    {
        foreach (WorldConsoleSurface surface in
                 Object.FindObjectsByType<WorldConsoleSurface>())
        {
            if (surface.WarehouseHost != null)
            {
                return surface;
            }
        }

        return null;
    }

    private static ShipWarehouseScreenHost WarehouseOf(ShipGameController ship)
    {
        WorldConsoleSurface surface = SurfaceOf(ship);
        return surface != null ? surface.WarehouseHost : null;
    }

    private static string ActiveSceneName()
    {
        Scene active = SceneManager.GetActiveScene();
        return active.IsValid() ? active.name : "<none>";
    }

    private static string LabelText(VisualElement root, string name)
    {
        Label label = root.Q<Label>(name);
        Assert.IsNotNull(label, "missing label " + name);
        return label.text;
    }

    private static string CellValue(VisualElement root, string name)
    {
        return LabelText(root, name);
    }

    private static IEnumerator WaitFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            yield return null;
        }
    }

    private static IEnumerator WaitUntil(System.Func<bool> condition,
        int maxFrames = MaxWaitFrames)
    {
        int frames = 0;
        while (!condition() && frames < maxFrames)
        {
            frames++;
            yield return null;
        }
    }

    private IEnumerator PressButton(GamepadButton button)
    {
        InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(button));
        yield return WaitFrames(3);
        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return WaitFrames(3);
    }

    /// <summary>Holds the LEFT stick (navigation) for a real interval.</summary>
    private IEnumerator PushStick(Vector2 direction, float seconds)
    {
        InputSystem.QueueStateEvent(_pad, new GamepadState { leftStick = direction });
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return WaitFrames(3);
    }

    /// <summary>The console's own 1920x1080 RenderTexture - exactly what the
    /// physical screen shows.</summary>
    private static IEnumerator CaptureConsole(ShipWarehouseScreenHost page, string path)
    {
        WorldConsoleSurface surface = page.GetComponentInParent<WorldConsoleSurface>();
        Assert.IsNotNull(surface, "the page lives on a console surface");
        RenderTexture source = surface.CurrentRenderTexture;
        Assert.IsNotNull(source, "the console exposes its RenderTexture");

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = source;
        var texture = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        texture.Apply();
        RenderTexture.active = previous;

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.Destroy(texture);

        Assert.Greater(new FileInfo(path).Length, 20000, "visible warehouse capture: " + path);
        yield return null;
    }
}
