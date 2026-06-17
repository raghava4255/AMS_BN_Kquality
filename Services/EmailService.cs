using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MimeKit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Ams.Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public EmailService(IConfiguration config, ILogger<EmailService> logger, IServiceScopeFactory scopeFactory)
        {
            _config = config;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        public async Task SendEmailAsync(string to, string subject, string bodyHtml)
        {
            if (string.IsNullOrWhiteSpace(to)) return;

            string host = _config["SmtpSettings:Host"] ?? "smtp.gmail.com";
            int port = _config.GetValue<int>("SmtpSettings:Port", 587);
            string senderEmail = _config["SmtpSettings:SenderEmail"] ?? "attendance041@gmail.com";
            string senderName = _config["SmtpSettings:SenderName"] ?? "Attendance Management System";
            string appPassword = _config["SmtpSettings:AppPassword"] ?? "";
            bool enableSsl = _config.GetValue<bool>("SmtpSettings:EnableSsl", true);

            var email = new MimeMessage();
            email.From.Add(new MailboxAddress(senderName, senderEmail));
            email.To.Add(MailboxAddress.Parse(to));
            email.Subject = subject;

            // Wrapper template with company branding
            string styledHtml = $@"
                <div style='font-family: Arial, sans-serif; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #e5e7eb; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 6px rgba(0,0,0,0.05);'>
                    <div style='background-color: #3b82f6; color: #ffffff; padding: 16px 20px; font-size: 1.25rem; font-weight: 600; text-align: center;'>
                        Attendance Management System
                    </div>
                    <div style='padding: 24px; line-height: 1.6; background-color: #ffffff;'>
                        {bodyHtml}
                    </div>
                    <div style='background-color: #f9fafb; padding: 12px 20px; font-size: 0.8rem; color: #6b7280; text-align: center; border-top: 1px solid #e5e7eb;'>
                        This is an automated notification from the Attendance Management System. Please do not reply directly to this email.
                    </div>
                </div>";

            email.Body = new TextPart(MimeKit.Text.TextFormat.Html) { Text = styledHtml };

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var logEntry = new EmailLog
            {
                RecipientEmail = to,
                Subject = subject,
                Body = bodyHtml, // store raw body without outer wrapper to save space
                Status = "Pending",
                SentAt = DateTime.Now,
                RetryCount = 0,
                ErrorMessage = ""
            };
            db.EmailLogs.Add(logEntry);
            await db.SaveChangesAsync();

            if (appPassword == "<YOUR_GMAIL_APP_PASSWORD>" || string.IsNullOrWhiteSpace(appPassword))
            {
                _logger.LogWarning($"Email to {to} failed because SMTP AppPassword is not configured.");
                logEntry.Status = "Failed";
                logEntry.ErrorMessage = "SMTP AppPassword is not configured. Please update appsettings.json.";
                await db.SaveChangesAsync();
                return;
            }

            try
            {
                using var smtp = new SmtpClient();
                // Port 465 uses SslOnConnect, port 587 uses StartTls
                var secureSocketOptions = enableSsl 
                    ? (port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls) 
                    : SecureSocketOptions.Auto;
                
                await smtp.ConnectAsync(host, port, secureSocketOptions);
                await smtp.AuthenticateAsync(senderEmail, appPassword);
                await smtp.SendAsync(email);
                await smtp.DisconnectAsync(true);

                logEntry.Status = "Sent";
                logEntry.SentAt = DateTime.Now;
                logEntry.ErrorMessage = "";
                await db.SaveChangesAsync();
                _logger.LogInformation($"Successfully sent email to {to}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to send email to {to}: {ex.Message}");
                logEntry.Status = "Failed";
                logEntry.ErrorMessage = ex.Message;
                await db.SaveChangesAsync();
            }
        }

        public async Task SendTemplatedEmailAsync(string to, string subject, string templateName, Dictionary<string, string> placeholders)
        {
            // Resolve the template file path relative to the application base directory
            string templatePath = Path.Combine(AppContext.BaseDirectory, "EmailTemplates", templateName);

            string bodyHtml;
            if (File.Exists(templatePath))
            {
                bodyHtml = await File.ReadAllTextAsync(templatePath);
                // Replace all {{Key}} placeholders with their values
                foreach (var kv in placeholders)
                    bodyHtml = bodyHtml.Replace($"{{{{{kv.Key}}}}}", kv.Value);
            }
            else
            {
                _logger.LogWarning($"Email template '{templateName}' not found at '{templatePath}'. Sending plain text fallback.");
                bodyHtml = $"<p>This is an automated notification. (Template '{templateName}' was not found.)</p>";
            }

            await SendEmailAsync(to, subject, bodyHtml);
        }

        public async Task RetryEmailAsync(int emailLogId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var logEntry = await db.EmailLogs.FindAsync(emailLogId);
            if (logEntry == null || logEntry.Status == "Sent") return;

            logEntry.RetryCount += 1;
            logEntry.Status = "Pending";
            await db.SaveChangesAsync();

            string host = _config["SmtpSettings:Host"] ?? "smtp.gmail.com";
            int port = _config.GetValue<int>("SmtpSettings:Port", 587);
            string senderEmail = _config["SmtpSettings:SenderEmail"] ?? "attendance041@gmail.com";
            string senderName = _config["SmtpSettings:SenderName"] ?? "Attendance Management System";
            string appPassword = _config["SmtpSettings:AppPassword"] ?? "";
            bool enableSsl = _config.GetValue<bool>("SmtpSettings:EnableSsl", true);

            if (appPassword == "<YOUR_GMAIL_APP_PASSWORD>" || string.IsNullOrWhiteSpace(appPassword))
            {
                logEntry.Status = "Failed";
                logEntry.ErrorMessage = "SMTP AppPassword is not configured.";
                await db.SaveChangesAsync();
                return;
            }

            var email = new MimeMessage();
            email.From.Add(new MailboxAddress(senderName, senderEmail));
            email.To.Add(MailboxAddress.Parse(logEntry.RecipientEmail));
            email.Subject = logEntry.Subject;

            string styledHtml = $@"
                <div style='font-family: Arial, sans-serif; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #e5e7eb; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 6px rgba(0,0,0,0.05);'>
                    <div style='background-color: #3b82f6; color: #ffffff; padding: 16px 20px; font-size: 1.25rem; font-weight: 600; text-align: center;'>
                        Attendance Management System
                    </div>
                    <div style='padding: 24px; line-height: 1.6; background-color: #ffffff;'>
                        {logEntry.Body}
                    </div>
                    <div style='background-color: #f9fafb; padding: 12px 20px; font-size: 0.8rem; color: #6b7280; text-align: center; border-top: 1px solid #e5e7eb;'>
                        This is an automated notification from the Attendance Management System. Please do not reply directly to this email.
                    </div>
                </div>";

            email.Body = new TextPart(MimeKit.Text.TextFormat.Html) { Text = styledHtml };

            try
            {
                using var smtp = new SmtpClient();
                var secureSocketOptions = enableSsl 
                    ? (port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls) 
                    : SecureSocketOptions.Auto;
                
                await smtp.ConnectAsync(host, port, secureSocketOptions);
                await smtp.AuthenticateAsync(senderEmail, appPassword);
                await smtp.SendAsync(email);
                await smtp.DisconnectAsync(true);

                logEntry.Status = "Sent";
                logEntry.SentAt = DateTime.Now;
                logEntry.ErrorMessage = "";
                await db.SaveChangesAsync();
                _logger.LogInformation($"Successfully retried email {emailLogId} to {logEntry.RecipientEmail}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Retry {logEntry.RetryCount} failed for email {emailLogId}: {ex.Message}");
                logEntry.Status = "Failed";
                logEntry.ErrorMessage = ex.Message;
                await db.SaveChangesAsync();
            }
        }
    }
}
