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
    public class AutomatedReportsService : BackgroundService
    {
        private readonly ILogger<AutomatedReportsService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private DateTime _lastDailyRun = DateTime.MinValue;
        private DateTime _lastMonthlyRun = DateTime.MinValue;

        public AutomatedReportsService(ILogger<AutomatedReportsService> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Automated Reports Background Service is starting.");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var now = DateTime.Now;

                        // Daily runs at 18:00 (6 PM)
                        if (now.Hour >= 18 && _lastDailyRun.Date != now.Date)
                        {
                            await RunDailyReportsAsync();
                            _lastDailyRun = now;
                        }

                        // Monthly runs on the 1st day of the month at 08:00 AM
                        if (now.Day == 1 && now.Hour >= 8 && _lastMonthlyRun.Month != now.Month)
                        {
                            await RunMonthlyReportsAsync();
                            _lastMonthlyRun = now;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error occurred executing AutomatedReportsService.");
                    }

                    await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Automated Reports Background Service is stopping due to cancellation.");
            }
        }

        private async Task RunDailyReportsAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

            string today = DateTime.Now.ToString("yyyy-MM-dd");

            // 1. Missing Punches
            var missingPunches = await db.AttendanceLogs
                .Include(l => l.User)
                .Where(l => l.Date == today && l.Status == "Active")
                .ToListAsync();

            foreach (var log in missingPunches)
            {
                if (log.User != null && !string.IsNullOrWhiteSpace(log.User.Email))
                {
                    string body = $"<p>Hi {log.User.Name},</p><p>You have a missing punch out for today (<strong>{today}</strong>). Your clock-in time was {log.ClockIn}.</p><p>Please regularize your attendance as soon as possible.</p>";
                    await emailService.SendEmailAsync(log.User.Email, "Action Required: Missing Punch Alert", body);
                }
            }

            // 2. Daily Summary to Admins
            var admins = await db.Users.Where(u => u.Role == "admin").ToListAsync();
            int totalPresent = await db.AttendanceLogs.CountAsync(l => l.Date == today && (l.Status == "Present" || l.Status == "Active"));
            int totalLeaves = await db.AttendanceLogs.CountAsync(l => l.Date == today && l.Status == "On Leave");

            string summaryBody = $@"
                <h3>Daily Attendance Summary</h3>
                <p>Date: <strong>{today}</strong></p>
                <ul>
                    <li>Total Present: {totalPresent}</li>
                    <li>On Leave: {totalLeaves}</li>
                    <li>Missing Punches: {missingPunches.Count}</li>
                </ul>
                <p>Log in to the Admin Dashboard for more details.</p>";

            foreach (var admin in admins)
            {
                await emailService.SendEmailAsync(admin.Email, $"Daily Attendance Summary - {today}", summaryBody);
            }

            _logger.LogInformation($"Daily reports sent for {today}.");
        }

        private async Task RunMonthlyReportsAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

            var admins = await db.Users.Where(u => u.Role == "admin").ToListAsync();
            string monthStr = DateTime.Now.AddMonths(-1).ToString("MMMM yyyy");

            string summaryBody = $@"
                <h3>Monthly Attendance Report</h3>
                <p>The attendance report for <strong>{monthStr}</strong> has been generated.</p>
                <p>Please log in to the admin dashboard to view the full analytics and department-wise attendance statistics.</p>";

            foreach (var admin in admins)
            {
                await emailService.SendEmailAsync(admin.Email, $"Monthly Attendance Report - {monthStr}", summaryBody);
            }

            _logger.LogInformation($"Monthly report sent for {monthStr}.");
        }
    }
}
