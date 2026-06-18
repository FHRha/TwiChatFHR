namespace TwitchChatCore;

public static class CancellationTokenExtensions
{
    public static Task WhenCanceledAsync(this CancellationToken token)
    {
        var tcs = new TaskCompletionSource();
        token.Register(() => tcs.TrySetResult());
        return tcs.Task;
    }
}
