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
    public class NotificationEmailSenderService : BackgroundService
    {
        private readonly ILogger<NotificationEmailSenderService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(30);

        public NotificationEmailSenderService(ILogger<NotificationEmailSenderService> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Notification Email Sender Service is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessUnsentNotificationsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while processing unsent notifications.");
                }

                await Task.Delay(_pollingInterval, stoppingToken);
            }

            _logger.LogInformation("Notification Email Sender Service is stopping.");
        }

        private async Task ProcessUnsentNotificationsAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

            // Fetch up to 20 unsent notifications that need to be emailed
            var unsentNotifications = await dbContext.Notifications
                .Include(n => n.User)
                .Where(n => !n.IsEmailSent)
                .OrderBy(n => n.CreatedAt)
                .Take(20)
                .ToListAsync(stoppingToken);

            if (!unsentNotifications.Any()) return;

            foreach (var notification in unsentNotifications)
            {
                if (notification.User != null && !string.IsNullOrWhiteSpace(notification.User.Email))
                {
                    try
                    {
                        // Send the email
                        await emailService.SendEmailAsync(notification.User.Email, notification.Title, notification.Message);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to send email for notification {notification.Id} to {notification.User.Email}");
                    }
                }

                // Mark as sent regardless of email success so we don't infinitely retry failed emails here.
                // The EmailService has its own retry mechanism (EmailLogs/EmailRetryService) if it fails due to SMTP issues.
                notification.IsEmailSent = true;
            }

            await dbContext.SaveChangesAsync(stoppingToken);
        }
    }
}
