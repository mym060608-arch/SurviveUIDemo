using System;
using UnityEngine;

/// <summary>
/// Authoritative shared runtime state for the Survive UI demo.
///
/// Exactly one instance exists for the whole application lifetime:
/// - created from 00_Boot (Bootstrap) or lazily by the Hub AppController,
/// - marked DontDestroyOnLoad,
/// - duplicate instances destroy themselves (singleton protection).
///
/// UI never owns these values. Screens read the session and refresh through
/// OnStateChanged. All resource mutation goes through this class.
/// </summary>
public class GameSession : MonoBehaviour
{
    // Default session state (Phase 01 contract).
    public const int DefaultEnergy = 74;
    public const int DefaultFood = 51;
    public const int DefaultMinerals = 20;
    public const int DefaultFuel = 8;
    public const int DefaultAlloy = 5;
    public const int DefaultCurrentPlanetIndex = 0;
    public const int DefaultHarvesterLevel = 1;
    public const int DefaultRefineryLevel = 1;
    public const int DefaultOutpostLevel = 1;

    // Refinery processing batches (Phase 04). One batch base = 5 minerals:
    // +2 Fuel and +1 Alloy per base batch. See ProcessingScreen for the flow.
    public const int ProcessBatchBase = 5;
    public const int ProcessFuelReward = 2;
    public const int ProcessAlloyReward = 1;

    // Outpost module progression (Phase 05). Demo tier range LV.1..LV.3.
    public const int MaxModuleLevel = 3;

    // Personal survival status (Phase 06 Revision round 2). Health 0-100
    // where 100 is the healthy default. Satiety 0 = extremely hungry,
    // 100 = fully satiated; the default is a normal high value (82). No
    // decay / eating / damage simulation yet.
    public const int DefaultPlayerHealth = 100;
    public const int DefaultPlayerSatiety = 82;

    // NEREID-04 surface harvesting (Phase 06). Three fixed resource nodes,
    // each settles exactly +5 Minerals once per Reset.
    public const int SurfaceNodeCount = 3;
    public const int SurfaceNodeYield = 5;
    public const float SurfaceBaseHarvestSeconds = 4.0f;
    public const float SurfaceWirelessTransferSeconds = 0.75f;
    public const string SurfaceSceneName = "02_NereidSurface";

    // Walkable ship interior (Phase 07). ShipInterior authors no GameSession;
    // this persistent object stays authoritative across scene transitions.
    public const string ShipInteriorSceneName = "03_ShipInterior";

    /// <summary>Selectable outpost modules (three authoritative levels).</summary>
    public enum OutpostModule
    {
        Core,
        Harvester,
        Refinery
    }

    /// <summary>Clean upgrade outcome semantics (never gameplay via strings).</summary>
    public enum UpgradeFailureReason
    {
        None,
        MaxLevel,
        CoreLevelRequired,
        InsufficientAlloy
    }

    /// <summary>Refinery process lifecycle. Complete keeps the last result
    /// visible until the next start or reset.</summary>
    public enum RefineryProcessState
    {
        Idle,
        Running,
        Complete
    }

    // Expedition loadout (Phase 03). HARVESTER MK-I is mission-required and
    // always carried; BATTERY PACK and SURVEY DRONE are optional.
    public const int HarvesterWeightKg = 45;
    public const int BatteryWeightKg = 25;
    public const int DroneWeightKg = 20;
    public const int CargoCapacityKg = 100;

    public static GameSession Instance { get; private set; }

    /// <summary>Fired after every authoritative mutation (resources, levels, reset).</summary>
    public event Action OnStateChanged;

    public int Energy { get; private set; }
    public int Food { get; private set; }
    public int Minerals { get; private set; }
    public int Fuel { get; private set; }
    public int Alloy { get; private set; }

    public int CurrentPlanetIndex { get; private set; }

    public int HarvesterLevel { get; private set; }
    public int RefineryLevel { get; private set; }
    public int OutpostLevel { get; private set; }

    /// <summary>Personal health, 0-100, 100 = healthy (Revision foundation).</summary>
    public int PlayerHealth { get; private set; }

    /// <summary>Personal satiety, 0 = extremely hungry .. 100 = fully
    /// satiated. Default is a normal high value (82). Higher is always better;
    /// there is no ambiguity between "full" and "starving".</summary>
    public int PlayerSatiety { get; private set; }

    // ---- Outpost module effects (Phase 05) ----
    // Derived authoritative values consumed by the UI and future gameplay.

    /// <summary>Highest tier allowed for subsystems: Core LV.1 -> LV.2 cap,
    /// Core LV.2+ -> LV.3 cap.</summary>
    public int SubsystemLevelCap => Mathf.Min(MaxModuleLevel, OutpostLevel + 1);

    /// <summary>Authoritative harvester multiplier (LV.1=1.0, LV.2=1.25,
    /// LV.3=1.5). Consumed by Phase 06 harvesting gameplay, not faked here.</summary>
    public float HarvesterEfficiencyMultiplier => 1f + (HarvesterLevel - 1) * 0.25f;

    /// <summary>Harvester multiplier as integer percent (100/125/150).</summary>
    public int HarvesterEfficiencyPercent => (int)(HarvesterEfficiencyMultiplier * 100f);

    /// <summary>Processing duration derived from RefineryLevel (LV.1=2.0s,
    /// LV.2=1.6s, LV.3=1.2s). Affects speed only, never the output ratio.</summary>
    public float RefineryProcessSeconds => Mathf.Max(0.5f, 2.0f - (RefineryLevel - 1) * 0.4f);

    /// <summary>Surface harvest duration driven by the real Phase 05
    /// harvester multiplier: LV.1 = 4.0s, LV.2 = 3.2s, LV.3 ~2.67s.</summary>
    public float SurfaceHarvestSeconds =>
        SurfaceBaseHarvestSeconds / HarvesterEfficiencyMultiplier;

    // ---- NEREID-04 surface state (Phase 06) ----
    // Authoritative per-node completion. Three nodes => 3-bit mask is enough.
    // UI and scene objects only call the APIs below; no duplicate protection
    // is scattered into the scene.

    private int _harvestedNodeMask;

    /// <summary>
    /// Phase 07B wrist memory: the last NEREID surface navigation target the
    /// player actually selected (0..2), or -1 when none was chosen this
    /// session. Read-only presentation memory so the ship wrist can say
    /// 地表目标已记录 without inventing a live surface distance.
    /// </summary>
    public int RecordedSurfaceTargetNodeId { get; private set; } = -1;

    /// <summary>Records a surface navigation selection (authority: the
    /// surface controller). Rejected when out of range.</summary>
    public void RecordSurfaceTarget(int nodeId)
    {
        if (nodeId < 0 || nodeId >= SurfaceNodeCount)
        {
            return;
        }

        RecordedSurfaceTargetNodeId = nodeId;
    }

    /// <summary>Number of surface nodes completed this session.</summary>
    public int HarvestedNodeCount
    {
        get
        {
            int count = 0;
            for (int id = 0; id < SurfaceNodeCount; id++)
            {
                if ((_harvestedNodeMask & (1 << id)) != 0)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>True when node <paramref name="id"/> already delivered its
    /// yield this session (persists across surface re-entry until Reset).</summary>
    public bool IsSurfaceNodeCompleted(int id)
    {
        if (id < 0 || id >= SurfaceNodeCount)
        {
            return false;
        }

        return (_harvestedNodeMask & (1 << id)) != 0;
    }

    /// <summary>
    /// Atomically completes a surface node: rejects already-completed or
    /// out-of-range ids, marks the bit and settles +yield Minerals once.
    /// Returns true only when the node was newly completed.
    /// </summary>
    public bool TryCompleteSurfaceNode(int id, int mineralYield)
    {
        if (id < 0 || id >= SurfaceNodeCount || IsSurfaceNodeCompleted(id))
        {
            return false;
        }

        if (mineralYield <= 0)
        {
            return false;
        }

        _harvestedNodeMask |= 1 << id;
        Minerals += mineralYield;
        NotifyChanged();
        return true;
    }

    /// <summary>Expedition may launch only after the loadout is confirmed.</summary>
    public bool CanStartExpedition => LoadoutConfirmed && IsLoadoutValid;

    // ---- Expedition loadout (Phase 03) ----
    // Single authoritative preparation state. The UI never owns these values;
    // CargoWeight is always derived from equipment state, never stored.

    /// <summary>HARVESTER MK-I is mission-required and always carried.</summary>
    public bool HarvesterSelected => true;

    public bool BatterySelected { get; private set; }
    public bool DroneSelected { get; private set; }
    public bool LoadoutConfirmed { get; private set; }

    /// <summary>Derived cargo mass from the current equipment state.</summary>
    public int CargoWeight
    {
        get
        {
            int total = HarvesterSelected ? HarvesterWeightKg : 0;
            if (BatterySelected)
            {
                total += BatteryWeightKg;
            }

            if (DroneSelected)
            {
                total += DroneWeightKg;
            }

            return total;
        }
    }

    /// <summary>A valid expedition package fits the cargo capacity with the
    /// required equipment carried (always true while the Harvester is
    /// required; kept as an explicit rule so capacity stays enforced).</summary>
    public bool IsLoadoutValid => HarvesterSelected && CargoWeight <= CargoCapacityKg;

    /// <summary>True when adding <paramref name="weight"/> keeps cargo within
    /// capacity. Shared by UI enablement and selection mutation.</summary>
    public bool CanCarry(int weight)
    {
        return CargoWeight + weight <= CargoCapacityKg;
    }

    /// <summary>Selects/deselects the BATTERY PACK. A selection that would
    /// exceed capacity is rejected cleanly. Changing equipment clears any
    /// previous confirmation (the confirmed loadout no longer matches).</summary>
    public bool TrySetBatterySelected(bool selected)
    {
        return TrySetOptionalEquipment(selected, BatteryWeightKg, () => BatterySelected,
            value => BatterySelected = value);
    }

    /// <summary>Selects/deselects the SURVEY DRONE (capacity guarded, clears
    /// confirmation on change, same semantics as the battery).</summary>
    public bool TrySetDroneSelected(bool selected)
    {
        return TrySetOptionalEquipment(selected, DroneWeightKg, () => DroneSelected,
            value => DroneSelected = value);
    }

    private bool TrySetOptionalEquipment(bool selected, int weight, System.Func<bool> get,
        System.Action<bool> set)
    {
        if (selected && !CanCarry(weight))
        {
            // Reject the impossible selection; state stays untouched.
            return false;
        }

        if (get() == selected)
        {
            // No-op toggle; nothing changed, nothing to invalidate.
            return true;
        }

        set(selected);
        LoadoutConfirmed = false;
        NotifyChanged();
        return true;
    }

    /// <summary>Confirms the current loadout. Only valid packages may be
    /// confirmed; otherwise state is untouched and false is returned.</summary>
    public bool TryConfirmLoadout()
    {
        if (!IsLoadoutValid)
        {
            return false;
        }

        LoadoutConfirmed = true;
        NotifyChanged();
        return true;
    }

    // ---- Refinery processing (Phase 04) ----
    // Authoritative short resource-conversion flow. Minerals are deducted on
    // start; Fuel/Alloy settle on completion. Reset cancels cleanly.

    public RefineryProcessState ProcessState { get; private set; } = RefineryProcessState.Idle;

    /// <summary>Selected batch size currently being processed (minerals).</summary>
    public int ActiveBatchMinerals { get; private set; }

    /// <summary>Fuel awarded when the running batch completes (result while Complete).</summary>
    public int PendingFuel { get; private set; }

    /// <summary>Alloy awarded when the running batch completes (result while Complete).</summary>
    public int PendingAlloy { get; private set; }

    public float ProcessRemainingSeconds { get; private set; }

    private float _runDurationSeconds;

    public bool IsProcessing => ProcessState == RefineryProcessState.Running;

    /// <summary>0..1 progress of the running batch (0 when not running).</summary>
    public float ProcessProgress01
    {
        get
        {
            if (!IsProcessing || _runDurationSeconds <= 0f)
            {
                return 0f;
            }

            float elapsed = _runDurationSeconds - ProcessRemainingSeconds;
            return Mathf.Clamp01(elapsed / _runDurationSeconds);
        }
    }

    /// <summary>Expected Fuel output for a batch size (deterministic).</summary>
    public static int FuelGainFor(int batchMinerals)
    {
        return batchMinerals / ProcessBatchBase * ProcessFuelReward;
    }

    /// <summary>Expected Alloy output for a batch size (deterministic).</summary>
    public static int AlloyGainFor(int batchMinerals)
    {
        return batchMinerals / ProcessBatchBase * ProcessAlloyReward;
    }

    /// <summary>True when <paramref name="batch"/> may be started right now:
    /// not already processing and enough minerals available.</summary>
    public bool CanStartBatch(int batchMinerals)
    {
        return !IsProcessing
               && (batchMinerals == 5 || batchMinerals == 10 || batchMinerals == 15)
               && Minerals >= batchMinerals;
    }

    /// <summary>
    /// Starts a refinery batch: deducts minerals immediately and moves to
    /// Running. Returns false (state untouched) when already processing,
    /// the batch is unsupported, or minerals are insufficient. Completion
    /// (via timer or AdvanceProcessing) settles the pending outputs.
    /// </summary>
    public bool TryStartProcess(int batchMinerals)
    {
        if (IsProcessing)
        {
            return false;
        }

        if (batchMinerals != 5 && batchMinerals != 10 && batchMinerals != 15)
        {
            return false;
        }

        if (Minerals < batchMinerals)
        {
            return false;
        }

        Minerals -= batchMinerals;
        ActiveBatchMinerals = batchMinerals;
        PendingFuel = FuelGainFor(batchMinerals);
        PendingAlloy = AlloyGainFor(batchMinerals);
        ProcessState = RefineryProcessState.Running;
        // Duration is captured at start from the current RefineryLevel.
        _runDurationSeconds = RefineryProcessSeconds;
        ProcessRemainingSeconds = _runDurationSeconds;
        NotifyChanged();
        return true;
    }

    /// <summary>Advances the running process deterministically by
    /// <paramref name="seconds"/>; completes (settles outputs) when the timer
    /// reaches zero. No-op while Idle/Complete, so stale output can never be
    /// awarded after a Reset.</summary>
    public void AdvanceProcessing(float seconds)
    {
        if (!IsProcessing)
        {
            return;
        }

        ProcessRemainingSeconds -= seconds;
        if (ProcessRemainingSeconds <= 0f)
        {
            FinishProcess();
        }
    }

    private void FinishProcess()
    {
        Fuel += PendingFuel;
        Alloy += PendingAlloy;
        ProcessState = RefineryProcessState.Complete;
        ProcessRemainingSeconds = 0f;
        NotifyChanged();
    }

    private void ResetProcessing()
    {
        ProcessState = RefineryProcessState.Idle;
        ActiveBatchMinerals = 0;
        PendingFuel = 0;
        PendingAlloy = 0;
        ProcessRemainingSeconds = 0f;
        _runDurationSeconds = 0f;
    }

    private bool _initialized;

    /// <summary>Returns the single authoritative session, creating it when absent.</summary>
    public static GameSession Ensure()
    {
        if (Instance != null)
        {
            return Instance;
        }

        var go = new GameObject("GameSession");
        DontDestroyOnLoad(go);
        return go.AddComponent<GameSession>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // Duplicate protection: keep the first authoritative session.
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (!_initialized)
        {
            ResetSession();
            _initialized = true;
        }
    }

    /// <summary>Restores the default session state and notifies listeners.
    /// Preparation state also returns to its defaults (optional equipment
    /// clear, confirmation cleared; the required Harvester is always carried).</summary>
    public void ResetSession()
    {
        Energy = DefaultEnergy;
        Food = DefaultFood;
        Minerals = DefaultMinerals;
        Fuel = DefaultFuel;
        Alloy = DefaultAlloy;
        CurrentPlanetIndex = DefaultCurrentPlanetIndex;
        HarvesterLevel = DefaultHarvesterLevel;
        RefineryLevel = DefaultRefineryLevel;
        OutpostLevel = DefaultOutpostLevel;
        BatterySelected = false;
        DroneSelected = false;
        LoadoutConfirmed = false;
        PlayerHealth = DefaultPlayerHealth;
        PlayerSatiety = DefaultPlayerSatiety;
        _harvestedNodeMask = 0;
        RecordedSurfaceTargetNodeId = -1;
        ResetProcessing();
        NotifyChanged();
    }

    public void AddMinerals(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        Minerals += amount;
        NotifyChanged();
    }

    public void AddAlloy(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        Alloy += amount;
        NotifyChanged();
    }

    /// <summary>Clamped personal health mutation (tests/debug only; no damage
    /// simulation exists yet).</summary>
    public void SetPlayerHealth(int value)
    {
        int clamped = Mathf.Clamp(value, 0, 100);
        if (clamped == PlayerHealth)
        {
            return;
        }

        PlayerHealth = clamped;
        NotifyChanged();
    }

    /// <summary>Clamped personal satiety mutation (tests/debug only; no decay
    /// exists yet). Lower value = hungrier.</summary>
    public void SetPlayerSatiety(int value)
    {
        int clamped = Mathf.Clamp(value, 0, 100);
        if (clamped == PlayerSatiety)
        {
            return;
        }

        PlayerSatiety = clamped;
        NotifyChanged();
    }

    /// <summary>
    /// Outpost module progression (Phase 05).
    ///
    /// Intentional migration from the Phase 01 flat "3 Alloy per refinery
    /// level" upgrade to deterministic per-module costs (Harvester/Refinery:
    /// cost = current level * 3; Core: cost = current level * 5), a LV.3 cap
    /// and a Core prerequisite (subsystems may not exceed Core level + 1).
    /// </summary>

    /// <summary>Authoritative level of the given module.</summary>
    public int ModuleLevel(OutpostModule module)
    {
        switch (module)
        {
            case OutpostModule.Core:
                return OutpostLevel;
            case OutpostModule.Harvester:
                return HarvesterLevel;
            default:
                return RefineryLevel;
        }
    }

    /// <summary>Deterministic Alloy cost to reach the next tier.</summary>
    public int GetUpgradeCost(OutpostModule module)
    {
        int level = ModuleLevel(module);
        int unit = module == OutpostModule.Core ? 5 : 3;
        return level * unit;
    }

    /// <summary>Validation without mutation. Returns the blocking reason, or
    /// None when the upgrade may proceed.</summary>
    public UpgradeFailureReason CanUpgrade(OutpostModule module)
    {
        int level = ModuleLevel(module);
        if (level >= MaxModuleLevel)
        {
            return UpgradeFailureReason.MaxLevel;
        }

        if (module != OutpostModule.Core && level + 1 > SubsystemLevelCap)
        {
            return UpgradeFailureReason.CoreLevelRequired;
        }

        if (Alloy < GetUpgradeCost(module))
        {
            return UpgradeFailureReason.InsufficientAlloy;
        }

        return UpgradeFailureReason.None;
    }

    /// <summary>Executes one upgrade transaction when valid: subtracts the
    /// exact cost once, increments the exact module once, notifies once.
    /// Returns the failure reason; state is untouched on failure.</summary>
    public UpgradeFailureReason TryUpgrade(OutpostModule module)
    {
        UpgradeFailureReason reason = CanUpgrade(module);
        if (reason != UpgradeFailureReason.None)
        {
            return reason;
        }

        Alloy -= GetUpgradeCost(module);
        switch (module)
        {
            case OutpostModule.Core:
                OutpostLevel += 1;
                break;
            case OutpostModule.Harvester:
                HarvesterLevel += 1;
                break;
            default:
                RefineryLevel += 1;
                break;
        }

        NotifyChanged();
        return UpgradeFailureReason.None;
    }

    private void Update()
    {
        // Authoritative short process timer only; static UI never refreshes
        // from Update. Processing belongs to this persistent session, so it
        // keeps advancing while the player is on any other screen.
        if (_initialized && Instance == this && IsProcessing)
        {
            AdvanceProcessing(Time.deltaTime);
        }
    }

    private void NotifyChanged()
    {
        OnStateChanged?.Invoke();
    }
}
