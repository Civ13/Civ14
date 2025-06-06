namespace Content.Server.GameTicking.Rules.Components;

/// <summary>
/// Marks an entity as a Valley supply box that can be delivered for points.
/// </summary>
[RegisterComponent, Access(typeof(ValleyPointsRuleSystem))]
public sealed partial class ValleySupplyBoxComponent : Component
{
    /// <summary>
    /// Whether this supply box has been delivered to a checkpoint.
    /// </summary>
    [DataField]
    public bool Delivered = false;

    /// <summary>
    /// The checkpoint this box is being secured at, if any.
    /// </summary>
    [DataField]
    public EntityUid? SecuringAtCheckpoint = null;
}
