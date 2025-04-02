// Content.Shared/Kitchen/GrillFuelBurnComponent.cs
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using System.Collections.Generic;
using Content.Shared.Temperature;

namespace Content.Shared.Kitchen;

[RegisterComponent]
public sealed partial class GrillFuelBurnComponent : Component
{
    /// <summary>
    /// Current amount of fuel in the campfire.
    /// </summary>
    [DataField("fuel")]
    public int Fuel = 2;

    /// <summary>
    /// Maximum amount of fuel the campfire can hold.
    /// </summary>
    [DataField("maxFuel")]
    public int MaxFuel = 10;

    /// <summary>
    /// Dictionary mapping accepted fuel entities (e.g., prototype IDs) to their burn time per unit in minutes.
    /// </summary>
    [DataField("fuelBurnTimes")]
    public Dictionary<string, float> FuelBurnTimes = new()
    {
        { "MaterialWoodPlank", 2.0f } // Example: 2 minutes per unit
    };

    /// <summary>
    /// Current heating setting of the campfire based on fuel level.
    /// </summary>
    [DataField]
    public EntityHeaterSetting Setting = EntityHeaterSetting.Off;

    /// <summary>
    /// Optional sound played when the setting changes.
    /// </summary>
    [DataField]
    public SoundPathSpecifier? SettingSound;
}