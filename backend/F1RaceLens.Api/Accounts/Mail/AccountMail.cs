using System.Net;
using System.Net.Mail;

namespace F1RaceLens.Api.Accounts;

public enum AccountMailKind { Recovery, Confirmation, EmailChange }
public sealed record AccountMail(AccountMailKind Kind, string Email, string? UserId = null, string? Token = null, string Language = "en");

public interface IAccountMailQueue
{
    bool Available { get; }
    bool Enqueue(AccountMail mail);
}

public interface IAccountMailTransport
{
    Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken);
}

public sealed class SmtpAccountMailTransport(IConfiguration configuration) : IAccountMailTransport
{
    public async Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
    {
        using var message = new MailMessage(configuration["PasswordRecovery:From"]!, recipient) {
            Subject = subject, Body = body, SubjectEncoding = System.Text.Encoding.UTF8, BodyEncoding = System.Text.Encoding.UTF8
        };
        using var smtp = new SmtpClient(configuration["PasswordRecovery:SmtpHost"], configuration.GetValue("PasswordRecovery:SmtpPort", 587)) {
            EnableSsl = true, UseDefaultCredentials = false,
            Credentials = new NetworkCredential(configuration["PasswordRecovery:Username"], configuration["PasswordRecovery:Password"])
        };
        await smtp.SendMailAsync(message, cancellationToken);
    }
}
