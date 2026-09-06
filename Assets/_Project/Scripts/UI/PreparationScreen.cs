using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Screen 02 - Preparation. Lightweight loadout selection prototype.
///
/// State is intentionally local to this prototype screen (no inventory
/// system): each equipment row is a selectable button, the sum of the
/// selected weights drives the simple cargo value, and CONFIRM toggles a
/// confirmation state that stays reversible/editable.
/// </summary>
public class PreparationScreen
{
    private const int HarvesterWeight = 40;
    private const int BatteryWeight = 10;
    private const int DroneWeight = 30;
    private const int CargoCapacity = 100;

    private readonly VisualElement _root;
    private readonly GameSession _session;

    private Button _harvesterRow;
    private Button _batteryRow;
    private Button _droneRow;
    private Label _cargoValue;
    private Label _stateLabel;
    private Button _confirmButton;

    private bool _harvesterSelected;
    private bool _batterySelected;
    private bool _droneSelected;
    private bool _confirmed;

    public PreparationScreen(VisualElement root, GameSession session)
    {
        _root = root;
        _session = session;
    }

    public void Bind()
    {
        _harvesterRow = BindRow(ElementNames.HarvesterRowButton, () => _harvesterSelected = !_harvesterSelected);
        _batteryRow = BindRow(ElementNames.BatteryRowButton, () => _batterySelected = !_batterySelected);
        _droneRow = BindRow(ElementNames.DroneRowButton, () => _droneSelected = !_droneSelected);

        _cargoValue = _root.Q<Label>(ElementNames.CargoValue);
        _stateLabel = _root.Q<Label>(ElementNames.LoadoutStateLabel);
        if (_cargoValue == null)
        {
            LogMissing(ElementNames.CargoValue);
        }

        if (_stateLabel == null)
        {
            LogMissing(ElementNames.LoadoutStateLabel);
        }

        _confirmButton = _root.Q<Button>(ElementNames.ConfirmLoadoutButton);
        if (_confirmButton != null)
        {
            _confirmButton.clicked += () =>
            {
                _confirmed = !_confirmed;
                Refresh();
            };
        }
        else
        {
            LogMissing(ElementNames.ConfirmLoadoutButton);
        }
    }

    private Button BindRow(string name, Action onToggled)
    {
        Button row = _root.Q<Button>(name);
        if (row == null)
        {
            LogMissing(name);
            return null;
        }

        row.clicked += () =>
        {
            onToggled();
            // Editing a loadout invalidates a previous confirmation.
            _confirmed = false;
            Refresh();
        };

        return row;
    }

    public void Refresh()
    {
        int cargo = (_harvesterSelected ? HarvesterWeight : 0)
                    + (_batterySelected ? BatteryWeight : 0)
                    + (_droneSelected ? DroneWeight : 0);

        if (_cargoValue != null)
        {
            _cargoValue.text = cargo + " / " + CargoCapacity;
        }

        if (_stateLabel != null)
        {
            _stateLabel.text = _confirmed ? "STATUS: CONFIRMED" : "STATUS: DRAFT";
        }

        RefreshRow(_harvesterRow, _harvesterSelected);
        RefreshRow(_batteryRow, _batterySelected);
        RefreshRow(_droneRow, _droneSelected);
    }

    private static void RefreshRow(Button row, bool selected)
    {
        if (row == null)
        {
            return;
        }

        if (selected)
        {
            row.AddToClassList("selected");
        }
        else
        {
            row.RemoveFromClassList("selected");
        }
    }

    private static void LogMissing(string name)
    {
        Debug.LogError("PreparationScreen: missing element " + name);
    }
}
