using Content.Server.Atmos.EntitySystems;
using Content.Server.Weather;
using Content.Shared.Atmos;
using Content.Shared.Light.Components; // Adicionado para ExpendableLightComponent
using Content.Server.Light.Components; // Adicionado para ExpendableLightComponent
using Robust.Shared.Map;
using Robust.Shared.Map.Components; // Para MapGridComponent
using Robust.Shared.Maths; // Para Vector2i
using Robust.Shared.Timing;
using Content.Server.Atmos.Components;
using Robust.Server.GameObjects;
using Content.Shared.Atmos.Components;


namespace Content.Server.Weather;

/// <summary>
/// System responsible for emitting heat to nearby tiles when a light source is active.
/// </summary>
public sealed class HeatEmitterSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;

    private float _lastUpdateTime;

    public override void Initialize()
    {
        base.Initialize();
        _lastUpdateTime = (float)_timing.CurTime.TotalSeconds;
    }

    /// <summary>
    /// Updates the system every frame, applying heat to nearby tiles based on active light sources.
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var currentTime = (float)_timing.CurTime.TotalSeconds;
        var deltaTime = currentTime - _lastUpdateTime;
        _lastUpdateTime = currentTime;

        var query = EntityQueryEnumerator<HeatEmitterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var heater, out var transform))
        {
            if (!TryComp<PointLightComponent>(uid, out var pointLight) || !pointLight.Enabled)
            {
                continue;
            }

            var gridUid = transform.GridUid;

            if (gridUid == null || !TryComp<MapGridComponent>(gridUid.Value, out var grid))
                continue;

            var position = transform.Coordinates;
            var tileIndices = grid.WorldToTile(position.Position);

            var tileMixture = _atmosphere.GetContainingMixture(uid, true);

            if (tileMixture != null && tileMixture.Temperature != null)
            {
                var currentTemp = tileMixture.Temperature;

                // Apply heat only while temp is less than 30 celsius. Less load for the update (303.15 K)
                if (currentTemp < heater.MaxTemperature)
                {
                    var heatCapacity = _atmosphere.GetHeatCapacity(tileMixture, true); // Heat capacity in J/K
                    var deltaT = heater.HeatingRate * deltaTime;
                    var dQ = heatCapacity * deltaT; // Heat in Joules

                    Log.Debug($"Adicionando {dQ} Joules ao tile {tileIndices} para aumentar a temperatura em {deltaT} K");

                    _atmosphere.AddHeat(tileMixture, dQ);

                }
            }
        }
    }

}