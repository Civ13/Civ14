namespace Content.Shared.Kitchen;

[RegisterComponent]
public sealed partial class BurnFuelComponent : Component
{
    [DataField("burnTime")]
    public float BurnTime = 2f; // Time in minutes the item burns
}