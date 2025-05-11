using Content.Client.Gameplay;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Faction.Windows;
using Content.Shared.Input;
using JetBrains.Annotations;
using Robust.Client.Player;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input.Binding;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BaseButton;
using Robust.Client.Console;
using Content.Shared.Civ14.CivFactions;
using Content.Client.Popups;
using Content.Shared.Popups;
using System.Linq;
using System.Text;
using Robust.Shared.Network;
using Robust.Shared.GameObjects;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;


namespace Content.Client.UserInterface.Systems.Faction;

[UsedImplicitly]
public sealed class FactionUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    [Dependency] private readonly IEntityManager _ent = default!;
    [Dependency] private readonly ILogManager _logMan = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IClientConsoleHost _consoleHost = default!;
    [Dependency] private readonly IClientNetManager _netManager = default!;
    private PopupSystem? _popupSystem; // Make nullable
    private ISawmill _sawmill = default!;
    private FactionWindow? _window; // Make nullable
    // Ensure the namespace and class name are correct for GameTopMenuBar
    private MenuButton? FactionButton => UIManager.GetActiveUIWidgetOrNull<GameTopMenuBar>()?.FactionButton;

    public override void Initialize()
    {
        base.Initialize();
        // Try to get PopupSystem. If this fails (e.g., due to initialization order issues),
        // _popupSystem will remain null. We'll attempt to resolve it lazily later if needed,
        // or handle its absence. This avoids a startup crash if EntitySystemManager is problematic.
        SubscribeNetworkEvent<FactionInviteOfferEvent>(OnFactionInviteOffer);
        SubscribeNetworkEvent<PlayerFactionStatusChangedEvent>(OnPlayerFactionStatusChanged);
        _sawmill = _logMan.GetSawmill("faction");
    }

    public void OnStateEntered(GameplayState state)
    {
        // _window should be null here if OnStateExited cleaned up properly
        // DebugTools.Assert(_window == null); // Keep this assertion

        _sawmill.Debug("FactionUIController entering GameplayState.");

        // Create the window instance
        _window = UIManager.CreateWindow<FactionWindow>();
        LayoutContainer.SetAnchorPreset(_window, LayoutContainer.LayoutPreset.CenterTop);
        _sawmill.Debug("FactionWindow created.");

        // Wire up window events
        _window.OnClose += DeactivateButton;
        _window.OnOpen += ActivateButton;
        _window.OnListFactionsPressed += HandleListFactionsPressed;
        _window.OnCreateFactionPressed += HandleCreateFactionPressed;
        _window.OnLeaveFactionPressed += HandleLeaveFactionPressed;
        _window.OnInvitePlayerPressed += HandleInvitePlayerPressed;
        _sawmill.Debug("FactionWindow events subscribed.");

        // Bind the key function
        CommandBinds.Builder
            .Bind(ContentKeyFunctions.OpenFactionsMenu,
                // Use the simpler FromDelegate overload
                InputCmdHandler.FromDelegate(session => // Takes the session argument
                {
                    // Perform the 'canExecute' check manually inside the action
                    if (_window != null)
                    {
                        ToggleWindow();
                    }
                    else
                    {
                        // Log an error if trying to toggle a null window via keybind
                        _sawmill.Error("Tried to toggle FactionWindow via keybind, but it was null.");
                    }
                }))
            .Register<FactionUIController>(); // Registering ties it to this controller's lifecycle

        _sawmill.Debug("Faction keybind registered.");

        // *** Ensure LoadButton is still called ***
        LoadButton();
    }

    public void OnStateExited(GameplayState state)
    {
        _sawmill.Debug("FactionUIController exiting GameplayState.");
        if (_window != null)
        {
            _sawmill.Debug("Cleaning up FactionWindow.");
            _window.OnClose -= DeactivateButton;
            _window.OnOpen -= ActivateButton;
            _window.OnListFactionsPressed -= HandleListFactionsPressed;
            _window.OnCreateFactionPressed -= HandleCreateFactionPressed;
            _window.OnLeaveFactionPressed -= HandleLeaveFactionPressed;
            _window.OnInvitePlayerPressed -= HandleInvitePlayerPressed;

            // Ensure window is closed before disposing
            if (_window.IsOpen)
                _window.Close();
            _window.Dispose();
            _window = null; // Set to null after disposal
        }

        // Unregister keybind
        CommandBinds.Unregister<FactionUIController>();
        _sawmill.Debug("Faction keybind unregistered.");

        // *** ADD THIS LINE ***
        // Unload the button hookup
        UnloadButton();
    }

    private CivFactionsComponent? GetCivFactionsComponent()
    {
        var query = _ent.EntityQueryEnumerator<CivFactionsComponent, MetaDataComponent>();
        CivFactionsComponent? firstComp = null;
        EntityUid? firstOwner = null;
        MetaDataComponent? firstMeta = null;
        int instanceCount = 0;

        _sawmill.Debug("Starting search for CivFactionsComponent instances...");
        while (query.MoveNext(out var ownerUid, out var comp, out var metadata))
        {
            instanceCount++;
            if (firstComp == null) // Store the first one found
            {
                firstComp = comp;
                firstOwner = ownerUid;
                firstMeta = metadata;
            }
            // Log details for every instance found
            var listIsNull = comp.FactionList == null;
            var listCount = listIsNull ? "N/A (list is null)" : comp.FactionList!.Count.ToString();
            _sawmill.Debug($"Discovered CivFactionsComponent on entity {ownerUid} (Name: '{metadata.EntityName}', Prototype: '{metadata.EntityPrototype?.ID ?? "N/A"}'). FactionList is null: {listIsNull}, FactionList count: {listCount}.");
        }

        if (instanceCount > 1 && firstOwner.HasValue && firstMeta != null)
        {
            _sawmill.Warning($"Found {instanceCount} instances of CivFactionsComponent. Using the first one found on entity {firstOwner.Value} (Name: '{firstMeta.EntityName}'). This might not be the authoritative instance.");
        }
        else if (instanceCount == 0)
        {
            _sawmill.Warning("Could not find any CivFactionsComponent in the game state.");
        }
        return firstComp; // Return the first component found, or null if none
    }

    private void OnFactionInviteOffer(FactionInviteOfferEvent msg, EntitySessionEventArgs args)
    {
        _sawmill.Info($"Received faction invite from {msg.InviterName} for faction '{msg.FactionName}'.");

        // Improved feedback using a clickable popup or chat message
        var message = $"{msg.InviterName} invited you to join faction '{msg.FactionName}'.";
        var acceptCommand = $"/acceptfactioninvite \"{msg.FactionName}\""; // Use quotes for names with spaces

        // You could use a more interactive popup system if available,
        // but for now, let's add the command hint to the popup/chat.
        var fullMessage = $"{message}\nType '{acceptCommand}' in chat to accept.";

        if (_player.LocalSession?.AttachedEntity is { Valid: true } playerEntity)
        {
            // Only use _popupSystem if it was successfully retrieved
            _popupSystem?.PopupEntity(fullMessage, playerEntity, PopupType.Medium);
        }
        else
        {
            // Fallback if player entity isn't available or popup system isn't
            _popupSystem?.PopupCursor(fullMessage); // Show on cursor if possible
            _sawmill.Warning($"Could not show faction invite popup on entity (player entity not found or PopupSystem unavailable). Falling back to cursor popup if PopupSystem exists. Message: {fullMessage}");
        }
        // As a very robust fallback, also send to chat, as popups can sometimes be missed or problematic.
        _consoleHost.ExecuteCommand($"say \"{message}\"");
        _consoleHost.ExecuteCommand($"echo \"To accept, type: {acceptCommand}\""); // Echo to self for easy copy/paste
    }

    private void OnPlayerFactionStatusChanged(PlayerFactionStatusChangedEvent msg, EntitySessionEventArgs args)
    {
        _sawmill.Info($"Received PlayerFactionStatusChangedEvent: IsInFaction={msg.IsInFaction}, FactionName='{msg.FactionName ?? "null"}'.");

        if (_window != null && _window.IsOpen)
        {
            _sawmill.Debug("PlayerFactionStatusChangedEvent received while window is open. Updating window state and faction list.");
            // Update the main view (InFactionView/NotInFactionView) based on the event
            _window.UpdateState(msg.IsInFaction, msg.FactionName);
            // Then, explicitly refresh the faction list display based on the latest component data
            // This ensures the list content (member counts, etc.) is also up-to-date.
            HandleListFactionsPressed();
        }
        else
        {
            _sawmill.Debug("PlayerFactionStatusChangedEvent received, but window is not open or null. No immediate UI refresh.");
        }
    }



    private (bool IsInFaction, string? FactionName) GetPlayerFactionStatus()
    {
        var localPlayerSession = _player.LocalSession;
        if (localPlayerSession == null)
        {
            _sawmill.Warning("LocalPlayerSession is null for faction status check.");
            return (false, null);
        }

        // Get the NetUserId and convert it to string for comparison.
        // NetUserId.ToString() produces a consistent lowercase GUID string.
        var localPlayerNetId = localPlayerSession.UserId;
        var localPlayerIdString = localPlayerNetId.ToString();
        _sawmill.Debug($"GetPlayerFactionStatus: Attempting to find player ID string '{localPlayerIdString}' in factions.");


        // Retrieve the global factions component
        var factionsComp = GetCivFactionsComponent();
        if (factionsComp == null)
        {
            _sawmill.Debug("CivFactionsComponent not found for faction status check.");
            return (false, null); // Not necessarily an error if the component doesn't exist yet
        }

        if (factionsComp.FactionList == null)
        {
            _sawmill.Warning("CivFactionsComponent.FactionList is null.");
            return (false, null);
        }

        // Iterate through each faction to check for the player's membership
        foreach (var faction in factionsComp.FactionList)
        {
            // Log the current faction being checked and its members for detailed debugging
            var membersString = faction.FactionMembers == null ? "null" : $"[{string.Join(", ", faction.FactionMembers)}]";
            _sawmill.Debug($"GetPlayerFactionStatus: Checking faction '{faction.FactionName ?? "Unnamed Faction"}'. Members: {membersString}");

            if (faction.FactionMembers != null && faction.FactionMembers.Contains(localPlayerIdString))
            {
                _sawmill.Debug($"GetPlayerFactionStatus: Player ID string '{localPlayerIdString}' FOUND in faction '{faction.FactionName}'.");
                return (true, faction.FactionName);
            }
            else if (faction.FactionMembers == null)
            {
                _sawmill.Debug($"GetPlayerFactionStatus: Faction '{faction.FactionName ?? "Unnamed Faction"}' has a null FactionMembers list.");
            }
            else
            {
                // This branch means FactionMembers is not null, but does not contain localPlayerIdString
                _sawmill.Debug($"GetPlayerFactionStatus: Player ID string '{localPlayerIdString}' NOT found in faction '{faction.FactionName ?? "Unnamed Faction"}'.");
            }
        }

        _sawmill.Debug($"GetPlayerFactionStatus: Player ID string '{localPlayerIdString}' was not found in any faction after checking all.");
        return (false, null);
    }

    private void HandleListFactionsPressed()
    {
        _sawmill.Info("List Factions button pressed. Querying local state...");

        if (_window == null)
        {
            _sawmill.Error("HandleListFactionsPressed called but _window is null!");
            return;
        }

        var factionsComp = GetCivFactionsComponent();
        if (factionsComp == null || factionsComp.FactionList == null) // Check FactionList null
        {
            _window.UpdateFactionList("Faction data not available.");
            _sawmill.Warning("Faction data unavailable for listing.");
            return;
        }

        if (factionsComp.FactionList.Count == 0)
        {
            _window.UpdateFactionList("No factions currently exist.");
            _sawmill.Info("Displayed empty faction list.");
            return;
        }

        var listBuilder = new StringBuilder();
        // OrderBy requires System.Linq
        foreach (var faction in factionsComp.FactionList.OrderBy(f => f.FactionName))
        {
            // Added detailed logging to inspect faction members state
            _sawmill.Debug($"Inspecting faction for UI list: '{faction.FactionName ?? "Unnamed Faction"}'");
            if (faction.FactionMembers == null)
            {
                _sawmill.Debug($"  - FactionMembers list is null.");
            }
            else
            {
                _sawmill.Debug($"  - FactionMembers.Count = {faction.FactionMembers.Count}");
                if (faction.FactionMembers.Count > 0)
                    _sawmill.Debug($"  - Members: [{string.Join(", ", faction.FactionMembers)}]");
            }

            // *** FIX: Construct the string first, then append ***
            string factionLine = $"{faction.FactionName ?? "Unnamed Faction"}: {faction.FactionMembers?.Count ?? 0} members";
            listBuilder.AppendLine(factionLine); // Use the AppendLine(string) overload
        }

        _window.UpdateFactionList(listBuilder.ToString());
        _sawmill.Info($"Displayed faction list with {factionsComp.FactionList.Count} factions.");
    }
    private void HandleCreateFactionPressed()
    {
        if (_window == null)
        {
            _sawmill.Error("Attempted to create faction, but FactionWindow is null!");
            return;
        }

        // Get the desired name from the window's input field
        // Assumes FactionWindow has a public property 'FactionNameInputText'
        var desiredName = _window.FactionNameInputText.Trim();

        // --- Client-side validation (Good practice) ---
        if (string.IsNullOrWhiteSpace(desiredName))
        {
            _sawmill.Warning("Create Faction pressed with empty name.");
            var errorMsg = "Faction name cannot be empty.";
            if (_player.LocalSession?.AttachedEntity is { Valid: true } playerEntity && _popupSystem != null)
                _popupSystem.PopupEntity(errorMsg, playerEntity, PopupType.SmallCaution);
            else // Fallback to cursor popup or console if entity/popupsystem is unavailable
                _popupSystem?.PopupCursor(errorMsg); // Use null-conditional
            return;
        }

        // Check length (sync this limit with server-side validation in CivFactionsSystem)
        const int maxNameLength = 32;
        if (desiredName.Length > maxNameLength)
        {
            _sawmill.Warning($"Create Faction pressed with name too long: {desiredName}");
            var msg = $"Faction name is too long (max {maxNameLength} characters).";
            if (_player.LocalSession?.AttachedEntity is { Valid: true } playerEntity && _popupSystem != null)
                _popupSystem.PopupEntity(msg, playerEntity, PopupType.SmallCaution);
            else // Fallback
                _popupSystem?.PopupCursor(msg); // Use null-conditional
            return;
        }
        // --- End Client-side validation ---

        _sawmill.Info($"Requesting to create faction with name: '{desiredName}'");

        // FIX: Call the constructor directly with the required argument
        var createEvent = new CreateFactionRequestEvent(desiredName);

        // Send the event to the server
        _ent.RaisePredictiveEvent(createEvent);

        _sawmill.Debug("Sent CreateFactionRequestEvent to server.");

        // Optional: Clear the input field in the UI after sending the request
        _window.ClearFactionNameInput(); // Assumes FactionWindow has this method

        // Attempt to refresh the window state immediately.
        // This relies on the server processing the request and the client receiving
        // the updated CivFactionsComponent relatively quickly.
        // A more robust solution might involve a server confirmation event or a short delay.
        // RefreshFactionWindowState(); // Removed: UI will update via PlayerFactionStatusChangedEvent
    }

    private void HandleLeaveFactionPressed()
    {
        _sawmill.Info("Leave Faction button pressed.");
        var leaveEvent = new LeaveFactionRequestEvent();
        // Raise the network event to send it to the server
        _ent.RaisePredictiveEvent(leaveEvent); // Use RaisePredictiveEvent for client-initiated actions
        _sawmill.Info("Sent LeaveFactionRequestEvent to server.");

        // Attempt to refresh the window state immediately.
        // RefreshFactionWindowState(); // Removed: UI will update via PlayerFactionStatusChangedEvent
    }


    private void HandleInvitePlayerPressed()
    {
        _sawmill.Info("Invite Player button pressed.");
        // TODO: Implement player selection UI (e.g., targeting system or player list)
        // Example: Show a prompt for player name/ID
        _consoleHost.ExecuteCommand("echo \"[TODO] Implement player selection UI (e.g., targeting or name input) and send InviteFactionRequestEvent to server...\"");

        // --- Example of how sending would look (needs target player info) ---
        // 1. Get Target Player's NetUserId (e.g., from a selection UI or input field)
        // NetUserId targetUserId = ... get this from UI ...;

        // 2. Create the event
        // var inviteEvent = new InviteFactionRequestEvent { TargetPlayerUserId = targetUserId };

        // 3. Send the event
        // _ent.RaisePredictiveEvent(inviteEvent);
        // _sawmill.Info($"Sent InviteFactionRequestEvent for target {targetUserId} to server.");
    }

    /// <summary>
    /// Refreshes the faction window's main view (in/not in faction) and the faction list.
    /// Call this after actions that might change the player's faction status or the list of factions.
    /// </summary>
    private void RefreshFactionWindowState()
    {
        if (_window == null)
        {
            _sawmill.Warning("RefreshFactionWindowState called but _window is null!");
            return;
        }
        if (!_window.IsOpen) // No need to refresh if not open
        {
            _sawmill.Debug("RefreshFactionWindowState called but window is not open.");
            return;
        }

        _sawmill.Debug("Refreshing faction window state...");
        var (isInFaction, factionName) = GetPlayerFactionStatus();
        _window.UpdateState(isInFaction, factionName); // This updates NotInFactionView vs InFactionView

        HandleListFactionsPressed(); // This updates the FactionListLabel

        _sawmill.Debug("Faction window state refreshed.");
    }

    public void UnloadButton()
    {
        if (FactionButton == null)
        {
            _sawmill.Debug("FactionButton is null during UnloadButton, cannot unsubscribe.");
            return;
        }
        FactionButton.OnPressed -= FactionButtonPressed;
        _sawmill.Debug("Unsubscribed from FactionButton.OnPressed.");
        // Also deactivate button state if window is closed during unload
        DeactivateButton();
    }

    public void LoadButton()
    {
        if (FactionButton == null)
        {
            // This might happen if the UI loads slightly out of order.
            // Could add a small delay/retry or ensure GameTopMenuBar is ready first.
            _sawmill.Warning("FactionButton is null during LoadButton. Button press won't work yet.");
            return; // Can't subscribe if button doesn't exist yet
        }
        // Prevent double-subscribing
        FactionButton.OnPressed -= FactionButtonPressed;
        FactionButton.OnPressed += FactionButtonPressed;
        _sawmill.Debug("Subscribed to FactionButton.OnPressed.");
        // Update button state based on current window state
        if (_window != null)
            FactionButton.Pressed = _window.IsOpen;
    }

    private void DeactivateButton()
    {
        if (FactionButton == null) return;
        FactionButton.Pressed = false;
        _sawmill.Debug("Deactivated FactionButton visual state.");
    }

    private void ActivateButton()
    {
        if (FactionButton == null) return;
        FactionButton.Pressed = true;
        _sawmill.Debug("Activated FactionButton visual state.");
    }

    private void CloseWindow()
    {
        if (_window == null)
        {
            _sawmill.Warning("CloseWindow called but _window is null.");
            return;
        }
        if (_window.IsOpen) // Only close if open
        {
            _window.Close();
            _sawmill.Debug("FactionWindow closed via CloseWindow().");
        }
    }

    private void FactionButtonPressed(ButtonEventArgs args)
    {
        _sawmill.Debug("FactionButton pressed, calling ToggleWindow.");
        ToggleWindow();
    }

    private void ToggleWindow()
    {
        _sawmill.Debug($"ToggleWindow called. Window is null: {_window == null}");
        if (_window == null)
        {
            _sawmill.Error("Attempted to toggle FactionWindow, but it's null!");
            // Maybe try to recreate it? Or just log the error.
            // For now, just return to prevent NullReferenceException
            return;
        }

        _sawmill.Debug($"Window IsOpen: {_window.IsOpen}");
        if (_window.IsOpen)
        {
            CloseWindow();
        }
        else
        {
            _sawmill.Debug("Opening FactionWindow...");
            // Get current status *before* opening
            var (isInFaction, factionName) = GetPlayerFactionStatus();
            _sawmill.Debug($"Player status: IsInFaction={isInFaction}, FactionName={factionName ?? "null"}");

            // Update the window state (which view to show)
            _window.UpdateState(isInFaction, factionName);
            _sawmill.Debug("FactionWindow state updated.");

            // Open the window
            _window.Open();
            _sawmill.Debug("FactionWindow opened.");

            // Optionally, refresh the list immediately on open
            // This ensures the faction list is populated when the window is first opened.
            HandleListFactionsPressed();
        }

        // Update button visual state AFTER toggling
        // Use null-conditional operator just in case FactionButton became null somehow
        // FactionButton?.SetClickPressed(_window?.IsOpen ?? false); // SetClickPressed might not be what you want, .Pressed is usually better for toggle state
        if (FactionButton != null)
        {
            FactionButton.Pressed = _window.IsOpen;
            _sawmill.Debug($"FactionButton visual state set to Pressed: {FactionButton.Pressed}");
        }
    }
}
