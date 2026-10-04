using System.Net;
using System.Net.Mail;

namespace RowadUmrahSystem.Web.Services
{
    public interface IAuthEmailSender
    {
        Task SendOtpAsync(string email, string subject, string code, CancellationToken cancellationToken = default);
    }

    public sealed class SmtpAuthEmailSender : IAuthEmailSender
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SmtpAuthEmailSender> _logger;

        public SmtpAuthEmailSender(IConfiguration configuration, ILogger<SmtpAuthEmailSender> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendOtpAsync(string email, string subject, string code, CancellationToken cancellationToken = default)
        {
            var smtpSection = _configuration.GetSection("Smtp");
            var host = smtpSection["Host"];
            var from = smtpSection["From"];

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
            {
                _logger.LogWarning("OTP for {Email}: {Code}. Configure Smtp:Host and Smtp:From to send real emails.", email, code);
                return;
            }

            using var message = new MailMessage
            {
                From = new MailAddress(from, smtpSection["FromName"] ?? "Rowad Umrah"),
                Subject = subject,
                Body = BuildBody(code),
                IsBodyHtml = true
            };
            message.To.Add(email);

            using var client = new SmtpClient(host, int.TryParse(smtpSection["Port"], out var port) ? port : 587)
            {
                EnableSsl = bool.TryParse(smtpSection["EnableSsl"], out var enableSsl) ? enableSsl : true
            };

            var username = smtpSection["Username"];
            var password = smtpSection["Password"];
            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            {
                client.Credentials = new NetworkCredential(username, password);
            }

            using var registration = cancellationToken.Register(client.SendAsyncCancel);
            await client.SendMailAsync(message, cancellationToken);
        }

        private static string BuildBody(string code)
        {
            return $$"""
                <div style="font-family:Arial,sans-serif;direction:rtl;text-align:right;color:#111">
                  <h2>رمز التحقق من رواد العمرة</h2>
                  <p>استخدم الرمز التالي لإكمال العملية:</p>
                  <div style="font-size:28px;font-weight:700;letter-spacing:6px;direction:ltr;text-align:center;margin:24px 0;padding:16px;border:1px solid #e1b94f;border-radius:8px">
                    {{WebUtility.HtmlEncode(code)}}
                  </div>
                  <p>إذا لم تطلب هذا الرمز، يمكنك تجاهل هذه الرسالة.</p>
                </div>
                """;
        }
    }
}
