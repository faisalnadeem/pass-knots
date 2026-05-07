using System.Threading.Channels;

namespace VaultApp.Services;

public record ShareInviteMessage(string ToEmail, string OwnerEmail, string SiteName, string SignupLink);

public interface IShareInviteQueue
{
    ValueTask EnqueueAsync(ShareInviteMessage message, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ShareInviteMessage> ReadAllAsync(CancellationToken cancellationToken);
}

public class ShareInviteQueue : IShareInviteQueue
{
    private readonly Channel<ShareInviteMessage> _channel = Channel.CreateUnbounded<ShareInviteMessage>();

    public ValueTask EnqueueAsync(ShareInviteMessage message, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(message, cancellationToken);

    public IAsyncEnumerable<ShareInviteMessage> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
