using Robust.Shared.GameStates;

namespace Content.Shared._Stalker.Stamina;
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class StaminaActiveComponent : Component
{

    /// <summary>
    /// Float on which our entity will be "stunned"
    /// </summary>
    [DataField("slowThreshold"), ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float SlowThreshold = 180f;

    /// <summary>
    /// Value to compare with StaminaDamage and set default sprint speed back.
    /// If Stamina damage will be less than this value - default sprint will be set.
    /// </summary>
    [DataField("reviveStaminaLevel"), ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float ReviveStaminaLevel = 80f;

    /// <summary>
    /// Stamina damage to apply when entity is running
    /// </summary>
    [DataField("runStaminaDamage"), ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float RunStaminaDamage = 0.4f;

    /// <summary>
    /// Modifier to set entity sprint speed to a walking speed. Counts himself.
    /// Nothing will happen if you'll set it manually
    /// </summary>
    public float SprintModifier = 0.5f;

    public bool Change;

    /// <summary>
    /// If our entity is slowed already.
    /// Nothing will happen if you'll set it manually.
    /// </summary>
    public bool Slowed = false;

}
