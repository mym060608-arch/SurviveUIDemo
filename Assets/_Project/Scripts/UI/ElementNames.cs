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

    // Processing (Phase 04 composition / runtime binding).
    public const string ProcessingMineralsValue = "ProcessingMineralsValue";
    public const string ProcessingFuelValue = "ProcessingFuelValue";
    public const string ProcessingAlloyValue = "ProcessingAlloyValue";
    public const string ProcessingStoragePanel = "ProcessingStoragePanel";
    public const string Batch5Button = "Batch5Button";
    public const string Batch10Button = "Batch10Button";
    public const string Batch15Button = "Batch15Button";
    public const string ProcessingCorePanel = "ProcessingCorePanel";
    public const string ProcessingOutputPanel = "ProcessingOutputPanel";
    public const string ProcessingOutputCaption = "ProcessingOutputCaption";
    public const string ProcessingFuelGainValue = "ProcessingFuelGainValue";
    public const string ProcessingAlloyGainValue = "ProcessingAlloyGainValue";
    public const string ProcessingProgressFill = "ProcessingProgressFill";
    public const string ProcessingProgressPercent = "ProcessingProgressPercent";
    public const string ProcessingStatusLabel = "ProcessingStatusLabel";
    public const string ProcessingInventoryPanel = "ProcessingInventoryPanel";
    public const string ProcessingRefineryLevel = "ProcessingRefineryLevel";
    public const string ProcessingEfficiencyValue = "ProcessingEfficiencyValue";
    public const string ProcessingStartButton = "ProcessingStartButton";

    // Outpost.
    public const string HarvesterLevelValue = "HarvesterLevelValue";
    public const string RefineryLevelValue = "RefineryLevelValue";
    public const string OutpostLevelValue = "OutpostLevelValue";
    public const string OutpostAlloyValue = "OutpostAlloyValue";
    public const string UpgradeRefineryButton = "UpgradeRefineryButton";

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
        ProcessingMineralsValue,
        ProcessingFuelValue,
        ProcessingAlloyValue,
        Batch5Button,
        Batch10Button,
        Batch15Button,
        ProcessingOutputCaption,
        ProcessingFuelGainValue,
        ProcessingAlloyGainValue,
        ProcessingProgressFill,
        ProcessingProgressPercent,
        ProcessingStatusLabel,
        ProcessingRefineryLevel,
        ProcessingStartButton,
        HarvesterLevelValue,
        RefineryLevelValue,
        OutpostLevelValue,
        OutpostAlloyValue,
        UpgradeRefineryButton
    };

    /// <summary>
    /// Phase 02 Overview visual composition elements that the structural
    /// validator must find in MainApp.uxml (stable names, no pixel checks).
    /// </summary>
    public static readonly string[] OverviewStructure =
    {
        OverviewScreen,
        OverviewTopBar,
        ShipStatusPanel,
        ShipSurvivalGroup,
        ShipResourceGroup,
        DestinationPanel,
        PlanetStage,
        PlanetDisc,
        ObjectivePanel,
        ObjectiveCopy,
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
    /// Phase 04 Processing composition elements the structural validator must
    /// find in MainApp.uxml (stable names, no pixel checks).
    /// </summary>
    public static readonly string[] ProcessingStructure =
    {
        ProcessingScreen,
        ProcessingStoragePanel,
        ProcessingMineralsValue,
        Batch5Button,
        Batch10Button,
        Batch15Button,
        ProcessingCorePanel,
        ProcessingOutputPanel,
        ProcessingFuelGainValue,
        ProcessingAlloyGainValue,
        ProcessingProgressFill,
        ProcessingStatusLabel,
        ProcessingInventoryPanel,
        ProcessingFuelValue,
        ProcessingAlloyValue,
        ProcessingRefineryLevel,
        ProcessingStartButton,
        BottomNav,
        NavOverview,
        NavPreparation,
        NavProcessing,
        NavOutpost
    };
}
