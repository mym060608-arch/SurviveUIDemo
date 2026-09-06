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

    /// <summary>Restores the default session state and notifies listeners.</summary>
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
