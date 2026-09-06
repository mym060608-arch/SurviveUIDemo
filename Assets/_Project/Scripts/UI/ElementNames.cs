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

    // Preparation.
    public const string HarvesterRowButton = "HarvesterRowButton";
    public const string BatteryRowButton = "BatteryRowButton";
    public const string DroneRowButton = "DroneRowButton";
    public const string CargoValue = "CargoValue";
    public const string LoadoutStateLabel = "LoadoutStateLabel";
    public const string ConfirmLoadoutButton = "ConfirmLoadoutButton";

    // Processing.
    public const string ProcessingMineralsValue = "ProcessingMineralsValue";
    public const string ProcessingFuelValue = "ProcessingFuelValue";
    public const string ProcessingAlloyValue = "ProcessingAlloyValue";
    public const string ProcessButton = "ProcessButton";

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
        HarvesterRowButton,
        BatteryRowButton,
        DroneRowButton,
        CargoValue,
        LoadoutStateLabel,
        ConfirmLoadoutButton,
        ProcessingMineralsValue,
        ProcessingFuelValue,
        ProcessingAlloyValue,
        ProcessButton,
        HarvesterLevelValue,
        RefineryLevelValue,
        OutpostLevelValue,
        OutpostAlloyValue,
        UpgradeRefineryButton
    };
}
