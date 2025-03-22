using Content.Shared.Interaction;
using Content.Shared.DoAfter;
using Content.Server.DoAfter;
using Content.Shared.Maps;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Content.Shared.Farming;
using System.Linq;

namespace Content.Server.Farming;

public sealed partial class DiggingSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly ITileDefinitionManager _tileManager = default!;
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    // Maps digging state
    private readonly Dictionary<string, string> _digProgression = new()
    {
        { "FloorDirt", "FloorDirtDigged_1" },
        { "FloorDirtDigged_1", "FloorDirtDigged_2" },
        { "FloorDirtDigged_2", "FloorDirtDigged_3" }
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DiggingComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<DiggingComponent, DigDoAfterEvent>(OnDoAfter);
    }

    private void OnAfterInteract(Entity<DiggingComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target != null || !args.CanReach)
            return;

        var comp = ent.Comp;
        var user = args.User;
        var clickLocation = args.ClickLocation;

        var gridUid = _transform.GetGrid(clickLocation);
        if (!gridUid.HasValue || !TryComp<MapGridComponent>(gridUid.Value, out var grid))
            return;

        var snapPos = grid.TileIndicesFor(clickLocation);
        var tileRef = _map.GetTileRef(gridUid.Value, grid, snapPos);
        var tileDef = (ContentTileDefinition)_tileManager[tileRef.Tile.TypeId];

        // Checks if tile can be dug
        if (!_digProgression.TryGetValue(tileDef.ID, out var nextTileId))
        {
            return; // Tile already fully dug
        }

        var delay = TimeSpan.FromSeconds(comp.DigTime);
        var netGridUid = GetNetEntity(gridUid.Value);
        var doAfterArgs = new DoAfterArgs(EntityManager, user, delay, new DigDoAfterEvent(netGridUid, snapPos, nextTileId), ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true
        };

        if (_doAfter.TryStartDoAfter(doAfterArgs))
        {
            _popup.PopupEntity("You start digging the soil.", ent, user);
            args.Handled = true;
        }
    }

    private void OnDoAfter(Entity<DiggingComponent> ent, ref DigDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        var gridUid = GetEntity(args.GridUid);
        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var snapPos = args.SnapPos;
        var nextTileId = args.NextTileId;

        var tileRef = _map.GetTileRef(gridUid, grid, snapPos);
        var tileDef = (ContentTileDefinition)_tileManager[tileRef.Tile.TypeId];
        var expectedTileId = _digProgression.FirstOrDefault(x => x.Value == nextTileId).Key;

        if (tileDef.ID != expectedTileId)
        {
            return;
        }

        // Updates tile to the next digging state
        var nextTile = _tileManager[nextTileId];
        _map.SetTile(gridUid, grid, snapPos, new Tile(nextTile.TileId));

        var coordinates = grid.GridTileToLocal(snapPos);
        Spawn("MaterialDirt1", coordinates);

        _popup.PopupEntity("You finish digging the soil.", ent, args.User);
        args.Handled = true;
    }
}