using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared.Farming;

[Serializable, NetSerializable]
public sealed partial class DigDoAfterEvent : DoAfterEvent
{
    public NetEntity GridUid { get; }
    public Vector2i SnapPos { get; }
    public string NextTileId { get; }

    public DigDoAfterEvent(NetEntity gridUid, Vector2i snapPos, string nextTileId)
    {
        GridUid = gridUid;
        SnapPos = snapPos;
        NextTileId = nextTileId;
    }

    public override DoAfterEvent Clone() => this;
}