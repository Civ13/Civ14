using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared.Examine;
using Content.Shared.Kitchen;
using Content.Shared.Placeable;
using Content.Shared.Temperature;
using Robust.Server.Audio;
using Robust.Shared.Timing;
using System.Linq;

namespace Content.Server.Kitchen;

public sealed class GrillFuelBurnSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly TemperatureSystem _temperature = default!;
    [Dependency] private readonly AudioSystem _audio = default!;

    // Tracks accumulated burn time for each campfire entity
    private readonly Dictionary<EntityUid, float> _fuelBurnTimers = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GrillFuelBurnComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<GrillFuelBurnComponent, ExaminedEvent>(OnExamined);
    }

    private void OnMapInit(EntityUid uid, GrillFuelBurnComponent component, MapInitEvent args)
    {
        _fuelBurnTimers[uid] = 0f; // Initialize burn timer for this entity
    }

    public override void Update(float deltaTime)
    {
        var query = EntityQueryEnumerator<GrillFuelBurnComponent, ItemPlacerComponent>();
        while (query.MoveNext(out var uid, out var comp, out var placer))
        {
            if (comp.Fuel <= 0)
            {
                // Spawn ashes and delete the campfire when fuel runs out
                var coordinates = Transform(uid).Coordinates;
                Spawn("Coal1", coordinates);
                QueueDel(uid);
                continue;
            }

            // Accumulate burn time
            if (!_fuelBurnTimers.ContainsKey(uid))
                _fuelBurnTimers[uid] = 0f;
            _fuelBurnTimers[uid] += deltaTime;

            // Use the first fuel burn time as a default for simplicity; extend this if multiple fuel types are tracked
            var burnInterval = comp.FuelBurnTimes.Values.FirstOrDefault() * 60f; // Convert minutes to seconds
            if (_fuelBurnTimers[uid] >= burnInterval)
            {
                comp.Fuel--;
                _fuelBurnTimers[uid] -= burnInterval;
                AdjustHeaterSetting(uid, comp);
            }

            // Apply heat to placed entities if the campfire is burning
            if (comp.Setting != EntityHeaterSetting.Off)
            {
                var energy = SettingPower(comp.Setting) * deltaTime;
                foreach (var ent in placer.PlacedEntities)
                {
                    _temperature.ChangeHeat(ent, energy);
                }
            }
        }
    }

    private void OnExamined(EntityUid uid, GrillFuelBurnComponent comp, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        // Calculate approximate remaining burn time in minutes
        var remainingTime = comp.Fuel * comp.FuelBurnTimes.Values.FirstOrDefault();
        args.PushMarkup($"The campfire has approximately {remainingTime:F1} minutes of fuel remaining.");
    }

    public void ChangeSetting(EntityUid uid, EntityHeaterSetting setting, GrillFuelBurnComponent? comp = null)
    {
        if (!Resolve(uid, ref comp))
            return;

        comp.Setting = setting;
        _appearance.SetData(uid, EntityHeaterVisuals.Setting, setting);
        _audio.PlayPvs(comp.SettingSound, uid);
    }

    private void AdjustHeaterSetting(EntityUid uid, GrillFuelBurnComponent comp)
    {
        // Adjust setting based on fuel level
        var fuelRatio = (float)comp.Fuel / comp.MaxFuel;
        EntityHeaterSetting newSetting;
        if (fuelRatio > 2f / 3f)
            newSetting = EntityHeaterSetting.High;
        else if (fuelRatio > 1f / 3f)
            newSetting = EntityHeaterSetting.Medium;
        else if (fuelRatio > 0f)
            newSetting = EntityHeaterSetting.Low;
        else
            newSetting = EntityHeaterSetting.Off;

        ChangeSetting(uid, newSetting, comp);
    }

    private float SettingPower(EntityHeaterSetting setting)
    {
        // Fixed heat output values for each setting
        switch (setting)
        {
            case EntityHeaterSetting.Low:
                return 800f;
            case EntityHeaterSetting.Medium:
                return 1600f;
            case EntityHeaterSetting.High:
                return 2400f;
            default:
                return 0f;
        }
    }
}