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

    // Prototype processing cost / reward.
    public const int ProcessMineralsCost = 5;
    public const int ProcessFuelReward = 2;
    public const int ProcessAlloyReward = 1;

    // Prototype refinery upgrade cost.
    public const int RefineryUpgradeAlloyCost = 3;

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

    /// <summary>
    /// Processes 5 Minerals into +2 Fuel and +1 Alloy.
    /// Returns false (state untouched) when Minerals &lt; 5.
    /// </summary>
    public bool TryProcessMinerals()
    {
        if (Minerals < ProcessMineralsCost)
        {
            return false;
        }

        Minerals -= ProcessMineralsCost;
        Fuel += ProcessFuelReward;
        Alloy += ProcessAlloyReward;
        NotifyChanged();
        return true;
    }

    /// <summary>
    /// Upgrades the Refinery for 3 Alloy.
    /// Returns false (state untouched) when Alloy &lt; 3.
    /// </summary>
    public bool TryUpgradeRefinery()
    {
        if (Alloy < RefineryUpgradeAlloyCost)
        {
            return false;
        }

        Alloy -= RefineryUpgradeAlloyCost;
        RefineryLevel += 1;
        NotifyChanged();
        return true;
    }

    private void NotifyChanged()
    {
        OnStateChanged?.Invoke();
    }
}
