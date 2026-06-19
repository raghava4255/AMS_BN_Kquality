using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Ams.Services
{
    public class ShiftPunchMonitoringService : BackgroundService
    {
        private readonly ILogger<ShiftPunchMonitoringService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public ShiftPunchMonitoringService(ILogger<ShiftPunchMonitoringService> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Shift Punch Monitoring Service is starting.");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await MonitorShiftPunchesAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error occurred executing ShiftPunchMonitoringService task.");
                    }

                    // Check every 1 minute
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Shift Punch Monitoring Service is stopping due to cancellation.");
            }
        }

        private async Task MonitorShiftPunchesAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

            // Fetch active logs (Status == "Active")
            var activeLogs = await db.AttendanceLogs
                .Include(l => l.User)
                .ThenInclude(u => u!.Shift)
                .Where(l => l.Status == "Active")
                .ToListAsync();

            var now = DateTime.Now;

            foreach (var log in activeLogs)
            {
                var user = log.User;
                if (user == null) continue;

                // Fallback to default shift (09:00 to 18:00) if no shift assigned
                var shift = user.Shift;
                string shiftStartTime = shift?.StartTime ?? "09:00";
                string shiftEndTime = shift?.EndTime ?? "18:00";

                if (!DateTime.TryParseExact(log.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime logDate))
                {
                    _logger.LogWarning($"Attendance log {log.Id} has invalid Date format: '{log.Date}'");
                    continue;
                }

                if (!TryParseTime(shiftStartTime, out DateTime startTime) || !TryParseTime(shiftEndTime, out DateTime endTime))
                {
                    _logger.LogWarning($"Shift time format invalid for log {log.Id}");
                    continue;
                }

                // Align shift times to the calendar day of the log
                var shiftStartDateTime = new DateTime(logDate.Year, logDate.Month, logDate.Day, startTime.Hour, startTime.Minute, 0);
                var shiftEndDateTime = new DateTime(logDate.Year, logDate.Month, logDate.Day, endTime.Hour, endTime.Minute, 0);

                // Midnight crossing support
                if (shiftEndDateTime < shiftStartDateTime)
                {
                    shiftEndDateTime = shiftEndDateTime.AddDays(1);
                }

                // 1. Check if 3 hours have passed since shift end
                if (now >= shiftEndDateTime.AddHours(3))
                {
                    _logger.LogInformation($"Auto-resolving active log {log.Id} for user {user.Name} as ABSENT (3h past shift end).");

                    log.Status = "Absent";
                    log.Hours = 0.0;
                    log.ClockOut = "---";

                    user.AbsentDays += 1;

                    // Recompute attendance rate
                    int totalDays = user.PresentDays + user.AbsentDays;
                    if (totalDays > 0)
                    {
                        user.AttendanceRate = Math.Round(((double)user.PresentDays / totalDays) * 100.0, 1);
                    }
                    else
                    {
                        user.AttendanceRate = 100;
                    }

                    // Create database notification
                    var notification = new Notification
                    {
                        UserId = user.Id,
                        Title = "Missed Punch-Out: Marked Absent",
                        Message = $"Your active session on {log.Date} has been auto-resolved as Absent because no punch-out was recorded within 3 hours of shift completion.",
                        Type = "danger",
                        CreatedAt = DateTime.Now
                    };
                    db.Notifications.Add(notification);

                    // Send email notification
                    string emailBody = $@"
                        <p>Hi {user.Name},</p>
                        <p>We noticed that you missed punching out for your shift on <strong>{log.Date}</strong>.</p>
                        <p>Since more than 3 hours have passed since your scheduled shift end time ({shiftEndTime}), your attendance status for this date has been auto-resolved as <strong>Absent</strong>.</p>
                        <p>Your monthly attendance metrics have been updated. If you believe this is an error, please contact your administrator or team lead to regularize your punch.</p>";

                    _ = emailService.SendEmailAsync(user.Email, "Attendance Alert: Missed Punch-Out Auto-Resolved as Absent", emailBody);
                }
                // 2. Check if 15 minutes have passed since shift end
                else if (now >= shiftEndDateTime.AddMinutes(15) && !log.MissedPunchEmailSent)
                {
                    _logger.LogInformation($"Sending missed punch warning email for log {log.Id} for user {user.Name} (15m past shift end).");

                    log.MissedPunchEmailSent = true;

                    // Create warning notification
                    var notification = new Notification
                    {
                        UserId = user.Id,
                        Title = "Missed Punch-Out Warning",
                        Message = $"Your shift ended at {shiftEndTime}. Please remember to punch out to record your working hours.",
                        Type = "warning",
                        CreatedAt = DateTime.Now
                    };
                    db.Notifications.Add(notification);

                    // Send warning email
                    string emailBody = $@"
                        <p>Hi {user.Name},</p>
                        <p>Your shift ended at <strong>{shiftEndTime}</strong>, but you have not yet punched out.</p>
                        <p>Please log in to the Attendance portal and punch out as soon as possible to ensure your work hours are recorded accurately.</p>
                        <p><em>Note: If you do not punch out within 3 hours of your shift completion, this session will be automatically recorded as <strong>Absent</strong>.</em></p>";

                    _ = emailService.SendEmailAsync(user.Email, "Action Required: Missed Punch-Out Alert", emailBody);
                }
            }

            await db.SaveChangesAsync();
        }

        private static bool TryParseTime(string timeStr, out DateTime result)
        {
            string[] formats = { "h:mm tt", "hh:mm tt", "H:mm", "HH:mm" };
            return DateTime.TryParseExact(
                timeStr,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out result
            );
        }
    }
}
