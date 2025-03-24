using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared.Gathering;

[Serializable, NetSerializable]
public sealed partial class StrawCollectDoAfterEvent : DoAfterEvent
{
    public NetEntity GridUid { get; }
    public Vector2i SnapPos { get; }

    public StrawCollectDoAfterEvent(NetEntity gridUid, Vector2i snapPos)
    {
        GridUid = gridUid;
        SnapPos = snapPos;
    }

    public override DoAfterEvent Clone() => this;
}