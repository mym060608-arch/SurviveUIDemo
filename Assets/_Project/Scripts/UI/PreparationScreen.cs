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

    private readonly GameSession _session;

    private bool _harvesterSelected;
    private bool _batterySelected;
    private bool _droneSelected;
    private bool _confirmed;

    public PreparationScreen(GameSession session)
    {
        _session = session;
    }

    public void Bind(VisualElement root)
    {
        BindRow(root, ElementNames.HarvesterRowButton, () => _harvesterSelected = !_harvesterSelected);
        BindRow(root, ElementNames.BatteryRowButton, () => _batterySelected = !_batterySelected);
        BindRow(root, ElementNames.DroneRowButton, () => _droneSelected = !_droneSelected);

        Button confirm = root.Q<Button>(ElementNames.ConfirmLoadoutButton);
        if (confirm != null)
        {
            confirm.clicked += () =>
            {
                _confirmed = !_confirmed;
                Refresh(root);
            };
        }
        else
        {
            Debug.LogError("PreparationScreen: missing element " + ElementNames.ConfirmLoadoutButton);
        }
    }

    private void BindRow(VisualElement root, string name, System.Action onToggled)
    {
        Button row = root.Q<Button>(name);
        if (row == null)
        {
            Debug.LogError("PreparationScreen: missing element " + name);
            return;
        }

        row.clicked += () =>
        {
            onToggled();
            // Editing a loadout invalidates a previous confirmation.
            _confirmed = false;
            Refresh(root);
        };
    }

    public void Refresh(VisualElement root)
    {
        int cargo = (_harvesterSelected ? HarvesterWeight : 0)
                    + (_batterySelected ? BatteryWeight : 0)
                    + (_droneSelected ? DroneWeight : 0);

        Label cargoValue = root.Q<Label>(ElementNames.CargoValue);
        if (cargoValue != null)
        {
            cargoValue.text = cargo + " / " + CargoCapacity;
        }

        Label state = root.Q<Label>(ElementNames.LoadoutStateLabel);
        if (state != null)
        {
            state.text = _confirmed ? "STATUS: CONFIRMED" : "STATUS: DRAFT";
        }

        SetRowSelected(root, ElementNames.HarvesterRowButton, _harvesterSelected);
        SetRowSelected(root, ElementNames.BatteryRowButton, _batterySelected);
        SetRowSelected(root, ElementNames.DroneRowButton, _droneSelected);
    }

    private static void SetRowSelected(VisualElement root, string name, bool selected)
    {
        Button row = root.Q<Button>(name);
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
}
