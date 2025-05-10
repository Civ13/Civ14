using Robust.Shared.Serialization;
using Robust.Shared.Network; // Required for NetUserId

namespace Content.Shared.Civ14.CivFactions;

/// <summary>
/// Base class for faction-related network events for easier subscription if needed.
/// </summary>
[Serializable, NetSerializable]
public abstract class BaseFactionRequestEvent : EntityEventArgs
{
    // Can add common fields here if necessary later
}

/// <summary>
/// Sent from Client -> Server when a player wants to create a new faction.
/// </summary>
[Serializable, NetSerializable]
public sealed class CreateFactionRequestEvent : BaseFactionRequestEvent
{
    public string FactionName { get; }

    public CreateFactionRequestEvent(string factionName)
    {
        FactionName = factionName;
    }
}

/// <summary>
/// Sent from Client -> Server when a player wants to leave their current faction.
/// </summary>
[Serializable, NetSerializable]
public sealed class LeaveFactionRequestEvent : BaseFactionRequestEvent
{
    // No extra data needed, server knows sender.
}

/// <summary>
/// Sent from Client -> Server when a player wants to invite another player.
/// </summary>
[Serializable, NetSerializable]
public sealed class InviteFactionRequestEvent : BaseFactionRequestEvent
{
    /// <summary>
    /// The NetUserId of the player being invited.
    /// </summary>
    public NetUserId TargetPlayerUserId { get; }

    public InviteFactionRequestEvent(NetUserId targetPlayerUserId)
    {
        TargetPlayerUserId = targetPlayerUserId;
    }
}

/// <summary>
/// Sent from Server -> Client (Target) to notify them of a faction invitation.
/// </summary>
[Serializable, NetSerializable]
public sealed class FactionInviteOfferEvent : EntityEventArgs // Not inheriting BaseFactionRequestEvent
{
    public string InviterName { get; }
    public string FactionName { get; }
    public NetUserId InviterUserId { get; } // Needed for the Accept event

    public FactionInviteOfferEvent(string inviterName, string factionName, NetUserId inviterUserId)
    {
        InviterName = inviterName;
        FactionName = factionName;
        InviterUserId = inviterUserId;
    }
}

/// <summary>
/// Sent from Client (Target) -> Server when a player accepts a faction invitation.
/// </summary>
[Serializable, NetSerializable]
public sealed class AcceptFactionInviteEvent : BaseFactionRequestEvent
{
    /// <summary>
    /// The name of the faction being joined.
    /// </summary>
    public string FactionName { get; }
    /// <summary>
    /// The NetUserId of the player who originally sent the invite.
    /// </summary>
    public NetUserId InviterUserId { get; } // To verify the invite context if needed

    public AcceptFactionInviteEvent(string factionName, NetUserId inviterUserId)
    {
        FactionName = factionName;
        InviterUserId = inviterUserId;
    }
}

// Optional: Decline event if explicit decline handling is needed beyond timeout/ignoring.
// [Serializable, NetSerializable]
// public sealed class DeclineFactionInviteEvent : BaseFactionRequestEvent { ... }
