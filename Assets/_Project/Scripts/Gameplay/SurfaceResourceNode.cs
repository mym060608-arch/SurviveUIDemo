using System;
using UnityEngine;

/// <summary>
/// NEREID-04 resource node (Phase 06). Scene-side state machine that only
/// settles Minerals through GameSession.TryCompleteSurfaceNode - the visual
/// never writes resources directly.
///
/// States: Available -> Harvesting -> Transmitting -> Depleted.
/// Harvest duration uses the real Phase 05 HarvesterEfficiencyMultiplier.
/// Completion persists in GameSession (bit mask), so re-entering the scene
/// restores Depleted nodes and duplicates can never pay out.
/// </summary>
public class SurfaceResourceNode : MonoBehaviour, IFirstPersonInteractable
{
    public enum NodeState
    {
        Available,
        Harvesting,
        Transmitting,
        Depleted
    }

    [SerializeField]
    public int nodeId;

    [SerializeField]
    public int mineralYield = GameSession.SurfaceNodeYield;

    // Visual hooks wired by the deterministic scene builder.
    public Renderer[] baseRenderers;
    public Renderer beaconRenderer;
    public GameObject harvester;
    public GameObject transferBeam;
    public Material depletedBaseMaterial;
    public Material depletedBeaconMaterial;

    public int NodeId => nodeId;
    public NodeState State { get; private set; } = NodeState.Available;

    /// <summary>Remaining seconds of the current harvest/transfer stage.</summary>
    public float RemainingSeconds { get; private set; }

    /// <summary>Total duration of the current stage (for HUD percentage).</summary>
    public float StageSeconds { get; private set; }

    public bool IsBusy => State == NodeState.Harvesting || State == NodeState.Transmitting;

    /// <summary>Fired whenever the node state changes (controller/HUD listeners).</summary>
    public event Action<SurfaceResourceNode> OnStateChanged;

    /// <summary>Fired once after a successful wireless transfer settled +yield
    /// Minerals into GameSession.</summary>
    public event Action<SurfaceResourceNode, int> OnSettled;

    private GameSession Session => GameSession.Instance;

    // ---- IFirstPersonInteractable (Phase 06 Revision) ----
    // Gaze interaction routes here through FirstPersonInteractor; the
    // authoritative deploy logic below is untouched.

    public bool CanInteract()
    {
        return State == NodeState.Available
               && Session != null
               && !Session.IsSurfaceNodeCompleted(nodeId);
    }

    public string GetInteractionPrompt()
    {
        switch (State)
        {
            case NodeState.Available:
                return InputDeviceTracker.FormatPrompt("部署采集器");
            case NodeState.Harvesting:
                return "采集中";
            case NodeState.Transmitting:
                return "无线传输中";
            default:
                return "资源节点已回收";
        }
    }

    public void Interact()
    {
        TryStartHarvest();
    }

    private void Awake()
    {
        if (nodeId < 0 || nodeId >= GameSession.SurfaceNodeCount)
        {
            Debug.LogError("SurfaceResourceNode: invalid id " + nodeId);
            return;
        }

        if (Session != null && Session.IsSurfaceNodeCompleted(nodeId))
        {
            EnterDepleted(true);
        }
        else
        {
            State = NodeState.Available;
            ApplyAvailableVisuals();
        }
    }

    private void Update()
    {
        AdvanceTimers(Time.deltaTime);
    }

    /// <summary>Advances harvest/transfer timers deterministically (Update
    /// drives it with real delta; tests call with fixed seconds).</summary>
    public void AdvanceTimers(float seconds)
    {
        if (State == NodeState.Harvesting)
        {
            RemainingSeconds -= seconds;
            if (RemainingSeconds <= 0f)
            {
                BeginTransfer();
            }

            return;
        }

        if (State == NodeState.Transmitting)
        {
            RemainingSeconds -= seconds;
            if (RemainingSeconds <= 0f)
            {
                Settle();
            }
        }
    }

    /// <summary>Only an Available node may be deployed; a second deployment is
    /// rejected safely (no duplicate harvester/timer).</summary>
    public bool TryStartHarvest()
    {
        if (State != NodeState.Available)
        {
            return false;
        }

        if (Session == null || Session.IsSurfaceNodeCompleted(nodeId))
        {
            return false;
        }

        State = NodeState.Harvesting;
        StageSeconds = Session.SurfaceHarvestSeconds;
        RemainingSeconds = StageSeconds;
        ApplyHarvestingVisuals();
        NotifyChanged();
        return true;
    }

    private void BeginTransfer()
    {
        State = NodeState.Transmitting;
        StageSeconds = GameSession.SurfaceWirelessTransferSeconds;
        RemainingSeconds = StageSeconds;
        ApplyTransmittingVisuals();
        NotifyChanged();
    }

    private void Settle()
    {
        bool awarded = Session != null && Session.TryCompleteSurfaceNode(nodeId, mineralYield);
        if (awarded)
        {
            EnterDepleted(false);
            NotifyChanged();
            OnSettled?.Invoke(this, mineralYield);
        }
        else
        {
            // Safety: already completed (e.g. double settle race) - visual
            // never pays out again.
            EnterDepleted(true);
        }
    }

    private void EnterDepleted(bool notify)
    {
        State = NodeState.Depleted;
        RemainingSeconds = 0f;
        ApplyDepletedVisuals();
        if (notify)
        {
            NotifyChanged();
        }
    }

    private void ApplyAvailableVisuals()
    {
        if (harvester != null)
        {
            harvester.SetActive(false);
        }

        if (transferBeam != null)
        {
            transferBeam.SetActive(false);
        }
    }

    private void ApplyHarvestingVisuals()
    {
        if (harvester != null)
        {
            harvester.SetActive(true);
        }

        if (transferBeam != null)
        {
            transferBeam.SetActive(false);
        }
    }

    private void ApplyTransmittingVisuals()
    {
        if (transferBeam != null)
        {
            transferBeam.SetActive(true);
        }
    }

    private void ApplyDepletedVisuals()
    {
        if (harvester != null)
        {
            harvester.SetActive(false);
        }

        if (transferBeam != null)
        {
            transferBeam.SetActive(false);
        }

        if (baseRenderers != null)
        {
            foreach (Renderer renderer in baseRenderers)
            {
                if (renderer != null && depletedBaseMaterial != null)
                {
                    renderer.sharedMaterial = depletedBaseMaterial;
                }
            }
        }

        if (beaconRenderer != null && depletedBeaconMaterial != null)
        {
            beaconRenderer.sharedMaterial = depletedBeaconMaterial;
        }
    }

    private void NotifyChanged()
    {
        OnStateChanged?.Invoke(this);
    }
}
