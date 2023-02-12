using System.Threading;

namespace Content.Server.Branch;

[RegisterComponent]
public sealed class BranchComponent : Component
{
    [DataField("entity")]
    public string? Entity { get; private set; }
    [DataField("sound")]
    public string Sound = string.Empty;

    [DataField("breakTime")]
    public float BreakTime = 3.0f;

    public CancellationTokenSource? CancelToken;
}
