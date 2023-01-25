using Content.Shared.Sun;
using Robust.Client.Graphics;

namespace Content.Client.Sun;

public sealed class SunSystem : SharedSunSystem
{
    [Dependency] private readonly LightManager _lightManager = default!;


    public override void Initialize()
    {
        base.Initialize();
    }

    protected override void OnTimeChangeEvent(TimeChangeEvent message, EntitySessionEventArgs eventArgs)
    {
        switch (message.TimeOfDay)
        {
            case Time.Midnight:
                _lightManager.AmbientLightColor = MidnightColor;
                break;
            case Time.Day:
                _lightManager.AmbientLightColor = DayColor;
                break;
            case Time.Noon:
                _lightManager.AmbientLightColor = NoonColor;
                break;
            case Time.Night:
                _lightManager.AmbientLightColor = NightColor;
                break;
        }
    }
}
