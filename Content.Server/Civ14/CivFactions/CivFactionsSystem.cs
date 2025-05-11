using Content.Server.Chat.Systems;
using Content.Shared.Civ14.CivFactions;
using Content.Shared.Popups; // Use Shared Popups
using Robust.Server.Player;
using Robust.Shared.Player; // Required for Filter, ICommonSession
using Robust.Shared.Network; // Required for NetUserId, INetChannel
using System.Linq;
using Content.Server.Chat.Managers;
using Content.Shared.Chat;
using Robust.Shared.Map.Components;
using Robust.Shared.GameObjects; // Required for EntityUid
using Content.Server.GameTicking;

namespace Content.Server.Civ14.CivFactions;

public sealed class CivFactionsSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly ChatSystem _chatSystem = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!; // Use IEntityManager
    [Dependency] private readonly GameTicker _gameTicker = default!;
    private EntityUid? _factionsEntity;
    private CivFactionsComponent? _factionsComponent;

    /// <summary>
    /// Initialises the faction system, ensuring the global factions component exists and subscribing to relevant network events for faction management.
    /// </summary>
    public override void Initialize()
    {
        base.Initialize();

        // Attempt to find the global factions component on startup
        EnsureFactionsComponent();

        // Subscribe to network events
        SubscribeNetworkEvent<CreateFactionRequestEvent>(OnCreateFactionRequest);
        SubscribeNetworkEvent<LeaveFactionRequestEvent>(OnLeaveFactionRequest);
        SubscribeNetworkEvent<InviteFactionRequestEvent>(OnInviteFactionRequest);
        SubscribeNetworkEvent<AcceptFactionInviteEvent>(OnAcceptFactionInvite);
    }

    /// <summary>
    /// Performs cleanup operations when the faction system is shut down.
    /// </summary>
    public override void Shutdown()
    {
        base.Shutdown();
    }

    /// <summary>
    /// Ensures the global CivFactionsComponent exists and caches its reference.
    /// Creates one if necessary (e.g., attached to the first map found).
    /// <summary>
    /// Ensures that a global CivFactionsComponent exists and is cached, creating one on a map entity if necessary.
    /// </summary>
    /// <returns>True if the factions component is available and cached; false if it could not be ensured.</returns>
    private bool EnsureFactionsComponent()
    {
        if (!_gameTicker.IsGameRuleActive("FactionRule"))
        {
            Log.Info($"Factions are disabled on this map.");
            return false;
        }
        if (_factionsComponent != null && !_entityManager.Deleted(_factionsEntity))
            return true; // Already cached and valid

        var query = EntityQueryEnumerator<CivFactionsComponent>();
        if (query.MoveNext(out var owner, out var comp))
        {
            _factionsEntity = owner;
            _factionsComponent = comp;
            Log.Info($"Found existing CivFactionsComponent on entity {_entityManager.ToPrettyString(owner)}");
            return true;
        }
        else
        {
            var mapQuery = EntityQueryEnumerator<MapComponent>();
            if (mapQuery.MoveNext(out var mapUid, out _))
            {
                Log.Info($"No CivFactionsComponent found. Creating one on map entity {_entityManager.ToPrettyString(mapUid)}.");
                _factionsComponent = _entityManager.AddComponent<CivFactionsComponent>(mapUid);
                _factionsEntity = mapUid;
                return true;
            }
            else
            {
                Log.Error("Could not find CivFactionsComponent and no map entity found to attach a new one!");
                _factionsComponent = null;
                _factionsEntity = null;
                return false;
            }
        }
    }

    /// <summary>
    /// Handles a request to create a new faction, validating the faction name and player status, and adds the player as the initial member if successful.
    /// </summary>

    private void OnCreateFactionRequest(CreateFactionRequestEvent msg, EntitySessionEventArgs args)
    {
        if (!EnsureFactionsComponent())
        {
            return;
        }
        var sourceEntity = _factionsEntity ?? EntityUid.Invalid; // Use Invalid if component entity is somehow null

        if (_factionsComponent == null || _factionsEntity == null)
        {
            Log.Error($"Player {args.SenderSession.Name} tried to create faction, but CivFactionsComponent is missing!");
            // FIX: Correct arguments for ChatMessageToOne
            var errorMsg = "Cannot create faction: Server configuration error.";
            _chatManager.ChatMessageToOne(ChatChannel.Notifications, errorMsg, errorMsg, sourceEntity, false, args.SenderSession.Channel);
            return;
        }

        var playerSession = args.SenderSession;
        var playerId = playerSession.UserId.ToString();

        // Validation
        if (string.IsNullOrWhiteSpace(msg.FactionName) || msg.FactionName.Length > 32)
        {
            // FIX: Correct arguments for ChatMessageToOne
            var errorMsg = "Invalid faction name.";
            _chatManager.ChatMessageToOne(ChatChannel.Notifications, errorMsg, errorMsg, sourceEntity, false, playerSession.Channel);
            return;
        }

        if (IsPlayerInFaction(playerSession.UserId, out _))
        {
            // FIX: Correct arguments for ChatMessageToOne
            var errorMsg = "You are already in a faction.";
            _chatManager.ChatMessageToOne(ChatChannel.Notifications, errorMsg, errorMsg, sourceEntity, false, playerSession.Channel);
            return;
        }

        if (_factionsComponent.FactionList.Any(f => f.FactionName.Equals(msg.FactionName, StringComparison.OrdinalIgnoreCase)))
        {
            // FIX: Correct arguments for ChatMessageToOne
            var errorMsg = $"Faction name '{msg.FactionName}' is already taken.";
            _chatManager.ChatMessageToOne(ChatChannel.Notifications, errorMsg, errorMsg, sourceEntity, false, playerSession.Channel);
            return;
        }

        // Create the new faction component
        var newFaction = new FactionData // <-- Use FactionData
        {
            FactionName = msg.FactionName,
            FactionMembers = new List<string> { playerId }
        };

        _factionsComponent.FactionList.Add(newFaction);
        Dirty(_factionsEntity.Value, _factionsComponent);
        Log.Info($"Player {playerSession.Name} created faction '{msg.FactionName}'.");

        // Send confirmation message
        var confirmationMsg = $"Faction '{msg.FactionName}' created successfully.";
        _chatManager.ChatMessageToOne(ChatChannel.Notifications, confirmationMsg, confirmationMsg, sourceEntity, false, playerSession.Channel);

        // Notify the client their status changed
        var statusChangeEvent = new PlayerFactionStatusChangedEvent(true, newFaction.FactionName);
        RaiseNetworkEvent(statusChangeEvent, playerSession.Channel); // Target the specific player
    }

    /// <summary>
    /// Handles a player's request to leave their current faction, updating faction membership and notifying the player.
    /// </summary>
    private void OnLeaveFactionRequest(LeaveFactionRequestEvent msg, EntitySessionEventArgs args)
    {
        if (!EnsureFactionsComponent())
        {
            return;
        }
        var sourceEntity = _factionsEntity ?? EntityUid.Invalid;
        if (_factionsComponent == null || _factionsEntity == null) return;

        var playerSession = args.SenderSession;
        var playerId = playerSession.UserId.ToString();

        if (!TryGetPlayerFaction(playerSession.UserId, out var faction))
        {
            // FIX: Correct arguments for ChatMessageToOne
            var errorMsg = "You are not in a faction.";
            _chatManager.ChatMessageToOne(ChatChannel.Notifications, errorMsg, errorMsg, sourceEntity, false, playerSession.Channel);
            return;
        }

        faction!.FactionMembers.Remove(playerId);
        Log.Info($"Player {playerSession.Name} left faction '{faction.FactionName}'.");

        // FIX: Correct arguments for ChatMessageToOne
        var confirmationMsg = $"You have left faction '{faction.FactionName}'.";
        _chatManager.ChatMessageToOne(ChatChannel.Notifications, confirmationMsg, confirmationMsg, sourceEntity, false, playerSession.Channel);

        if (faction.FactionMembers.Count == 0)
        {
            _factionsComponent.FactionList.Remove(faction);
            Log.Info($"Faction '{faction.FactionName}' disbanded as it became empty.");
        }

        Dirty(_factionsEntity.Value, _factionsComponent);

        // Notify the client their status changed
        var statusChangeEvent = new PlayerFactionStatusChangedEvent(false, null);
        RaiseNetworkEvent(statusChangeEvent, playerSession.Channel); // Target the specific player
    }

    /// <summary>
    /// Handles a request for a player to invite another player to their faction, performing validation and sending appropriate notifications and network events.
    /// </summary>
    private void OnInviteFactionRequest(InviteFactionRequestEvent msg, EntitySessionEventArgs args)
    {
        if (!EnsureFactionsComponent())
        {
            return;
        }
        var sourceEntity = _factionsEntity ?? EntityUid.Invalid;
        if (_factionsComponent == null || _factionsEntity == null) return;

        var inviterSession = args.SenderSession;
        var inviterId = inviterSession.UserId;

        if (!TryGetPlayerFaction(inviterId, out var inviterFaction))
        {
            // FIX: Correct arguments for ChatMessageToOne
            var errorMsg = "You must be in a faction to invite others.";
            _chatManager.ChatMessageToOne(ChatChannel.Notifications, errorMsg, errorMsg, sourceEntity, false, inviterSession.Channel);
            return;
        }

        if (!_playerManager.TryGetSessionById(msg.TargetPlayerUserId, out var targetSession))
        {
            // FIX: Correct arguments for ChatMessageToOne
            var errorMsg = "Could not find the player you tried to invite.";
            _chatManager.ChatMessageToOne(ChatChannel.Notifications, errorMsg, errorMsg, sourceEntity, false, inviterSession.Channel);
            return;
        }

        if (IsPlayerInFaction(msg.TargetPlayerUserId, out _))
        {
            // FIX: Correct arguments for ChatMessageToOne (to inviter)
            var inviterErrorMsg = $"{targetSession.Name} is already in a faction.";
            _chatManager.ChatMessageToOne(ChatChannel.Notifications, inviterErrorMsg, inviterErrorMsg, sourceEntity, false, inviterSession.Channel);

            // FIX: Correct arguments for ChatMessageToOne (to target)
            var targetErrorMsg = $"{inviterSession.Name} tried to invite you to '{inviterFaction!.FactionName}', but you are already in a faction.";
            _chatManager.ChatMessageToOne(ChatChannel.Notifications, targetErrorMsg, targetErrorMsg, sourceEntity, false, targetSession.Channel);
            return;
        }

        var offerEvent = new FactionInviteOfferEvent(inviterSession.Name, inviterFaction!.FactionName, inviterId);
        RaiseNetworkEvent(offerEvent, Filter.SinglePlayer(targetSession));

        // FIX: Correct arguments for ChatMessageToOne (confirmation to inviter)
        var inviterConfirmMsg = $"Invitation sent to {targetSession.Name}.";
        _chatManager.ChatMessageToOne(ChatChannel.Notifications, inviterConfirmMsg, inviterConfirmMsg, sourceEntity, false, inviterSession.Channel);

        // FIX: Correct arguments for ChatMessageToOne (notification to target)
        var targetNotifyMsg = $"{inviterSession.Name} has invited you to join the faction '{inviterFaction.FactionName}'. Check your chat or notifications.";
        _chatManager.ChatMessageToOne(ChatChannel.Notifications, targetNotifyMsg, targetNotifyMsg, sourceEntity, false, targetSession.Channel);

        Log.Info($"Player {inviterSession.Name} invited {targetSession.Name} to faction '{inviterFaction.FactionName}'.");
    }

    /// <summary>
    /// Handles a player's acceptance of a faction invitation, adding them to the specified faction and notifying them of the status change.
    /// </summary>
    private void OnAcceptFactionInvite(AcceptFactionInviteEvent msg, EntitySessionEventArgs args)
    {
        if (!EnsureFactionsComponent())
        {
            return;
        }
        var sourceEntity = _factionsEntity ?? EntityUid.Invalid;
        if (_factionsComponent == null || _factionsEntity == null) return;

        var accepterSession = args.SenderSession;
        var accepterId = accepterSession.UserId;
        var accepterIdStr = accepterId.ToString();

        if (IsPlayerInFaction(accepterId, out var currentFaction))
        {
            // FIX: Correct arguments for ChatMessageToOne
            var errorMsg = $"You cannot accept the invite, you are already in faction '{currentFaction!.FactionName}'.";
            _chatManager.ChatMessageToOne(ChatChannel.Notifications, errorMsg, errorMsg, sourceEntity, false, accepterSession.Channel);
            return;
        }

        var targetFaction = _factionsComponent.FactionList.FirstOrDefault(f => f.FactionName.Equals(msg.FactionName, StringComparison.OrdinalIgnoreCase));
        if (targetFaction == null)
        {
            // FIX: Correct arguments for ChatMessageToOne
            var errorMsg = $"The faction '{msg.FactionName}' no longer exists.";
            _chatManager.ChatMessageToOne(ChatChannel.Notifications, errorMsg, errorMsg, sourceEntity, false, accepterSession.Channel);
            return;
        }

        targetFaction.FactionMembers.Add(accepterIdStr);
        Dirty(_factionsEntity.Value, _factionsComponent);

        // FIX: Correct arguments for ChatMessageToOne
        var confirmationMsg = $"You have joined faction '{targetFaction.FactionName}'.";
        _chatManager.ChatMessageToOne(ChatChannel.Notifications, confirmationMsg, confirmationMsg, sourceEntity, false, accepterSession.Channel);
        Log.Info($"Player {accepterSession.Name} accepted invite and joined faction '{targetFaction.FactionName}'.");

        // Notify the client their status changed
        var statusChangeEvent = new PlayerFactionStatusChangedEvent(true, targetFaction.FactionName);
        RaiseNetworkEvent(statusChangeEvent, accepterSession.Channel); // Target the specific player
    }


    /// <summary>
    /// Determines whether the specified player is a member of any faction.
    /// </summary>
    /// <param name="userId">The user ID of the player to check.</param>
    /// <param name="faction">
    /// When this method returns, contains the faction the player belongs to if found; otherwise, null.
    /// </param>
    /// <returns>True if the player is in a faction; otherwise, false.</returns>

    public bool IsPlayerInFaction(NetUserId userId, out FactionData? faction) // <-- Use FactionData
    {
        faction = null;
        if (_factionsComponent == null)
            return false;

        var playerIdStr = userId.ToString();
        foreach (var f in _factionsComponent.FactionList)
        {
            if (f.FactionMembers.Contains(playerIdStr))
            {
                faction = f;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Attempts to find the faction that the specified player belongs to.
    /// </summary>
    /// <param name="userId">The user ID of the player.</param>
    /// <param name="faction">When this method returns, contains the player's faction if found; otherwise, null.</param>
    /// <returns>True if the player is a member of a faction; otherwise, false.</returns>
    public bool TryGetPlayerFaction(NetUserId userId, out FactionData? faction) // <-- Use FactionData
    {
        return IsPlayerInFaction(userId, out faction);
    }
}
