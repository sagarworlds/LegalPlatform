using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MailKit.Net.Smtp;
using MimeKit;
using NotificationService.Domain;

namespace NotificationService.Providers;

public class EmailNotificationProvider : INotificationProvider
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailNotificationProvider> _logger;

    public string Channel => "Email";

    public EmailNotificationProvider(IConfiguration configuration, ILogger<EmailNotificationProvider> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> SendAsync(Notification notification)
    {
        try
        {
            var host = _configuration["ConnectionStrings:mailpit:host"] ?? "localhost";
            var portStr = _configuration["ConnectionStrings:mailpit:port"] ?? "1025";
            var port = int.TryParse(portStr, out var p) ? p : 1025;

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Legal Platform", "no-reply@legalplatform.com"));
            message.To.Add(new MailboxAddress("User", $"{notification.UserId}@legalplatform.com"));
            message.Subject = notification.Subject;
            message.Body = new TextPart("plain")
            {
                Text = notification.Message
            };

            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, MailKit.Security.SecureSocketOptions.None);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Email notification sent successfully to {UserId} via Mailpit at {Host}:{Port}", notification.UserId, host, port);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email notification to {UserId}", notification.UserId);
            return false;
        }
    }
}
