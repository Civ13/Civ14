using System.Threading;

namespace Content.Server.Tree;

[RegisterComponent]
public sealed class TreeComponent : Component
{
    [DataField("entity")]
    public string? Entity { get; private set; }
    [DataField("amount")]
    public float Amount = 3.0f;
    [DataField("sound")]
    public string Sound = string.Empty;

    [DataField("breakTime")]
    public float BreakTime = 3.0f;

    public CancellationTokenSource? CancelToken;
}
