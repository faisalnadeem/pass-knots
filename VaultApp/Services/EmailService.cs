using System.Net;
using System.Net.Mail;

namespace VaultApp.Services;

public interface IEmailService
{
    Task SendShareInviteAsync(string toEmail, string ownerEmail, string siteName, string signupLink);
}

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendShareInviteAsync(string toEmail, string ownerEmail, string siteName, string signupLink)
    {
        var host = _config["Email:SmtpHost"];
        var port = int.TryParse(_config["Email:SmtpPort"], out var parsedPort) ? parsedPort : 587;
        var useSsl = bool.TryParse(_config["Email:UseSsl"], out var parsedSsl)
            ? parsedSsl
            : (port == 465 || port == 587);
        var username = _config["Email:Username"];
        var password = _config["Email:Password"];
        var from = _config["Email:From"] ?? username;

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
        {
            _logger.LogWarning("Share invite email not sent; SMTP config missing.");
            return;
        }

        var subject = "A password entry was shared with you";
        var body =
$@"Hi,

{ownerEmail} has shared a password entry ('{siteName}') with you on PassKnots.

Sign up to access it:
{signupLink}

If you already have an account with this email, just log in and it will appear in your shared entries.";

        using var message = new MailMessage(from, toEmail, subject, body);
        using var client = new SmtpClient(host, port)
        {
            EnableSsl = useSsl,
            UseDefaultCredentials = false,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 15000
        };

        if (!string.IsNullOrWhiteSpace(username))
        {
            client.Credentials = new NetworkCredential(username, password ?? "");
        }

        try
        {
            await client.SendMailAsync(message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send share invite email to {Email}", toEmail);
        }
    }
}
