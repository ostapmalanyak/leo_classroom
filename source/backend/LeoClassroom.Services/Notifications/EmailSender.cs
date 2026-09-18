using LeoClassroom.Services.Util;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Services.Notifications;

public readonly record struct EmailMessage(string ToAddress, string Subject, string Body);

public readonly record struct EmailError(string Reason);

public interface IEmailSender
{
    public ValueTask<OneOf<Success, EmailError>> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

internal sealed class MailKitEmailSender(IOptions<SmtpSettings> options, ILogger<MailKitEmailSender> logger)
    : IEmailSender
{
    public async ValueTask<OneOf<Success, EmailError>> SendAsync(
        EmailMessage message, CancellationToken cancellationToken)
    {
        SmtpSettings settings = options.Value;

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.ToAddress));
        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.Body };

        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(settings.Host, settings.Port, ToSocketOptions(settings.Security), cancellationToken);
            if (!string.IsNullOrEmpty(settings.Username))
            {
                await client.AuthenticateAsync(settings.Username, settings.Password ?? string.Empty, cancellationToken);
            }
            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);

            return new Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "SMTP send to {Recipient} failed", message.ToAddress);

            return new EmailError(ex.Message);
        }
    }

    private static SecureSocketOptions ToSocketOptions(SmtpSecurity security) => security switch
    {
        SmtpSecurity.None => SecureSocketOptions.None,
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.StartTlsWhenAvailable
    };
}
