using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Kitchen;
using Content.Shared.Placeable;
using Content.Shared.Stacks;
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
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly SharedStackSystem _stackSystem = default!;

    // Tracks remaining burn time for each campfire entity (in seconds)
    private readonly Dictionary<EntityUid, float> _remainingBurnTime = new();

    public override void Initialize()
    {
        Log.Debug("INITIALIZING");
        base.Initialize();
        SubscribeLocalEvent<GrillFuelBurnComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<GrillFuelBurnComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<GrillFuelBurnComponent, ItemPlacedEvent>(OnItemPlaced);
        SubscribeLocalEvent<GrillFuelBurnComponent, InteractUsingEvent>(OnInteractUsing);
    }

    private void OnMapInit(EntityUid uid, GrillFuelBurnComponent component, MapInitEvent args)
    {
        _remainingBurnTime[uid] = component.Fuel * 60f; // Initial fuel in seconds (assuming 1 fuel = 1 minute default)
    }

    private void OnItemPlaced(EntityUid uid, GrillFuelBurnComponent comp, ref ItemPlacedEvent args)
    {
        var itemProto = _entityManager.GetComponent<MetaDataComponent>(args.OtherEntity).EntityPrototype?.ID;
        Log.Debug($"Item placed on campfire: {itemProto}");
    }

    private void OnInteractUsing(EntityUid uid, GrillFuelBurnComponent comp, InteractUsingEvent args)
    {
        if (!_entityManager.TryGetComponent<BurnFuelComponent>(args.Used, out var burnFuel))
        {
            Log.Debug($"Item {args.Used} has no BurnFuelComponent");
            return;
        }

        var itemProto = _entityManager.GetComponent<MetaDataComponent>(args.Used).EntityPrototype?.ID;
        Log.Debug($"InteractUsing on campfire with: {itemProto}, BurnTime: {burnFuel.BurnTime}");

        // Check if the item has a StackComponent
        if (_entityManager.TryGetComponent<StackComponent>(args.Used, out var stackComp))
        {
            int availableFuel = stackComp.Count; // How many items in the stack
            int fuelNeeded = comp.MaxFuel - comp.Fuel; // How many more fuel units the campfire can take
            int fuelToAdd = Math.Min(availableFuel, fuelNeeded); // Add only what’s needed or available

            if (fuelToAdd > 0)
            {
                Log.Debug($"Adding {fuelToAdd} fuel units from stack of {stackComp.Count}");
                comp.Fuel += fuelToAdd;
                _remainingBurnTime[uid] = _remainingBurnTime.GetValueOrDefault(uid) + (fuelToAdd * burnFuel.BurnTime * 60f);

                // Reduce the stack count using SharedStackSystem
                _stackSystem.SetCount(args.Used, stackComp.Count - fuelToAdd, stackComp);

                // If stack is depleted, delete it
                if (stackComp.Count <= 0)
                {
                    Log.Debug("Stack depleted, deleting item");
                    QueueDel(args.Used);
                }

                AdjustHeaterSetting(uid, comp);
                //_audio.PlayPvs("/Audio/Effects/click.ogg", uid);
                args.Handled = true;
            }
            else
            {
                Log.Debug("Fuel at max, no fuel added from stack");
            }
        }
        else
        {
            // Non-stack item (single unit)
            if (comp.Fuel < comp.MaxFuel)
            {
                Log.Debug("Adding 1 fuel unit from non-stack item");
                comp.Fuel++;
                _remainingBurnTime[uid] = _remainingBurnTime.GetValueOrDefault(uid) + (burnFuel.BurnTime * 60f);
                QueueDel(args.Used);
                AdjustHeaterSetting(uid, comp);
                //_audio.PlayPvs("/Audio/Effects/click.ogg", uid);
                args.Handled = true;
            }
            else
            {
                Log.Debug("Fuel at max, item not consumed");
            }
        }
    }

    public override void Update(float deltaTime)
    {
        var query = EntityQueryEnumerator<GrillFuelBurnComponent, ItemPlacerComponent>();
        while (query.MoveNext(out var uid, out var comp, out var placer))
        {
            if (comp.Fuel <= 0 || _remainingBurnTime.GetValueOrDefault(uid) <= 0)
            {
                var coordinates = Transform(uid).Coordinates;
                Spawn("Coal1", coordinates);
                QueueDel(uid);
                _remainingBurnTime.Remove(uid);
                continue;
            }

            _remainingBurnTime[uid] -= deltaTime;
            if (_remainingBurnTime[uid] <= 0)
            {
                comp.Fuel--;
                _remainingBurnTime[uid] = 0f;
                AdjustHeaterSetting(uid, comp);
            }

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

        var remainingTime = _remainingBurnTime.GetValueOrDefault(uid) / 60f;
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