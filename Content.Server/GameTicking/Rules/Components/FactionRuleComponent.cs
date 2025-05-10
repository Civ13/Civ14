namespace Content.Server.GameTicking.Rules.Components;

[RegisterComponent]
public sealed partial class FactionRuleComponent : Component
{
    /// <summary>
    /// Is Nomads factions module active
    /// </summary>
    [DataField("active")]
    public bool Active = true;

}
