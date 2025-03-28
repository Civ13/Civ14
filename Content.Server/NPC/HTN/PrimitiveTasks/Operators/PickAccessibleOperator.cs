using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC.Pathfinding;

namespace Content.Server.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
/// Chooses a nearby coordinate and puts it into the resulting key.
/// </summary>
public sealed partial class PickAccessibleOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    private PathfindingSystem _pathfinding = default!;

    [DataField("rangeKey", required: true)]
    public string RangeKey = string.Empty;

    [DataField("targetCoordinates")]
    public string TargetCoordinates = "TargetCoordinates";

    [DataField("targetKey")]
    public string TargetKey = default!;

    /// supposed be be a toggle to check if the chosen tile is safe
    [DataField("isSafe")]
    public bool IsSafe = false;

    /// <summary>
    /// Where the pathfinding result will be stored (if applicable). This gets removed after execution.
    /// </summary>
    [DataField("pathfindKey")]
    public string PathfindKey = NPCBlackboard.PathfindKey;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _pathfinding = sysManager.GetEntitySystem<PathfindingSystem>();
    }

    /// <inheritdoc/>
    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        // Very inefficient (should weight each region by its node count) but better than the old system
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        blackboard.TryGetValue<float>(RangeKey, out var maxRange, _entManager);

        if (maxRange == 0f)
            maxRange = 7f;

        var path = await _pathfinding.GetRandomPath(
            owner,
            maxRange,
            cancelToken,
            flags: _pathfinding.GetFlags(blackboard));

        if (path.Result != PathResult.Path)
        {
            return (false, null);
        }

        //CIV14, written by the ever incompetent Aciad
        //todo, make it redo the selection if the path doesn't meet the criteria of being further away from
        //the target than the current possition, I mean it seems to me like it should
        //rn it seems like this doesn't actually do anything
        if (IsSafe == true) //not sure entirely if this bit works
        {
            //this should get the value of whatever entity is set as the target
            blackboard.TryGetValue<EntityUid>(TargetKey, out var targetEntity, _entManager);

            var ownerTransform = _entManager.GetComponent<TransformComponent>(owner);
            var targetTransform = _entManager.GetComponent<TransformComponent>(targetEntity);

            //figures out current positions of the entities & the destination target
            var pathEndCoordinates = path.Path.Last().Coordinates;
            var ownerWorldPosition = ownerTransform.WorldPosition;
            var targetWorldPosition = targetTransform.WorldPosition;

            var ownerToTargetDistance = (ownerWorldPosition - targetWorldPosition).Length;
            var targetToPathEndDistance = (targetWorldPosition - pathEndCoordinates.Position).Length;

            if (ownerToTargetDistance.Invoke() < targetToPathEndDistance.Invoke()) //if it ain't far enough away, quit
            {
                return (false, null);
            }
        }





        var target = path.Path.Last().Coordinates;

        return (true, new Dictionary<string, object>()
        {
            { TargetCoordinates, target },
            { PathfindKey, path}
        });
    }
}
