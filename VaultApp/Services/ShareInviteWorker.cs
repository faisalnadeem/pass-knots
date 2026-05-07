namespace VaultApp.Services;

public class ShareInviteWorker : BackgroundService
{
    private readonly IShareInviteQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ShareInviteWorker> _logger;

    public ShareInviteWorker(
        IShareInviteQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<ShareInviteWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                await emailService.SendShareInviteAsync(
                    message.ToEmail,
                    message.OwnerEmail,
                    message.SiteName,
                    message.SignupLink);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed background send of share invite to {Email}", message.ToEmail);
            }
        }
    }
}
