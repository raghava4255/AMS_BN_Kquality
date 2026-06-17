using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Ams.Services
{
    public class EmailRetryService : BackgroundService
    {
        private readonly ILogger<EmailRetryService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5);

        public EmailRetryService(ILogger<EmailRetryService> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Email Retry Background Service is starting.");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await ProcessFailedEmailsAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error occurred executing EmailRetryService.");
                    }

                    await Task.Delay(_checkInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Graceful cancellation on shutdown
            }

            _logger.LogInformation("Email Retry Background Service is stopping.");
        }

        private async Task ProcessFailedEmailsAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

            // Find emails that failed and have less than 3 retries
            var failedEmails = await dbContext.EmailLogs
                .Where(e => e.Status == "Failed" && e.RetryCount < 3)
                .OrderBy(e => e.SentAt)
                .Take(50) // Batch size
                .ToListAsync();

            if (failedEmails.Any())
            {
                _logger.LogInformation($"Found {failedEmails.Count} failed emails to retry.");
                foreach (var email in failedEmails)
                {
                    await emailService.RetryEmailAsync(email.Id);
                }
            }
        }
    }
}
