using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Screen 03 - Processing / Resource Processing (Phase 04).
///
/// Renders the authoritative refinery flow owned by GameSession:
/// - raw mineral storage (live),
/// - 5/10/15 batch selection (UI selection; start validated session-side),
/// - expected/settled output preview,
/// - short process timer progress (driven by session, ticked while visible),
/// - inventory (Fuel/Alloy) and refinery metadata.
///
/// No per-frame queries: element references are cached once; the progress
/// fill is the only thing updated while running and visible. All state
/// changes refresh through GameSession.OnStateChanged.
/// </summary>
public class ProcessingScreen
{
    private readonly VisualElement _root;
    private readonly GameSession _session;

    private int _selectedBatch = GameSession.ProcessBatchBase;

    private Label _minerals;
    private Label _fuel;
    private Label _alloy;

    private Button _batch5;
    private Button _batch10;
    private Button _batch15;
    private Button _startButton;

    private Label _fuelGain;
    private Label _alloyGain;
    private Label _outputCaption;
    private Label _statusLabel;
    private Label _percentLabel;
    private Label _refineryLevel;
    private VisualElement _progressFill;

    public ProcessingScreen(VisualElement root, GameSession session)
    {
        _root = root;
        _session = session;
    }

    public void Bind()
    {
        _minerals = BindLabel(ElementNames.ProcessingMineralsValue);
        _fuel = BindLabel(ElementNames.ProcessingFuelValue);
        _alloy = BindLabel(ElementNames.ProcessingAlloyValue);

        _batch5 = BindButton(ElementNames.Batch5Button, () => SelectBatch(5));
        _batch10 = BindButton(ElementNames.Batch10Button, () => SelectBatch(10));
        _batch15 = BindButton(ElementNames.Batch15Button, () => SelectBatch(15));

        _fuelGain = BindLabel(ElementNames.ProcessingFuelGainValue);
        _alloyGain = BindLabel(ElementNames.ProcessingAlloyGainValue);
        _outputCaption = BindLabel(ElementNames.ProcessingOutputCaption);
        _statusLabel = BindLabel(ElementNames.ProcessingStatusLabel);
        _percentLabel = BindLabel(ElementNames.ProcessingProgressPercent);
        _refineryLevel = BindLabel(ElementNames.ProcessingRefineryLevel);
        _progressFill = BindElement(ElementNames.ProcessingProgressFill);

        _startButton = BindButton(ElementNames.ProcessingStartButton,
            () => _session.TryStartProcess(_selectedBatch));
    }

    private Button BindButton(string name, System.Action onClick)
    {
        Button button = _root.Q<Button>(name);
        if (button == null)
        {
            Debug.LogError("ProcessingScreen: missing button " + name);
            return null;
        }

        button.clicked += onClick;
        return button;
    }

    private void SelectBatch(int batch)
    {
        if (_session.IsProcessing)
        {
            return;
        }

        _selectedBatch = batch;
        Refresh();
    }

    public void Refresh()
    {
        SetText(_minerals, _session.Minerals);
        SetText(_fuel, _session.Fuel);
        SetText(_alloy, _session.Alloy);

        if (_refineryLevel != null)
        {
            _refineryLevel.text = "LV." + _session.RefineryLevel;
        }

        RefreshBatches();
        RefreshOutput();
        RefreshProgress();
        RefreshStartButton();
    }

    /// <summary>Lightweight per-frame progress refresh while the screen is
    /// visible and a process is running (cached references only).</summary>
    public void TickProgressIfVisible(bool visible)
    {
        if (!visible || !_session.IsProcessing)
        {
            return;
        }

        if (_progressFill != null)
        {
            float percent = _session.ProcessProgress01 * 100f;
            _progressFill.style.width = Length.Percent(percent);
        }

        if (_percentLabel != null)
        {
            _percentLabel.text = Mathf.RoundToInt(_session.ProcessProgress01 * 100f) + "%";
        }
    }

    private void RefreshBatches()
    {
        bool locked = _session.IsProcessing;
        SetBatchEnabled(_batch5, 5, locked);
        SetBatchEnabled(_batch10, 10, locked);
        SetBatchEnabled(_batch15, 15, locked);

        SetBatchSelected(_batch5, _selectedBatch == 5);
        SetBatchSelected(_batch10, _selectedBatch == 10);
        SetBatchSelected(_batch15, _selectedBatch == 15);
    }

    private void SetBatchEnabled(Button button, int batch, bool locked)
    {
        if (button == null)
        {
            return;
        }

        // Insufficient batches are visibly unavailable; a running process
        // locks the whole selector.
        bool available = !locked && _session.Minerals >= batch;
        button.SetEnabled(available);
    }

    private static void SetBatchSelected(Button button, bool selected)
    {
        if (button == null)
        {
            return;
        }

        if (selected)
        {
            button.AddToClassList("batch-selected");
        }
        else
        {
            button.RemoveFromClassList("batch-selected");
        }
    }

    private void RefreshOutput()
    {
        bool showingResult = _session.ProcessState == GameSession.RefineryProcessState.Complete
                             && _session.ActiveBatchMinerals == _selectedBatch;

        if (_outputCaption != null)
        {
            _outputCaption.text = showingResult ? "本次产出" : "预计产出";
        }

        int fuel = showingResult ? _session.PendingFuel : GameSession.FuelGainFor(_selectedBatch);
        int alloy = showingResult ? _session.PendingAlloy : GameSession.AlloyGainFor(_selectedBatch);

        SetGainText(_fuelGain, fuel);
        SetGainText(_alloyGain, alloy);
    }

    private static void SetGainText(Label label, int amount)
    {
        if (label != null)
        {
            label.text = "+" + amount;
        }
    }

    private void RefreshProgress()
    {
        bool running = _session.IsProcessing;
        bool complete = _session.ProcessState == GameSession.RefineryProcessState.Complete;

        if (_statusLabel != null)
        {
            _statusLabel.text = running ? "加工中" : (complete ? "加工完成" : "准备就绪");
        }

        if (_percentLabel != null)
        {
            _percentLabel.text = running
                ? Mathf.RoundToInt(_session.ProcessProgress01 * 100f) + "%"
                : (complete ? "100%" : "—");
        }

        if (_progressFill != null)
        {
            float percent = running
                ? _session.ProcessProgress01 * 100f
                : (complete ? 100f : 0f);
            _progressFill.style.width = Length.Percent(percent);
        }
    }

    private void RefreshStartButton()
    {
        if (_startButton == null)
        {
            return;
        }

        if (_session.IsProcessing)
        {
            _startButton.text = "加工中";
            _startButton.SetEnabled(false);
            return;
        }

        bool canStart = _session.CanStartBatch(_selectedBatch);
        bool complete = _session.ProcessState == GameSession.RefineryProcessState.Complete;

        if (!canStart)
        {
            _startButton.text = "原料不足";
            _startButton.SetEnabled(false);
            return;
        }

        _startButton.text = complete ? "再次加工" : "开始加工";
        _startButton.SetEnabled(true);
    }

    private Label BindLabel(string name)
    {
        Label label = _root.Q<Label>(name);
        if (label == null)
        {
            Debug.LogError("ProcessingScreen: missing label " + name);
        }

        return label;
    }

    private VisualElement BindElement(string name)
    {
        VisualElement element = _root.Q<VisualElement>(name);
        if (element == null)
        {
            Debug.LogError("ProcessingScreen: missing element " + name);
        }

        return element;
    }

    private static void SetText(Label label, int value)
    {
        if (label != null)
        {
            label.text = value.ToString();
        }
    }
}
