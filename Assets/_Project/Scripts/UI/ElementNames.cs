/// <summary>
/// Single source of truth for MainApp.uxml element names.
///
/// Runtime binding, the structural validator and Play Mode smoke tests all
/// reference these constants so name drift fails loudly instead of silently.
/// </summary>
public static class ElementNames
{
    // Screen containers (children of AppBody).
    public const string OverviewScreen = "OverviewScreen";
    public const string PreparationScreen = "PreparationScreen";
    public const string ProcessingScreen = "ProcessingScreen";
    public const string OutpostScreen = "OutpostScreen";

    // Bottom navigation.
    public const string BottomNav = "BottomNav";
    public const string NavOverview = "NavOverview";
    public const string NavPreparation = "NavPreparation";
    public const string NavProcessing = "NavProcessing";
    public const string NavOutpost = "NavOutpost";

    // Debug panel.
    public const string DebugPanel = "DebugPanel";
    public const string DebugToggleButton = "DebugToggleButton";
    public const string DebugContent = "DebugContent";
    public const string DebugAddMineralsButton = "DebugAddMineralsButton";
    public const string DebugAddAlloyButton = "DebugAddAlloyButton";
    public const string DebugResetSessionButton = "DebugResetSessionButton";

    // Overview.
    public const string EnergyValue = "EnergyValue";
    public const string FoodValue = "FoodValue";
    public const string MineralsValue = "MineralsValue";
    public const string FuelValue = "FuelValue";
    public const string AlloyValue = "AlloyValue";
    public const string DestinationValue = "DestinationValue";
    public const string OpenPreparationButton = "OpenPreparationButton";

    // Overview (Phase 02 visual composition).
    public const string OverviewTopBar = "OverviewTopBar";
    public const string ShipStatusPanel = "ShipStatusPanel";
    public const string ShipSurvivalGroup = "ShipSurvivalGroup";
    public const string ShipResourceGroup = "ShipResourceGroup";
    public const string EnergyBarFill = "EnergyBarFill";
    public const string FoodBarFill = "FoodBarFill";
    public const string DestinationPanel = "DestinationPanel";
    public const string PlanetStage = "PlanetStage";
    public const string PlanetDisc = "PlanetDisc";
    public const string ObjectivePanel = "ObjectivePanel";
    public const string ObjectiveCopy = "ObjectiveCopy";
    public const string MissionStatusBlock = "MissionStatusBlock";

    // Overview (Phase 09B PASS 7: page title, planet name consistency,
    // exploration read-out and the shared controller key line).
    public const string OverviewTitle = "OverviewTitle";
    public const string ObjectiveSurveyText = "ObjectiveSurveyText";
    public const string ExplorationValue = "ExplorationValue";
    public const string ExplorationBarFill = "ExplorationBarFill";
    public const string OverviewPromptHost = "OverviewPromptHost";

    // Landing Confirmation / 最终确认 (Phase 09B PASS 8): the two code-drawn cyan
    // dot fields, the key glyph fused into each action card and the hold bar.
    public const string LandingDotsLeft = "LandingDotsLeft";
    public const string LandingDotsRight = "LandingDotsRight";
    public const string LandingAdjustKey = "DeployAdjustKey";
    public const string LandingConfirmKey = "DeployConfirmKey";
    public const string LandingCancelKey = "DeployCancelKey";
    public const string LandingHoldFill = "DeployHoldFill";

    // Preparation.
    public const string HarvesterRowButton = "HarvesterRowButton";
    public const string BatteryRowButton = "BatteryRowButton";
    public const string DroneRowButton = "DroneRowButton";
    public const string CargoValue = "CargoValue";
    public const string LoadoutStateLabel = "LoadoutStateLabel";
    public const string ConfirmLoadoutButton = "ConfirmLoadoutButton";

    // Preparation (Phase 03 composition / runtime binding).
    public const string PreparationDestination = "PreparationDestination";
    public const string MissionSummary = "MissionSummary";
    public const string EnvironmentPanel = "EnvironmentPanel";
    public const string EquipmentPanel = "EquipmentPanel";
    public const string CargoPanel = "CargoPanel";
    public const string CargoBarFill = "CargoBarFill";
    public const string ReadinessPanel = "ReadinessPanel";
    public const string ReadinessPowerValue = "ReadinessPowerValue";
    public const string ReadinessSurveyValue = "ReadinessSurveyValue";
    public const string ReadinessCargoValue = "ReadinessCargoValue";

    // Preparation controller hints (Phase 09B PASS 5 follow-up): the page's own
    // bottom-right key line, rewritten for the device in hand.
    public const string PrepHintDetails = "PrepHintDetails";
    public const string PrepHintNavigate = "PrepHintNavigate";
    public const string PrepHintBack = "PrepHintBack";
    public const string PrepHintConfirm = "PrepHintConfirm";

    // Processing (Phase 04 composition; restructured in Phase 09B PASS 5).
    public const string ProcessingMineralsValue = "ProcessingMineralsValue";
    public const string ProcessingFuelValue = "ProcessingFuelValue";
    public const string ProcessingAlloyValue = "ProcessingAlloyValue";
    public const string ProcessingRecipePanel = "ProcessingRecipePanel";
    public const string ProcessingRecipeList = "ProcessingRecipeList";
    public const string ProcessingRecipeRow = "ProcessingRecipeRow";
    public const string ProcessingRecipeName = "ProcessingRecipeName";
    public const string ProcessingRecipeFlow = "ProcessingRecipeFlow";
    public const string ProcessingQuantityRow = "ProcessingQuantityRow";
    public const string ProcessingQuantityValue = "ProcessingQuantityValue";
    public const string ProcessingQuantityDown = "ProcessingQuantityDown";
    public const string ProcessingQuantityUp = "ProcessingQuantityUp";
    public const string ProcessingQuantityHint = "ProcessingQuantityHint";
    public const string ProcessingCorePanel = "ProcessingCorePanel";
    public const string ProcessingOutputPanel = "ProcessingOutputPanel";
    public const string ProcessingOutputCaption = "ProcessingOutputCaption";
    public const string ProcessingFuelGainValue = "ProcessingFuelGainValue";
    public const string ProcessingAlloyGainValue = "ProcessingAlloyGainValue";
    public const string ProcessingCoreNote = "ProcessingCoreNote";
    public const string ProcessingProgressFill = "ProcessingProgressFill";
    public const string ProcessingProgressPercent = "ProcessingProgressPercent";
    public const string ProcessingStatusLabel = "ProcessingStatusLabel";
    public const string ProcessingInventoryPanel = "ProcessingInventoryPanel";
    public const string ProcessingRefineryLevel = "ProcessingRefineryLevel";
    public const string ProcessingDurationValue = "ProcessingDurationValue";
    public const string ProcessingStartButton = "ProcessingStartButton";
    public const string ProcessingPromptHost = "ProcessingPromptHost";

    // Outpost (Phase 05 composition / runtime binding).
    public const string HarvesterLevelValue = "HarvesterLevelValue";
    public const string RefineryLevelValue = "RefineryLevelValue";
    public const string OutpostLevelValue = "OutpostLevelValue";
    public const string OutpostAlloyValue = "OutpostAlloyValue";
    public const string OutpostStatusPanel = "OutpostStatusPanel";
    public const string OutpostTopology = "OutpostTopology";
    public const string OutpostDetailPanel = "OutpostDetailPanel";
    public const string CoreModuleNode = "CoreModuleNode";
    public const string HarvesterModuleNode = "HarvesterModuleNode";
    public const string RefineryModuleNode = "RefineryModuleNode";
    public const string CoreNodeLevel = "CoreNodeLevel";
    public const string HarvesterNodeLevel = "HarvesterNodeLevel";
    public const string RefineryNodeLevel = "RefineryNodeLevel";
    public const string CoreNodeStatusValue = "CoreNodeStatusValue";
    public const string HarvesterNodeStatusValue = "HarvesterNodeStatusValue";
    public const string RefineryNodeStatusValue = "RefineryNodeStatusValue";
    public const string OutpostCapValue = "OutpostCapValue";
    public const string ModuleDetailName = "ModuleDetailName";
    public const string ModuleDetailLevels = "ModuleDetailLevels";
    public const string ModuleDetailEffectTitle = "ModuleDetailEffectTitle";
    public const string ModuleCurrentEffectValue = "ModuleCurrentEffectValue";
    public const string ModuleNextEffectValue = "ModuleNextEffectValue";
    public const string ModuleUpgradeCostValue = "ModuleUpgradeCostValue";
    public const string ModuleUpgradeRequirement = "ModuleUpgradeRequirement";
    public const string OutpostUpgradeButton = "OutpostUpgradeButton";
    public const string OutpostPromptHost = "OutpostPromptHost";

    /// <summary>Every element the runtime binds; the validator checks each exists in MainApp.uxml.</summary>
    public static readonly string[] All = new[]
    {
        OverviewScreen,
        PreparationScreen,
        ProcessingScreen,
        OutpostScreen,
        BottomNav,
        NavOverview,
        NavPreparation,
        NavProcessing,
        NavOutpost,
        DebugPanel,
        DebugToggleButton,
        DebugContent,
        DebugAddMineralsButton,
        DebugAddAlloyButton,
        DebugResetSessionButton,
        EnergyValue,
        FoodValue,
        MineralsValue,
        FuelValue,
        AlloyValue,
        DestinationValue,
        OpenPreparationButton,
        EnergyBarFill,
        FoodBarFill,
        PlanetDisc,
        OverviewTitle,
        ObjectiveSurveyText,
        ExplorationValue,
        ExplorationBarFill,
        OverviewPromptHost,
        HarvesterRowButton,
        BatteryRowButton,
        DroneRowButton,
        CargoValue,
        LoadoutStateLabel,
        ConfirmLoadoutButton,
        CargoBarFill,
        ReadinessPowerValue,
        ReadinessSurveyValue,
        ReadinessCargoValue,
        PrepHintDetails,
        PrepHintNavigate,
        PrepHintBack,
        PrepHintConfirm,
        ProcessingMineralsValue,
        ProcessingFuelValue,
        ProcessingAlloyValue,
        ProcessingRecipeList,
        ProcessingRecipeRow,
        ProcessingRecipeName,
        ProcessingRecipeFlow,
        ProcessingQuantityRow,
        ProcessingQuantityValue,
        ProcessingQuantityDown,
        ProcessingQuantityUp,
        ProcessingQuantityHint,
        ProcessingOutputCaption,
        ProcessingFuelGainValue,
        ProcessingAlloyGainValue,
        ProcessingProgressFill,
        ProcessingProgressPercent,
        ProcessingStatusLabel,
        ProcessingCoreNote,
        ProcessingRefineryLevel,
        ProcessingDurationValue,
        ProcessingStartButton,
        ProcessingPromptHost,
        HarvesterLevelValue,
        RefineryLevelValue,
        OutpostLevelValue,
        OutpostAlloyValue,
        CoreModuleNode,
        HarvesterModuleNode,
        RefineryModuleNode,
        CoreNodeLevel,
        HarvesterNodeLevel,
        RefineryNodeLevel,
        CoreNodeStatusValue,
        HarvesterNodeStatusValue,
        RefineryNodeStatusValue,
        OutpostCapValue,
        ModuleDetailName,
        ModuleDetailLevels,
        ModuleDetailEffectTitle,
        ModuleCurrentEffectValue,
        ModuleNextEffectValue,
        ModuleUpgradeCostValue,
        ModuleUpgradeRequirement,
        OutpostUpgradeButton
    };

    /// <summary>
    /// Phase 02 Overview visual composition elements that the structural
    /// validator must find in MainApp.uxml (stable names, no pixel checks).
    /// </summary>
    public static readonly string[] OverviewStructure =
    {
        OverviewScreen,
        OverviewTopBar,
        OverviewTitle,
        ShipStatusPanel,
        ShipSurvivalGroup,
        ShipResourceGroup,
        DestinationPanel,
        PlanetStage,
        PlanetDisc,
        ExplorationValue,
        ExplorationBarFill,
        OverviewPromptHost,
        ObjectivePanel,
        ObjectiveCopy,
        ObjectiveSurveyText,
        MissionStatusBlock,
        OpenPreparationButton,
        BottomNav,
        NavOverview,
        NavPreparation,
        NavProcessing,
        NavOutpost
    };

    /// <summary>
    /// Phase 03 Preparation composition elements the structural validator must
    /// find in MainApp.uxml (stable names, no pixel checks).
    /// </summary>
    public static readonly string[] PreparationStructure =
    {
        PreparationScreen,
        MissionSummary,
        PreparationDestination,
        EnvironmentPanel,
        EquipmentPanel,
        HarvesterRowButton,
        BatteryRowButton,
        DroneRowButton,
        CargoPanel,
        CargoValue,
        CargoBarFill,
        ReadinessPanel,
        LoadoutStateLabel,
        ReadinessCargoValue,
        ConfirmLoadoutButton,
        BottomNav,
        NavOverview,
        NavPreparation,
        NavProcessing,
        NavOutpost
    };

    /// <summary>
    /// Processing composition elements the structural validator must find in
    /// MainApp.uxml (stable names, no pixel checks). Phase 09B PASS 5 replaced
    /// the fixed 5/10/15 batch buttons with the recipe list + quantity control.
    /// </summary>
    public static readonly string[] ProcessingStructure =
    {
        ProcessingScreen,
        ProcessingRecipePanel,
        ProcessingRecipeList,
        ProcessingRecipeRow,
        ProcessingRecipeName,
        ProcessingRecipeFlow,
        ProcessingQuantityRow,
        ProcessingQuantityValue,
        ProcessingQuantityDown,
        ProcessingQuantityUp,
        ProcessingQuantityHint,
        ProcessingMineralsValue,
        ProcessingCorePanel,
        ProcessingOutputPanel,
        ProcessingOutputCaption,
        ProcessingFuelGainValue,
        ProcessingAlloyGainValue,
        ProcessingProgressFill,
        ProcessingProgressPercent,
        ProcessingStatusLabel,
        ProcessingCoreNote,
        ProcessingInventoryPanel,
        ProcessingFuelValue,
        ProcessingAlloyValue,
        ProcessingRefineryLevel,
        ProcessingDurationValue,
        ProcessingStartButton,
        ProcessingPromptHost,
        BottomNav,
        NavOverview,
        NavPreparation,
        NavProcessing,
        NavOutpost
    };

    /// <summary>
    /// Phase 05 Outpost composition elements the structural validator must
    /// find in MainApp.uxml (stable names, no pixel checks).
    /// </summary>
    public static readonly string[] OutpostStructure =
    {
        OutpostScreen,
        OutpostStatusPanel,
        OutpostAlloyValue,
        OutpostTopology,
        CoreModuleNode,
        HarvesterModuleNode,
        RefineryModuleNode,
        OutpostCapValue,
        OutpostDetailPanel,
        ModuleDetailName,
        ModuleDetailLevels,
        ModuleCurrentEffectValue,
        ModuleNextEffectValue,
        ModuleUpgradeCostValue,
        ModuleUpgradeRequirement,
        OutpostUpgradeButton,
        OutpostPromptHost,
        BottomNav,
        NavOverview,
        NavPreparation,
        NavProcessing,
        NavOutpost
    };

    /// <summary>
    /// Landing Confirmation / 最终确认 composition the structural validator must
    /// find in ShipDeployConfirm.uxml (Phase 09B PASS 8): the two code-drawn dot
    /// fields, the key glyph fused into each action card, and the ONLY hold bar.
    /// </summary>
    public static readonly string[] LandingStructure =
    {
        "DeployOverlayRoot",
        LandingDotsLeft,
        LandingDotsRight,
        "DeployPanel",
        "DeployDestination",
        "DeployHarvesterState",
        "DeployBatteryState",
        "DeployDroneState",
        "DeployCargoValue",
        "DeployAdjustGearButton",
        "DeployConfirmButton",
        "DeployCancelButton",
        LandingAdjustKey,
        LandingConfirmKey,
        LandingCancelKey,
        LandingHoldFill
    };
}
