

using Content.Client.Gameplay;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Faction.Windows;
using Content.Shared.Input;
using Content.Shared.Roles;
using JetBrains.Annotations;
using Robust.Client.Player;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input.Binding;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client.UserInterface.Systems.Faction;

[UsedImplicitly]
public sealed class FactionUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    [Dependency] private readonly IEntityManager _ent = default!;
    [Dependency] private readonly ILogManager _logMan = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = _logMan.GetSawmill("faction");

    }

    private FactionWindow? _window;
    private MenuButton? FactionButton => UIManager.GetActiveUIWidgetOrNull<MenuBar.Widgets.GameTopMenuBar>()?.FactionButton;

    public void OnStateEntered(GameplayState state)
    {
        DebugTools.Assert(_window == null);

        _window = UIManager.CreateWindow<FactionWindow>();
        LayoutContainer.SetAnchorPreset(_window, LayoutContainer.LayoutPreset.CenterTop);

        _window.OnClose += DeactivateButton;
        _window.OnOpen += ActivateButton;

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.OpenFactionsMenu,
                InputCmdHandler.FromDelegate(_ => ToggleWindow()))
            .Register<FactionUIController>();
    }

    public void OnStateExited(GameplayState state)
    {
        if (_window != null)
        {
            _window.Close();
            _window = null;
        }

        CommandBinds.Unregister<FactionUIController>();
    }

    public void UnloadButton()
    {
        if (FactionButton == null)
        {
            return;
        }

        FactionButton.OnPressed -= FactionButtonPressed;
    }

    public void LoadButton()
    {
        if (FactionButton == null)
        {
            return;
        }

        FactionButton.OnPressed += FactionButtonPressed;
    }

    private void DeactivateButton()
    {
        if (FactionButton == null)
        {
            return;
        }

        FactionButton.Pressed = false;
    }

    private void ActivateButton()
    {
        if (FactionButton == null)
        {
            return;
        }

        FactionButton.Pressed = true;
    }
    public void OnSystemLoaded()
    {

    }

    public void OnSystemUnloaded()
    {

    }
    private void CloseWindow()
    {
        _window?.Close();
    }
    private void FactionButtonPressed(ButtonEventArgs args)
    {
        ToggleWindow();
    }
    private void ToggleWindow()
    {
        if (_window == null)
            return;

        FactionButton?.SetClickPressed(!_window.IsOpen);

        if (_window.IsOpen)
        {
            CloseWindow();
        }
        else
        {
            //_characterInfo.RequestCharacterInfo();
            _window.Open();
        }
    }
}
