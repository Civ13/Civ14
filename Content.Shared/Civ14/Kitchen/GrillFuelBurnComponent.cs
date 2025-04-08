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
    /// Current heating setting of the campfire based on fuel level.
    /// </summary>
    [DataField("setting")]
    public EntityHeaterSetting Setting = EntityHeaterSetting.Off;

    /// <summary>
    /// Whether the campfire is currently lit or not.
    /// </summary>
    [DataField("isLit")]
    public bool IsLit { get; set; } = false; // Estado aceso/apagado

    /// <summary>
    /// Whether the campfire turns into coal when done (true) or not (false)
    /// </summary>
    [DataField("expends")]
    public bool Expends { get; set; } = true; //


    /// <summary>
    /// Optional sound played when the setting changes.
    /// </summary>
    [DataField("settingSound")]
    public SoundPathSpecifier? SettingSound;
}
