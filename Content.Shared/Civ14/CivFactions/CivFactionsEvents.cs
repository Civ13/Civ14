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

    /// <summary>
    /// Initialises a new request to create a faction with the specified name.
    /// </summary>
    /// <param name="factionName">The desired name for the new faction.</param>
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

    /// <summary>
    /// Initialises a new invitation request event targeting the specified player for faction invitation.
    /// </summary>
    /// <param name="targetPlayerUserId">The user ID of the player to invite to the faction.</param>
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
    public NetUserId InviterUserId { get; } /// <summary>
    /// Initialises a new faction invitation offer with the inviter's name, faction name, and inviter's user ID.
    /// </summary>
    /// <param name="inviterName">The display name of the player sending the invitation.</param>
    /// <param name="factionName">The name of the faction the invitation is for.</param>
    /// <param name="inviterUserId">The user ID of the player sending the invitation.</param>

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
    public NetUserId InviterUserId { get; } /// <summary>
    /// Initialises a new event indicating that a player has accepted a faction invitation.
    /// </summary>
    /// <param name="factionName">The name of the faction being joined.</param>
    /// <param name="inviterUserId">The user ID of the player who sent the invitation.</param>

    public AcceptFactionInviteEvent(string factionName, NetUserId inviterUserId)
    {
        FactionName = factionName;
        InviterUserId = inviterUserId;
    }
}

// Optional: Decline event if explicit decline handling is needed beyond timeout/ignoring.
// [Serializable, NetSerializable]
// public sealed class DeclineFactionInviteEvent : BaseFactionRequestEvent { ... }

/// <summary>
/// Sent from Server -> Client (specific player) when their faction membership status changes.
/// </summary>
[Serializable, NetSerializable]
public sealed class PlayerFactionStatusChangedEvent : EntityEventArgs
{
    public bool IsInFaction { get; }
    public string? FactionName { get; } /// <summary>
    /// Initialises a new event indicating a player's faction membership status and, if applicable, the faction's name.
    /// </summary>
    /// <param name="isInFaction">Whether the player is currently in a faction.</param>
    /// <param name="factionName">The name of the faction if the player is a member; otherwise, null.</param>

    public PlayerFactionStatusChangedEvent(bool isInFaction, string? factionName)
    {
        IsInFaction = isInFaction;
        FactionName = factionName;
    }
}
