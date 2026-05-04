namespace WorkTimeTracker.Server;

public class ServerOptions
{
    /// <summary>
    /// When true, an agent reaching the server with a previously-unseen
    /// hostname is auto-registered using the token it presented. After
    /// rollout flip this to false so only known token/hostname pairs are
    /// accepted.
    /// </summary>
    public bool AllowAutoRegister { get; set; } = true;

    /// <summary>
    /// Minimum token length agents must present. Tokens below this are
    /// rejected even when AllowAutoRegister is on.
    /// </summary>
    public int MinTokenLength { get; set; } = 16;
}
