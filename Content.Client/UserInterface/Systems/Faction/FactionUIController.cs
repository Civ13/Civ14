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
using Content.Shared.Civ14.CivFactions; // Existing using
using Content.Client.Popups;
using Content.Shared.Popups;
using System.Linq;
using System.Text;
using Robust.Shared.Network;
using Robust.Shared.GameObjects;
using Robust.Shared.Player; // Required for ICommonSession
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Robust.Shared.IoC; // Added for IoCManager
using Content.Client.Commands;

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
    private PopupSystem? _popupSystem;

    // Store the command instance to manage its registration lifecycle
    private AcceptFactionInviteCommand? _acceptInviteCmdInstance;
    private ISawmill _sawmill = default!;
    private FactionWindow? _window; // Make nullable
    // Ensure the namespace and class name are correct for GameTopMenuBar
    private MenuButton? FactionButton => UIManager.GetActiveUIWidgetOrNull<GameTopMenuBar>()?.FactionButton;

    private bool _factionControllerResourcesCleanedUp = false;

    /// <summary>
    /// Performs initial setup for the faction UI controller, including subscribing to relevant network events and configuring logging.
    /// </summary>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<FactionInviteOfferEvent>(OnFactionInviteOffer);
        SubscribeNetworkEvent<PlayerFactionStatusChangedEvent>(OnPlayerFactionStatusChanged);
        _sawmill = _logMan.GetSawmill("faction");

        // Create an instance of the command
        var acceptInviteCmd = new AcceptFactionInviteCommand();
        IoCManager.InjectDependencies(acceptInviteCmd); // Injects [Dependency] fields in AcceptFactionInviteCommand

        try
        {
            _consoleHost.RegisterCommand(acceptInviteCmd);
            _acceptInviteCmdInstance = acceptInviteCmd; // This instance successfully registered the command
            _sawmill.Debug($"Command '{acceptInviteCmd.Command}' registered successfully by this FactionUIController instance.");
        }
        catch (ArgumentException e) when (e.Message.Contains("An item with the same key has already been added"))
        {
            // Command is already registered, likely by another client instance in a test/tool environment.
            // Log this and assume the existing registration is fine.
            _sawmill.Debug($"Command '{acceptInviteCmd.Command}' was already registered. Skipping registration for this FactionUIController instance. Exception: {e.Message}");
            _acceptInviteCmdInstance = null; // This instance did not register the command.
        }
    }

    /// <summary>
    /// Handles entering the gameplay state by creating and configuring the faction window, wiring up UI events, registering keybinds, and loading the faction menu button.
    /// </summary>
    public void OnStateEntered(GameplayState state)
    {
        // _window should be null here if OnStateExited cleaned up properly
        // DebugTools.Assert(_window == null); // Keep this assertion
        _factionControllerResourcesCleanedUp = false;
        _sawmill.Debug("FactionUIController entering GameplayState.");

        // Retrieve PopupSystem here, as EntityManager should be more reliably initialized.
        _ent.TrySystem(out _popupSystem);

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

    /// <summary>
    /// Cleans up faction UI elements and event handlers when exiting the gameplay state.
    /// </summary>
    /// <param name="state">The gameplay state being exited.</param>
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
            _window = null; // Set to null after closing
        }

        // Unregister keybind
        CommandBinds.Unregister<FactionUIController>();
        _sawmill.Debug("Faction keybind unregistered.");

        // *** ADD THIS LINE ***
        // Unload the button hookup
        UnloadButton();

        // Perform cleanup of resources specific to this controller when exiting the gameplay state.
        // This is used as the primary cleanup point for the command registration
        // due to apparent issues with overriding or extending UIController.Dispose in the current build environment.
        CleanupFactionControllerResources(true); // True for 'disposing managed resources'
    }

    /// <summary>
    /// Performs cleanup of managed resources held by this FactionUIController,
    /// such as unregistering console commands.
    /// This method is called from OnStateExited as the primary cleanup path
    /// because the standard IDisposable pattern with overriding Dispose(bool)
    /// seems problematic in the current build/linting environment (based on CS0115, CS0117).
    /// </summary>
    /// <param name="disposing">True if called because managed resources should be disposed.</param>
    private void CleanupFactionControllerResources(bool disposing)
    {
        if (_factionControllerResourcesCleanedUp)
            return;

        if (disposing)
        {
            if (_acceptInviteCmdInstance != null)
            {
                _consoleHost.UnregisterCommand(_acceptInviteCmdInstance.Command);
                _sawmill.Debug($"Command '{_acceptInviteCmdInstance.Command}' unregistered by FactionUIController in CleanupFactionControllerResources.");
                _acceptInviteCmdInstance = null;
            }
        }
        _factionControllerResourcesCleanedUp = true;
    }

    // Note: The base UIController.Dispose() method (from IDisposable) will be called when this controller is disposed by the UserInterfaceManager.
    // However, due to compiler errors (CS0115 'no suitable method to override' for Dispose(bool), and CS0117 'base.Dispose(bool) not found'),
    // FactionUIController-specific cleanup (like command unregistration) has been moved to OnStateExited via CleanupFactionControllerResources.
    // If the UIController API in the environment were to match the standard RobustToolbox pattern (with a protected virtual Dispose(bool)),
    // that would be the ideal place for this cleanup logic.

    /// <summary>
    /// Retrieves the first available <see cref="CivFactionsComponent"/> found in the game state, or null if none exist.
    /// </summary>
    /// <returns>The first discovered <see cref="CivFactionsComponent"/>, or null if not found.</returns>
    /// <remarks>
    /// Logs detailed information about all found instances and warns if multiple or none are present. The returned component may not be the authoritative instance if multiple exist.
    /// </remarks>
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

    /// <summary>
    /// Handles a faction invite offer by notifying the player with a popup and chat messages containing instructions to accept the invite.
    /// </summary>
    private void OnFactionInviteOffer(FactionInviteOfferEvent msg, EntitySessionEventArgs args)
    {
        _sawmill.Info($"Received faction invite from {msg.InviterName} for faction '{msg.FactionName}'.");

        // Improved feedback using a clickable popup or chat message
        var message = $"{msg.InviterName} invited you to join faction '{msg.FactionName}'.";
        // Include InviterUserId in the command. It needs to be a string for the command line.
        var acceptCommand = $"/acceptfactioninvite \"{msg.FactionName}\" \"{msg.InviterUserId.ToString()}\"";

        // You could use a more interactive popup system if available,
        // but for now, let's add the command hint to the popup/chat.
        var fullMessage = $"{message}\nType '{acceptCommand}' in chat to accept.";

        var localPlayerEntity = _player.LocalSession?.AttachedEntity;
        if (localPlayerEntity.HasValue && _ent.EntityExists(localPlayerEntity))
        {
            _popupSystem?.PopupEntity(fullMessage, localPlayerEntity.Value, PopupType.Medium);
        }
        else
        {
            _popupSystem?.PopupCursor(fullMessage, PopupType.Medium);
            _sawmill.Warning($"Could not show faction invite popup on player entity (entity not found or invalid). Falling back to cursor popup. Message: {fullMessage}");
        }
        // As a very robust fallback, also send to chat, as popups can sometimes be missed or problematic.
        // _consoleHost.ExecuteCommand($"say \"{message}\""); // Optional: 'say' might be too noisy. The popup and echo should suffice.
        _consoleHost.ExecuteCommand($"echo \"To accept, type: {acceptCommand}\""); // Echo to self for easy copy/paste
    }

    /// <summary>
    /// Handles updates to the player's faction status, refreshing the faction window UI and updating the player's faction component if necessary.
    /// </summary>
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

            if (msg.IsInFaction == true && msg.FactionName != null)
            {
                if (_ent.TryGetComponent<CivFactionComponent>(_player.LocalEntity, out var factionComp))
                {
                    _sawmill.Debug($"Updating faction component for player entity: {_player.LocalEntity}");
                    factionComp.SetFaction(msg.FactionName);
                    _sawmill.Debug($"Faction name set to '{msg.FactionName}'({factionComp.FactionName}) in CivFactionComponent.");

                }
            }
        }
        else
        {
            _sawmill.Debug("PlayerFactionStatusChangedEvent received, but window is not open or null. No immediate UI refresh.");
        }
    }



    /// <summary>
    /// Determines whether the local player is a member of any faction and returns the faction name if applicable.
    /// </summary>
    /// <returns>
    /// A tuple containing a boolean indicating membership status and the name of the faction if the player is a member; otherwise, null.
    /// </returns>
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

    /// <summary>
    /// Displays a list of all existing factions and their member counts in the faction window.
    /// </summary>
    /// <remarks>
    /// If no faction data is available or no factions exist, an appropriate message is shown instead.
    /// </remarks>
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
    /// <summary>
    /// Handles the creation of a new faction based on user input, performing client-side validation and sending a creation request to the server.
    /// </summary>
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
            if (_player.LocalSession?.AttachedEntity is { Valid: true } playerEntity) // playerEntity here is EntityUid
                _popupSystem?.PopupEntity(errorMsg, playerEntity, PopupType.SmallCaution); // Use playerEntity directly
            else // Fallback to cursor popup or console if entity/popupsystem is unavailable
                _popupSystem?.PopupCursor(errorMsg, PopupType.SmallCaution);
            return;
        }

        // Check length (sync this limit with server-side validation in CivFactionsSystem)
        const int maxNameLength = 32;
        if (desiredName.Length > maxNameLength)
        {
            _sawmill.Warning($"Create Faction pressed with name too long: {desiredName}");
            var msg = $"Faction name is too long (max {maxNameLength} characters).";
            if (_player.LocalSession?.AttachedEntity is { Valid: true } playerEntity) // playerEntity here is EntityUid
                _popupSystem?.PopupEntity(msg, playerEntity, PopupType.SmallCaution); // Use playerEntity directly
            else // Fallback
                _popupSystem?.PopupCursor(msg, PopupType.SmallCaution);
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

        //probably need to check if the name is being used or not
        if (_ent.TryGetComponent<CivFactionComponent>(_player.LocalEntity, out var factionComp))
        {
            if (factionComp.FactionName == "")
            {
                _sawmill.Debug($"Setting faction name to '{desiredName}' in CivFactionComponent.");
                factionComp.SetFaction(desiredName);
            }
        }
    }

    /// <summary>
    /// Handles the action when the player chooses to leave their current faction, sending a leave request to the server and clearing the local faction component.
    /// </summary>
    private void HandleLeaveFactionPressed()
    {
        _sawmill.Info("Leave Faction button pressed.");
        var leaveEvent = new LeaveFactionRequestEvent();
        // Raise the network event to send it to the server
        _ent.RaisePredictiveEvent(leaveEvent); // Use RaisePredictiveEvent for client-initiated actions
        _sawmill.Info("Sent LeaveFactionRequestEvent to server.");
        if (_ent.TryGetComponent<CivFactionComponent>(_player.LocalEntity, out var factionComp))
        {
            factionComp.SetFaction("");
        }
        // Attempt to refresh the window state immediately.
        // RefreshFactionWindowState(); // Removed: UI will update via PlayerFactionStatusChangedEvent
    }


    /// <summary>
    /// Handles the invite player action from the faction window, validating input, searching for the player by name, and sending an invite request to the server.
    /// </summary>
    private void HandleInvitePlayerPressed()
    {
        _sawmill.Debug("Invite Player button pressed.");

        if (_window == null)
        {
            _sawmill.Error("Attempted to invite player, but FactionWindow is null!");
            return;
        }

        // Get the target player's name from the window's input field
        var targetPlayerName = _window.InvitePlayerNameInputText.Trim();

        if (string.IsNullOrWhiteSpace(targetPlayerName))
        {
            _sawmill.Debug("Invite player: Name field is empty.");
            _popupSystem?.PopupCursor("Player name cannot be empty.", PopupType.SmallCaution);
            return;
        }

        _sawmill.Info($"Attempting to invite player: '{targetPlayerName}'");

        // Find the player session by name (case-insensitive search)
        var targetSession = _player.Sessions.FirstOrDefault( // Sessions are ICommonSession on client
            s => s.Name.Equals(targetPlayerName, StringComparison.OrdinalIgnoreCase) // Name is available on ICommonSession
        );

        if (targetSession == null)
        {
            var notFoundMsg = $"Player '{targetPlayerName}' not found.";
            _sawmill.Warning(notFoundMsg);
            _popupSystem?.PopupCursor(notFoundMsg, PopupType.SmallCaution);
            return;
        }

        // Player found, get their NetUserId. UserId is available on ICommonSession.
        NetUserId targetUserId = targetSession.UserId; // Correctly accesses UserId from ICommonSession

        // Create the event
        var inviteEvent = new InviteFactionRequestEvent(targetUserId);

        // Send the event to the server
        _ent.RaisePredictiveEvent(inviteEvent);
        _sawmill.Info($"Sent InviteFactionRequestEvent for target player '{targetPlayerName}' (ID: {targetUserId}) to server.");
        _popupSystem?.PopupCursor($"Invite sent to {targetPlayerName}.", PopupType.Small);

        _window.ClearInvitePlayerNameInput(); // Clear the input field
    }

    /// <summary>
    /// Refreshes the faction window's main view (in/not in faction) and the faction list.
    /// Call this after actions that might change the player's faction status or the list of factions.
    /// <summary>
    /// Updates the faction window UI to reflect the player's current faction status and the latest faction list.
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

    /// <summary>
    /// Unsubscribes the faction button from its pressed event and deactivates its pressed state.
    /// </summary>
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

    /// <summary>
    /// Subscribes to the faction button's press event and synchronises its pressed state with the faction window's visibility.
    /// </summary>
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

    /// <summary>
    /// Sets the faction button's pressed state to inactive if the button exists.
    /// </summary>
    private void DeactivateButton()
    {
        if (FactionButton == null) return;
        FactionButton.Pressed = false;
        _sawmill.Debug("Deactivated FactionButton visual state.");
    }

    /// <summary>
    /// Sets the faction button's pressed state to active if the button exists.
    /// </summary>
    private void ActivateButton()
    {
        if (FactionButton == null) return;
        FactionButton.Pressed = true;
        _sawmill.Debug("Activated FactionButton visual state.");
    }

    /// <summary>
    /// Closes the faction window if it exists and is currently open.
    /// </summary>
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

    /// <summary>
    /// Handles the faction button press event by toggling the faction window's visibility.
    /// </summary>
    private void FactionButtonPressed(ButtonEventArgs args)
    {
        _sawmill.Debug("FactionButton pressed, calling ToggleWindow.");
        ToggleWindow();
    }

    /// <summary>
    /// Toggles the visibility of the faction management window, updating its state and synchronising the faction button's visual state.
    /// </summary>
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
