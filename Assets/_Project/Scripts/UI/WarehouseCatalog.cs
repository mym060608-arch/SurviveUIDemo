using System.Collections.Generic;

/// <summary>
/// What a warehouse cell shows (Phase 09B PASS 9).
/// </summary>
public enum WarehouseItemKind
{
    Mineral,
    Fuel,
    Alloy,
    Harvester,
    Battery,
    Drone,
    Cargo
}

/// <summary>
/// One cell of the warehouse grid: the REAL item behind it plus the copy the
/// page shows. Amounts and states are copied from the authoritative session
/// every refresh - the catalog never stores gameplay state.
/// </summary>
public sealed class WarehouseItem
{
    /// <summary>Stable internal id (never shown to the player).</summary>
    public string Id;

    /// <summary>Player-facing name (中文优先).</summary>
    public string Name;

    /// <summary>Which code-drawn mark the cell and the detail panel use.</summary>
    public WarehouseItemKind Icon;

    /// <summary>Current amount for counted resources (ignored when
    /// <see cref="ShowAmount"/> is false).</summary>
    public int Amount;

    /// <summary>False for single pieces of equipment (no "x1" noise).</summary>
    public bool ShowAmount;

    /// <summary>Direct value text for items that are not a plain count
    /// (the cargo mass reads "45 / 100 KG").</summary>
    public string ValueText;

    /// <summary>Equipment state chip ("必需" / "已装备" / "未选择", "" = none).</summary>
    public string State;

    /// <summary>True when the state chip carries the accent colour.</summary>
    public bool StateOn;

    /// <summary>The real item description (single source, never duplicated in
    /// a UI controller).</summary>
    public string Description;

    /// <summary>Legacy element name kept for the existing probes / tests
    /// ("WarehouseMineralsValue" ...). Empty when the cell has no legacy name.</summary>
    public string ValueElementName;

    /// <summary>The count text the cell shows ("x10", "" when hidden).</summary>
    public string AmountText => ShowAmount ? "x" + Amount : string.Empty;
}

/// <summary>
/// The ONE place the Warehouse page reads its items and copy from (Phase 09B
/// PASS 9).
///
/// The page is a READ-ONLY view: every amount comes from GameSession and every
/// description lives here, so no UI controller hardcodes an item string and the
/// page can never invent an item that gameplay does not have. The lists are
/// built fresh on every refresh because the counts change with the session.
/// </summary>
public static class WarehouseCatalog
{
    /// <summary>Tab id of the resource page.</summary>
    public const string ResourcesTab = "resources";

    /// <summary>Tab id of the equipment page.</summary>
    public const string EquipmentTab = "equipment";

    /// <summary>Every resource the ship really stores, in display order.</summary>
    public static List<WarehouseItem> Resources(GameSession session)
    {
        return new List<WarehouseItem>
        {
            new WarehouseItem
            {
                Id = "mineral",
                Name = "矿物",
                Icon = WarehouseItemKind.Mineral,
                Amount = session != null ? session.Minerals : 0,
                ShowAmount = true,
                Description = "常见的天然矿物资源。\n可用于制作、建造和设备升级。",
                ValueElementName = "WarehouseMineralsValue"
            },
            new WarehouseItem
            {
                Id = "fuel",
                Name = "燃料",
                Icon = WarehouseItemKind.Fuel,
                Amount = session != null ? session.Fuel : 0,
                ShowAmount = true,
                Description = "舰船与远征设备的主要能源。\n由精炼模块加工矿物获得。",
                ValueElementName = "WarehouseFuelValue"
            },
            new WarehouseItem
            {
                Id = "alloy",
                Name = "合金",
                Icon = WarehouseItemKind.Alloy,
                Amount = session != null ? session.Alloy : 0,
                ShowAmount = true,
                Description = "结构材料。\n用于据点模块升级与设备制造。",
                ValueElementName = "WarehouseAlloyValue"
            }
        };
    }

    /// <summary>Every piece of equipment the expedition really carries, plus
    /// the current payload mass.</summary>
    public static List<WarehouseItem> Equipment(GameSession session)
    {
        int cargo = session != null ? session.CargoWeight : 0;

        return new List<WarehouseItem>
        {
            new WarehouseItem
            {
                Id = "harvester",
                Name = "采集器 MK-I",
                Icon = WarehouseItemKind.Harvester,
                ShowAmount = false,
                State = "必需",
                StateOn = true,
                Description = "可部署矿物采集模块。\n远征必需装备。",
                ValueElementName = "WarehouseHarvesterState"
            },
            new WarehouseItem
            {
                Id = "battery",
                Name = "备用电池组",
                Icon = WarehouseItemKind.Battery,
                ShowAmount = false,
                State = session != null && session.BatterySelected ? "已装备" : "未选择",
                StateOn = session != null && session.BatterySelected,
                Description = "为远程设备提供辅助能源。",
                ValueElementName = "WarehouseBatteryState"
            },
            new WarehouseItem
            {
                Id = "drone",
                Name = "勘测无人机",
                Icon = WarehouseItemKind.Drone,
                ShowAmount = false,
                State = session != null && session.DroneSelected ? "已装备" : "未选择",
                StateOn = session != null && session.DroneSelected,
                Description = "近距离地形与资源扫描。",
                ValueElementName = "WarehouseDroneState"
            },
            new WarehouseItem
            {
                Id = "cargo",
                Name = "载荷",
                Icon = WarehouseItemKind.Cargo,
                ShowAmount = false,
                ValueText = cargo + " / " + GameSession.CargoCapacityKg + " KG",
                Description = "当前远征配置的载荷重量。\n上限 "
                              + GameSession.CargoCapacityKg + " KG。",
                ValueElementName = "WarehouseCargoValue"
            }
        };
    }

    /// <summary>Items of one tab id (unknown ids fall back to the resources).</summary>
    public static List<WarehouseItem> ForTab(string tabId, GameSession session)
    {
        return string.Equals(tabId, EquipmentTab, System.StringComparison.Ordinal)
            ? Equipment(session)
            : Resources(session);
    }
}
