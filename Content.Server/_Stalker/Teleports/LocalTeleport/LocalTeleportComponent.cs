namespace Content.Server._Stalker.Teleports.LocalTeleport;
/// <summary>
/// Contains portal name for <see cref="LocalTeleportSystem"/>
/// </summary>
[RegisterComponent]
public sealed partial class LocalTeleportComponent : Component
{
    // Portal name to link two portals with the same name together
    [DataField("portalName"), ViewVariables(VVAccess.ReadWrite)]
    public string? PortalName = string.Empty;

    [DataField]
    public bool AllowAll;
}
