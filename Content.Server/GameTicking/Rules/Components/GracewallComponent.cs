namespace Content.Server.GameTicking.Rules.Components;

[RegisterComponent]
public sealed partial class GracewallRuleComponent : Component
{
    /// <summary>
    /// How long the grace wall lasts since the round started
    /// </summary>
    [DataField("gracewallDuration")]
    public TimeSpan GracewallDuration { get; set; } = TimeSpan.FromMinutes(5);
    /// <summary>
    /// Is the grace wall currently active?
    /// </summary>
    [DataField("gracewallActive")]
    public bool GracewallActive { get; set; } = true;


}
