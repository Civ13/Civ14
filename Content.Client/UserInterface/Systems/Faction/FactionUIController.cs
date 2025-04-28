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
using System.Linq;
using System.Text;
using Content.Shared.Popups;
using Robust.Client.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.GameObjects;


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
    private ISawmill _sawmill = default!;
    private FactionWindow _window = default!;
    private MenuButton? FactionButton => UIManager.GetActiveUIWidgetOrNull<MenuBar.Widgets.GameTopMenuBar>()?.FactionButton;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<FactionInviteOfferEvent>(OnFactionInviteOffer);
        _sawmill = _logMan.GetSawmill("faction");
    }

    public void OnStateEntered(GameplayState state)
    {
        DebugTools.Assert(_window == null);

        // FIX 2: Ensure UIManager.CreateWindow uses the correct FactionWindow type from the corrected namespace
        _window = UIManager.CreateWindow<FactionWindow>();
        LayoutContainer.SetAnchorPreset(_window, LayoutContainer.LayoutPreset.CenterTop);

        _window.OnClose += DeactivateButton;
        _window.OnOpen += ActivateButton;
        _window.OnListFactionsPressed += HandleListFactionsPressed;
        _window.OnCreateFactionPressed += HandleCreateFactionPressed;
        _window.OnLeaveFactionPressed += HandleLeaveFactionPressed;
        _window.OnInvitePlayerPressed += HandleInvitePlayerPressed;

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.OpenFactionsMenu,
                InputCmdHandler.FromDelegate(_ => ToggleWindow()))
            .Register<FactionUIController>();
    }

    public void OnStateExited(GameplayState state)
    {
        if (_window != null)
        {
            _window.OnClose -= DeactivateButton;
            _window.OnOpen -= ActivateButton;
            _window.OnListFactionsPressed -= HandleListFactionsPressed;
            _window.OnCreateFactionPressed -= HandleCreateFactionPressed;
            _window.OnLeaveFactionPressed -= HandleLeaveFactionPressed;
            _window.OnInvitePlayerPressed -= HandleInvitePlayerPressed;

            _window.Close();
            _window.Dispose();
        }

        CommandBinds.Unregister<FactionUIController>();
    }

    private CivFactionsComponent? GetCivFactionsComponent()
    {
        var query = _ent.EntityQueryEnumerator<CivFactionsComponent>();
        if (query.MoveNext(out var owner, out var comp))
        {
            return comp;
        }
        _sawmill.Warning("Could not find CivFactionsComponent in the game state.");
        return null;
    }

    private void OnFactionInviteOffer(FactionInviteOfferEvent msg, EntitySessionEventArgs args)
    {
        _sawmill.Info($"Received faction invite from {msg.InviterName} for faction '{msg.FactionName}'.");

        var message = $"{msg.InviterName} invited you to join faction '{msg.FactionName}'.\nType 'acceptfactioninvite {msg.FactionName}' to accept.";
        // TODO: Replace with a proper UI confirmation window
        _ent.System<SharedPopupSystem>().PopupEntity(message, _player.LocalPlayer?.ControlledEntity ?? EntityUid.Invalid);
    }

    private (bool IsInFaction, string? FactionName) GetPlayerFactionStatus()
    {
        var localPlayerUserId = _player.LocalSession?.UserId;
        if (localPlayerUserId == null)
        {
            _sawmill.Warning("Could not get local player user ID.");
            return (false, null);
        }

        var factionsComp = GetCivFactionsComponent();
        if (factionsComp == null)
        {
            return (false, null);
        }

        foreach (var faction in factionsComp.FactionList)
        {
            if (faction.FactionMembers.Contains(localPlayerUserId.Value.ToString()))
            {
                return (true, faction.FactionName);
            }
        }

        return (false, null);
    }

    private void HandleListFactionsPressed()
    {
        _sawmill.Info("List Factions button pressed. Querying local state...");

        var factionsComp = GetCivFactionsComponent();
        if (factionsComp == null || _window == null)
        {
            _window?.UpdateFactionList("Faction data not available.");
            return;
        }

        if (factionsComp.FactionList.Count == 0)
        {
            _window.UpdateFactionList("No factions currently exist.");
            return;
        }

        var listBuilder = new StringBuilder();
        foreach (var faction in factionsComp.FactionList.OrderBy(f => f.FactionName))
        {
            listBuilder.AppendLine(string.Format("{0}: {1} members", faction.FactionName, faction.FactionMembers.Count));

        }

        _window.UpdateFactionList(listBuilder.ToString());
        _sawmill.Info($"Displayed faction list with {factionsComp.FactionList.Count} factions.");
    }

    private void HandleCreateFactionPressed()
    {
        _sawmill.Info("Create Faction button pressed.");
        _consoleHost.ExecuteCommand("echo \"[TODO] Implement UI prompt and send create faction request to server...\"");
    }

    private void HandleLeaveFactionPressed()
    {
        _sawmill.Info("Leave Faction button pressed.");
        var leaveEvent = new LeaveFactionRequestEvent();
        //TODO: Raise event
        // _ent.RaiseNetworkEvent(leaveEvent);
        _sawmill.Info("Sent LeaveFactionRequestEvent to server.");
    }


    private void HandleInvitePlayerPressed()
    {
        _sawmill.Info("Invite Player button pressed.");
        _consoleHost.ExecuteCommand("echo \"[TODO] Implement targeting and send invite request to server...\"");
    }

    public void UnloadButton()
    {
        if (FactionButton == null) return;
        FactionButton.OnPressed -= FactionButtonPressed;
    }

    public void LoadButton()
    {
        if (FactionButton == null) return;
        FactionButton.OnPressed += FactionButtonPressed;
    }

    private void DeactivateButton()
    {
        if (FactionButton == null) return;
        FactionButton.Pressed = false;
    }

    private void ActivateButton()
    {
        if (FactionButton == null) return;
        FactionButton.Pressed = true;
    }

    private void CloseWindow()
    {
        _window?.Close(); // Use null-conditional operator here too
    }

    private void FactionButtonPressed(ButtonEventArgs args)
    {
        ToggleWindow();
    }

    private void ToggleWindow()
    {
        if (_window == null)
            return;

        // FIX 1: Use null-conditional operator ?. to access IsOpen
        // Use ?? false to handle the case where _window is null (though the check above handles this,
        // it's good practice when dealing with nullable members)
        if (_window?.IsOpen ?? false)
        {
            CloseWindow();
        }
        else
        {
            var (isInFaction, factionName) = GetPlayerFactionStatus();

            if (_window == null) return;
            _window.UpdateState(isInFaction, factionName);
            _window.Open();
        }
        // FIX 1: Use null-conditional operator ?. and ?? false for SetClickPressed
        FactionButton?.SetClickPressed(_window?.IsOpen ?? false);
    }
}
