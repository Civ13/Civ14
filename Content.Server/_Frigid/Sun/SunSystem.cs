using Content.Shared.Sun;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server.Sun;

public sealed class SunSystem : SharedSunSystem
{
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    private float _accumulatedFrameTime;
    private float TimeUntilNextCycle = 300;
    private Time CurrentCycle = Time.Noon;

    public override void Initialize()
    {
        base.Initialize();
        _playerManager.PlayerStatusChanged += PlayerStatusChanged;
    }

    private void PlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        var user = e.Session;
        UpdateLighting(user);
    }

    /// <summary>
    /// Cycles the current day/night cycle
    /// </summary>
    public void Cycle()
    {
        switch (CurrentCycle)
        {
            case Time.Day:
                CurrentCycle = Time.Noon;
                break;
            case Time.Noon:
                CurrentCycle = Time.Night;
                break;
            case Time.Night:
                CurrentCycle = Time.Midnight;
                break;
            case Time.Midnight:
                CurrentCycle = Time.Day;
                break;
        }

        UpdateLighting();

        Logger.Debug($"Cycled day night to {CurrentCycle.ToString()}");
    }

    /// <summary>
    /// Updates the lighting of all clients, or an optionally provided one
    /// </summary>
    /// <param name="session"></param>
    public void UpdateLighting(IPlayerSession? session = null)
    {
        var message = new TimeChangeEvent(CurrentCycle);
        var filter = Filter.Empty();

        if (session != null)
        {
            filter.AddPlayer(session);
        }
        else
        {
            filter.AddAllPlayers();
        }

        RaiseNetworkEvent(message, filter);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulatedFrameTime += frameTime;

        if (_accumulatedFrameTime > TimeUntilNextCycle)
        {
            _accumulatedFrameTime -= TimeUntilNextCycle;
            Cycle();
        }
    }
}