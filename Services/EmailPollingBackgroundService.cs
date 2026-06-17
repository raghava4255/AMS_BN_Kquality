using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Ams.Services
{
    public class EmailPollingBackgroundService : BackgroundService
    {
        private readonly ILogger<EmailPollingBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _pollingInterval = TimeSpan.FromMinutes(2); // 2 minutes

        public EmailPollingBackgroundService(ILogger<EmailPollingBackgroundService> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Email Polling Background Service is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PollEmailsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while polling emails.");
                }

                // Wait before next poll
                await Task.Delay(_pollingInterval, stoppingToken);
            }

            _logger.LogInformation("Email Polling Background Service is stopping.");
        }

        private async Task PollEmailsAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var imapService = scope.ServiceProvider.GetRequiredService<ImapEmailService>();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var newEmails = await imapService.FetchUnreadEmailsAsync(stoppingToken);

            if (newEmails.Count == 0) return;

            _logger.LogInformation($"Fetched {newEmails.Count} new emails from IMAP server.");

            var admins = await dbContext.Users
                .Where(u => u.Role != null && u.Role.ToLower().Contains("admin"))
                .ToListAsync(stoppingToken);

            foreach (var email in newEmails)
            {
                // Check if email is already in database to prevent duplicates
                bool exists = await dbContext.IncomingEmails.AnyAsync(e => e.MessageId == email.MessageId, stoppingToken);
                
                if (!exists)
                {
                    email.IsProcessed = true;
                    dbContext.IncomingEmails.Add(email);

                    // Create notifications for Admins
                    foreach (var admin in admins)
                    {
                        var notification = new Notification
                        {
                            UserId = admin.Id,
                            Title = $"New Email: {email.Subject}",
                            Message = $"From: {email.SenderAddress}\n\n{(email.BodyText.Length > 200 ? email.BodyText.Substring(0, 200) + "..." : email.BodyText)}",
                            Type = "info",
                            CreatedAt = DateTime.UtcNow,
                            IsRead = false
                        };
                        dbContext.Notifications.Add(notification);
                    }
                }
            }

            await dbContext.SaveChangesAsync(stoppingToken);
        }
    }
}
