using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using System.Globalization;

namespace Ams.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AttendanceController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly Ams.Services.IEmailService _emailService;

        public AttendanceController(AppDbContext context, Ams.Services.IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        public class ClockRequest
        {
            [JsonPropertyName("userId")]
            public int UserId { get; set; }

            [JsonPropertyName("clockInLat")]
            public double? ClockInLat { get; set; }

            [JsonPropertyName("clockInLng")]
            public double? ClockInLng { get; set; }

            [JsonPropertyName("clockInAddress")]
            public string ClockInAddress { get; set; } = string.Empty;

            [JsonPropertyName("clockOutLat")]
            public double? ClockOutLat { get; set; }

            [JsonPropertyName("clockOutLng")]
            public double? ClockOutLng { get; set; }

            [JsonPropertyName("clockOutAddress")]
            public string ClockOutAddress { get; set; } = string.Empty;
        }

        // Parses time strings in both "h:mm tt" and "hh:mm tt" formats
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

        private static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            var R = 6371e3; // metres
            var p1 = lat1 * Math.PI / 180; // radians
            var p2 = lat2 * Math.PI / 180;
            var dp = (lat2 - lat1) * Math.PI / 180;
            var dl = (lon2 - lon1) * Math.PI / 180;

            var a = Math.Sin(dp / 2) * Math.Sin(dp / 2) +
                    Math.Cos(p1) * Math.Cos(p2) *
                    Math.Sin(dl / 2) * Math.Sin(dl / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return R * c; // in metres
        }

        [HttpPost("clock-in")]
        public async Task<IActionResult> ClockIn([FromBody] ClockRequest request)
        {
            if (request == null || request.UserId <= 0)
            {
                return BadRequest(new { error = "Invalid User ID." });
            }

            var user = await _context.Users
                .Include(u => u.Shift)
                .FirstOrDefaultAsync(u => u.Id == request.UserId);

            if (user == null)
            {
                return NotFound(new { error = "User not found." });
            }

            if (user.AllowedLat.HasValue && user.AllowedLng.HasValue)
            {
                if (!request.ClockInLat.HasValue || !request.ClockInLng.HasValue)
                {
                    return BadRequest(new { error = "Location is required for your account." });
                }
                
                var distance = CalculateDistance(user.AllowedLat.Value, user.AllowedLng.Value, request.ClockInLat.Value, request.ClockInLng.Value);
                if (distance > user.AllowedRadius)
                {
                    return BadRequest(new { error = $"You are {Math.Round(distance)}m away from your allowed location. Must be within {user.AllowedRadius}m." });
                }
            }

            string todayStr = DateTime.Now.ToString("yyyy-MM-dd");

            // Check if already clocked in today (active, already checked out, or on leave)
            var existingLog = await _context.AttendanceLogs
                .FirstOrDefaultAsync(l => l.UserId == request.UserId && l.Date == todayStr);

            if (existingLog != null)
            {
                return BadRequest(new { error = "You have already clocked in for today." });
            }

            // Use 24-hour hour format (e.g. "09:05")
            string clockInTime = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);

            bool isLate = false;
            if (user.Shift != null && TryParseTime(user.Shift.StartTime, out DateTime shiftStart) && TryParseTime(clockInTime, out DateTime parsedClockIn))
            {
                // Align dates
                shiftStart = new DateTime(parsedClockIn.Year, parsedClockIn.Month, parsedClockIn.Day, shiftStart.Hour, shiftStart.Minute, 0);
                if (parsedClockIn > shiftStart.AddMinutes(user.Shift.GraceTime))
                {
                    isLate = true;
                }
            }

            var log = new AttendanceLog
            {
                UserId = request.UserId,
                Date = todayStr,
                ClockIn = clockInTime,
                ClockOut = "---",
                Status = "Active",
                Hours = 0.0,
                LateEntry = isLate,
                ClockInLat = request.ClockInLat,
                ClockInLng = request.ClockInLng,
                ClockInAddress = request.ClockInAddress ?? string.Empty
            };

            _context.AttendanceLogs.Add(log);
            
            var notification = new Notification
            {
                UserId = request.UserId,
                Title = "Clock In Successful",
                Message = $"You clocked in successfully at {clockInTime}.",
                Type = "success"
            };
            _context.Notifications.Add(notification);

            await _context.SaveChangesAsync();

            return Ok(new { message = "Clock-in successful!", log = log });
        }

        [HttpPost("clock-out")]
        public async Task<IActionResult> ClockOut([FromBody] ClockRequest request)
        {
            if (request == null || request.UserId <= 0)
            {
                return BadRequest(new { error = "Invalid User ID." });
            }

            // Find current active clock-in log in database
            var log = await _context.AttendanceLogs
                .Include(l => l.User)
                .ThenInclude(u => u!.Shift)
                .FirstOrDefaultAsync(l => l.UserId == request.UserId && l.Status == "Active");

            if (log == null)
            {
                return BadRequest(new { error = "No active clock-in session found." });
            }

            var user = log.User;
            if (user != null && user.AllowedLat.HasValue && user.AllowedLng.HasValue)
            {
                if (!request.ClockOutLat.HasValue || !request.ClockOutLng.HasValue)
                {
                    return BadRequest(new { error = "Location is required for your account." });
                }
                
                var distance = CalculateDistance(user.AllowedLat.Value, user.AllowedLng.Value, request.ClockOutLat.Value, request.ClockOutLng.Value);
                if (distance > user.AllowedRadius)
                {
                    return BadRequest(new { error = $"You are {Math.Round(distance)}m away from your allowed location. Must be within {user.AllowedRadius}m." });
                }
            }

            string clockOutTime = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);

            // Accurate work hours calculation — supports multiple time formats + midnight crossing
            double actualHours = 0.0;
            double overtimeHours = 0.0;
            double pendingHours = 0.0;
            bool isEarly = false;
            double breakDuration = 0.0;

            if (TryParseTime(log.ClockIn, out DateTime parsedClockIn) &&
                TryParseTime(clockOutTime, out DateTime parsedClockOut))
            {
                // If clock-out is before clock-in, employee worked past midnight — add 1 day
                if (parsedClockOut < parsedClockIn)
                {
                    parsedClockOut = parsedClockOut.AddDays(1);
                }

                TimeSpan duration = parsedClockOut - parsedClockIn;
                actualHours = Math.Round(duration.TotalHours, 2);

                if (user?.Shift != null)
                {
                    double shiftBreak = Math.Round(user.Shift.BreakTime / 60.0, 2);
                    if (actualHours > 4.0)
                    {
                        breakDuration = shiftBreak;
                        actualHours -= breakDuration;
                    }
                    else
                    {
                        breakDuration = 0.0;
                    }
                    if (actualHours < 0) actualHours = 0;

                    if (TryParseTime(user.Shift.EndTime, out DateTime shiftEnd) && TryParseTime(user.Shift.StartTime, out DateTime shiftStart))
                    {
                        shiftStart = new DateTime(parsedClockIn.Year, parsedClockIn.Month, parsedClockIn.Day, shiftStart.Hour, shiftStart.Minute, 0);
                        shiftEnd = new DateTime(parsedClockIn.Year, parsedClockIn.Month, parsedClockIn.Day, shiftEnd.Hour, shiftEnd.Minute, 0);
                        
                        if (shiftEnd < shiftStart) shiftEnd = shiftEnd.AddDays(1);

                        double requiredHours = (shiftEnd - shiftStart).TotalHours - shiftBreak;
                        if (requiredHours < 0) requiredHours = 0;

                        if (actualHours > requiredHours)
                        {
                            overtimeHours = Math.Round(actualHours - requiredHours, 2);
                        }
                        else
                        {
                            pendingHours = Math.Round(requiredHours - actualHours, 2);
                        }

                        // Align clock out date with shift end date for early exit check
                        DateTime adjustedClockOut = new DateTime(parsedClockOut.Year, parsedClockOut.Month, parsedClockOut.Day, parsedClockOut.Hour, parsedClockOut.Minute, 0);
                        if (adjustedClockOut < shiftEnd)
                        {
                            isEarly = true;
                        }
                    }
                }
                else
                {
                    if (actualHours < 0) actualHours = 0;
                }
            }

            log.ClockOut = clockOutTime;
            log.Status = "Present";
            log.Hours = actualHours;
            log.EarlyExit = isEarly;
            log.OvertimeHours = overtimeHours;
            log.PendingHours = pendingHours;
            log.BreakDuration = breakDuration;
            log.ClockOutLat = request.ClockOutLat;
            log.ClockOutLng = request.ClockOutLng;
            log.ClockOutAddress = request.ClockOutAddress ?? string.Empty;

            // Increment employee metrics in SQL Server
            if (user != null)
            {
                user.PresentDays += 1;
                user.WorkHoursThisMonth = Math.Round(user.WorkHoursThisMonth + actualHours, 2);

                // Recompute attendance rate
                int totalDays = user.PresentDays + user.AbsentDays;
                if (totalDays > 0)
                {
                    user.AttendanceRate = Math.Round(((double)user.PresentDays / totalDays) * 100.0, 1);
                }
            }

            var notification = new Notification
            {
                UserId = request.UserId,
                Title = "Clock Out Successful",
                Message = $"You clocked out successfully at {clockOutTime}. Worked {actualHours} hours.",
                Type = "success"
            };
            _context.Notifications.Add(notification);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Clock-out successful!",
                log = new
                {
                    id = log.Id,
                    date = log.Date,
                    clockIn = log.ClockIn,
                    clockOut = log.ClockOut,
                    status = log.Status,
                    hours = log.Hours,
                    lateEntry = log.LateEntry,
                    earlyExit = log.EarlyExit,
                    clockInLat = log.ClockInLat,
                    clockInLng = log.ClockInLng,
                    clockInAddress = log.ClockInAddress,
                    clockOutLat = log.ClockOutLat,
                    clockOutLng = log.ClockOutLng,
                    clockOutAddress = log.ClockOutAddress
                },
                user = user == null ? null : new
                {
                    presentDays = user.PresentDays,
                    workHoursThisMonth = user.WorkHoursThisMonth,
                    attendanceRate = user.AttendanceRate
                }
            });
        }

        [HttpGet("history")]
        public async Task<IActionResult> GetAttendanceHistory([FromQuery] int? managerId)
        {
            IQueryable<AttendanceLog> query = _context.AttendanceLogs.AsNoTracking()
                .Include(l => l.User)
                .ThenInclude(u => u!.Shift);

            if (managerId.HasValue && managerId.Value > 0)
            {
                var manager = await _context.Users.FindAsync(managerId.Value);
                if (manager != null && !string.IsNullOrEmpty(manager.Department))
                {
                    var rawDepts = manager.Department.Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries).Select(d => d.Trim()).ToList();
                    var managerDepts = new List<string>();
                    foreach (var d in rawDepts)
                    {
                        managerDepts.Add(d);
                        managerDepts.Add(d.ToLower());
                        managerDepts.Add(d.ToUpper());
                    }
                    managerDepts = managerDepts.Distinct().ToList();

                    query = query.Where(l => l.User != null && managerDepts.Contains(l.User.Department) && l.User.Role != null && l.User.Role.Contains("employee"));
                }
            }

            var logs = await query.OrderByDescending(l => l.Date)
                .Select(l => new {
                    id = l.Id,
                    userId = l.UserId,
                    userName = l.User != null ? l.User.Name : "Unknown",
                    department = l.User != null ? l.User.Department : "Unknown",
                    date = l.Date,
                    clockIn = l.ClockIn,
                    clockOut = l.ClockOut,
                    status = l.Status,
                    hours = l.Hours,
                    lateEntry = l.LateEntry,
                    earlyExit = l.EarlyExit,
                    overtimeHours = l.OvertimeHours,
                    pendingHours = l.PendingHours,
                    breakDuration = l.BreakDuration,
                    shiftName = l.User != null && l.User.Shift != null ? l.User.Shift.Name : "None",
                    shiftStart = l.User != null && l.User.Shift != null ? l.User.Shift.StartTime : "",
                    shiftEnd = l.User != null && l.User.Shift != null ? l.User.Shift.EndTime : "",
                    clockInLat = l.ClockInLat,
                    clockInLng = l.ClockInLng,
                    clockInAddress = l.ClockInAddress,
                    clockOutLat = l.ClockOutLat,
                    clockOutLng = l.ClockOutLng,
                    clockOutAddress = l.ClockOutAddress
                })
                .ToListAsync();

            return Ok(logs);
        }

        public class ResetRequest
        {
            [JsonPropertyName("userId")]
            public int UserId { get; set; }
        }

        public class UpdateLogRequest
        {
            [JsonPropertyName("id")]
            public int Id { get; set; }

            [JsonPropertyName("clockIn")]
            public string ClockIn { get; set; } = string.Empty;

            [JsonPropertyName("clockOut")]
            public string ClockOut { get; set; } = string.Empty;

            [JsonPropertyName("clockInLat")]
            public double? ClockInLat { get; set; }

            [JsonPropertyName("clockInLng")]
            public double? ClockInLng { get; set; }

            [JsonPropertyName("clockInAddress")]
            public string ClockInAddress { get; set; } = string.Empty;

            [JsonPropertyName("clockOutLat")]
            public double? ClockOutLat { get; set; }

            [JsonPropertyName("clockOutLng")]
            public double? ClockOutLng { get; set; }

            [JsonPropertyName("clockOutAddress")]
            public string ClockOutAddress { get; set; } = string.Empty;
        }

        [HttpPut("update")]
        public async Task<IActionResult> UpdateAttendanceLog([FromBody] UpdateLogRequest request)
        {
            var log = await _context.AttendanceLogs
                .Include(l => l.User)
                .ThenInclude(u => u!.Shift)
                .FirstOrDefaultAsync(l => l.Id == request.Id);

            if (log == null)
            {
                return NotFound(new { error = "Attendance log not found." });
            }

            var user = log.User;
            
            // Validate time format
            if (!TryParseTime(request.ClockIn, out DateTime parsedClockIn) || !TryParseTime(request.ClockOut, out DateTime parsedClockOut))
            {
                return BadRequest(new { error = "Invalid time format. Please use 'HH:mm' format (e.g. 15:45)." });
            }

            // Reverse the old hours from user metrics
            if (user != null && log.Status == "Present")
            {
                user.WorkHoursThisMonth = Math.Max(0, user.WorkHoursThisMonth - log.Hours);
            }

            // If clock-out is before clock-in, assume next day
            if (parsedClockOut < parsedClockIn)
            {
                parsedClockOut = parsedClockOut.AddDays(1);
            }

            TimeSpan duration = parsedClockOut - parsedClockIn;
            double actualHours = Math.Round(duration.TotalHours, 2);

            double overtimeHours = 0.0;
            double pendingHours = 0.0;
            bool isEarly = false;
            bool isLate = false;
            double breakDuration = 0.0;

            if (user?.Shift != null)
            {
                // Re-evaluate late entry
                if (TryParseTime(user.Shift.StartTime, out DateTime shiftStart))
                {
                    shiftStart = new DateTime(parsedClockIn.Year, parsedClockIn.Month, parsedClockIn.Day, shiftStart.Hour, shiftStart.Minute, 0);
                    if (parsedClockIn > shiftStart.AddMinutes(user.Shift.GraceTime))
                    {
                        isLate = true;
                    }
                }

                double shiftBreak = Math.Round(user.Shift.BreakTime / 60.0, 2);
                if (actualHours > 4.0)
                {
                    breakDuration = shiftBreak;
                    actualHours -= breakDuration;
                }
                else
                {
                    breakDuration = 0.0;
                }

                if (TryParseTime(user.Shift.EndTime, out DateTime shiftEnd) && TryParseTime(user.Shift.StartTime, out DateTime shiftStart2))
                {
                    shiftStart2 = new DateTime(parsedClockIn.Year, parsedClockIn.Month, parsedClockIn.Day, shiftStart2.Hour, shiftStart2.Minute, 0);
                    shiftEnd = new DateTime(parsedClockIn.Year, parsedClockIn.Month, parsedClockIn.Day, shiftEnd.Hour, shiftEnd.Minute, 0);
                    
                    if (shiftEnd < shiftStart2) shiftEnd = shiftEnd.AddDays(1);

                    double requiredHours = (shiftEnd - shiftStart2).TotalHours - shiftBreak;
                    if (requiredHours < 0) requiredHours = 0;

                    if (actualHours > requiredHours)
                    {
                        overtimeHours = Math.Round(actualHours - requiredHours, 2);
                    }
                    else
                    {
                        pendingHours = Math.Round(requiredHours - actualHours, 2);
                    }

                    DateTime adjustedClockOut = new DateTime(parsedClockOut.Year, parsedClockOut.Month, parsedClockOut.Day, parsedClockOut.Hour, parsedClockOut.Minute, 0);
                    if (adjustedClockOut < shiftEnd)
                    {
                        isEarly = true;
                    }
                }
            }

            // Apply new calculations
            log.ClockIn = parsedClockIn.ToString("HH:mm", CultureInfo.InvariantCulture);
            log.ClockOut = parsedClockOut.ToString("HH:mm", CultureInfo.InvariantCulture);
            log.Hours = actualHours;
            log.LateEntry = isLate;
            log.EarlyExit = isEarly;
            log.OvertimeHours = overtimeHours;
            log.PendingHours = pendingHours;
            log.BreakDuration = breakDuration;
            
            // Allow admin to update locations manually via the Update endpoint if provided
            if (request.ClockInLat.HasValue) log.ClockInLat = request.ClockInLat;
            if (request.ClockInLng.HasValue) log.ClockInLng = request.ClockInLng;
            if (!string.IsNullOrEmpty(request.ClockInAddress)) log.ClockInAddress = request.ClockInAddress;
            
            if (request.ClockOutLat.HasValue) log.ClockOutLat = request.ClockOutLat;
            if (request.ClockOutLng.HasValue) log.ClockOutLng = request.ClockOutLng;
            if (!string.IsNullOrEmpty(request.ClockOutAddress)) log.ClockOutAddress = request.ClockOutAddress;
            
            // If it was Active, it's now Present since it has a clockout
            if (log.Status == "Active") log.Status = "Present";

            // Add new hours to user metrics
            if (user != null && log.Status == "Present")
            {
                user.WorkHoursThisMonth = Math.Round(user.WorkHoursThisMonth + actualHours, 2);
            }

            await _context.SaveChangesAsync();

            // Notify Employee
            if (user != null && !string.IsNullOrWhiteSpace(user.Email))
            {
                string reqBody = $"<p>Hi {user.Name},</p><p>Your attendance log for <strong>{log.Date}</strong> has been regularized by an administrator.</p><p>New Clock In: {log.ClockIn}<br/>New Clock Out: {log.ClockOut}<br/>Calculated Hours: {log.Hours}</p>";
                _ = _emailService.SendEmailAsync(user.Email, "Attendance Regularization Update", reqBody);
            }

            return Ok(new { message = "Attendance log updated successfully.", log = log });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAttendanceLog(int id)
        {
            var log = await _context.AttendanceLogs.FindAsync(id);
            if (log == null)
            {
                return NotFound(new { error = "Attendance log not found." });
            }

            var user = await _context.Users.FindAsync(log.UserId);
            if (user != null && log.Status == "Present")
            {
                user.PresentDays = Math.Max(0, user.PresentDays - 1);
                user.WorkHoursThisMonth = Math.Max(0, user.WorkHoursThisMonth - log.Hours);
                
                int totalDays = user.PresentDays + user.AbsentDays;
                if (totalDays > 0)
                {
                    user.AttendanceRate = Math.Round(((double)user.PresentDays / totalDays) * 100.0, 1);
                }
                else 
                {
                    user.AttendanceRate = 100;
                }
            }

            _context.AttendanceLogs.Remove(log);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Attendance log deleted successfully." });
        }

        [HttpPost("reset")]
        public async Task<IActionResult> ResetAttendance([FromBody] ResetRequest request)
        {
            string todayStr = DateTime.Now.ToString("yyyy-MM-dd");
            var logs = await _context.AttendanceLogs
                .Where(l => l.UserId == request.UserId && l.Date == todayStr)
                .ToListAsync();

            if (logs.Any())
            {
                var user = await _context.Users.FindAsync(request.UserId);
                if (user != null)
                {
                    foreach (var log in logs)
                    {
                        if (log.Status == "Present")
                        {
                            user.PresentDays = Math.Max(0, user.PresentDays - 1);
                            user.WorkHoursThisMonth = Math.Max(0, user.WorkHoursThisMonth - log.Hours);
                        }
                    }
                    
                    int totalDays = user.PresentDays + user.AbsentDays;
                    if (totalDays > 0)
                    {
                        user.AttendanceRate = Math.Round(((double)user.PresentDays / totalDays) * 100.0, 1);
                    }
                    else 
                    {
                        user.AttendanceRate = 100;
                    }
                }

                _context.AttendanceLogs.RemoveRange(logs);
                await _context.SaveChangesAsync();
                return Ok(new { message = "Attendance reset successfully." });
            }
            
            return Ok(new { message = "No attendance logs found for today to reset." });
        }
    }
}
