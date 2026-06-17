using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Ams.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly Ams.Services.IEmailService _emailService;

        public AuthController(AppDbContext context, Ams.Services.IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        public class LoginRequest
        {
            [JsonPropertyName("email")]
            public string Email { get; set; } = string.Empty;

            [JsonPropertyName("password")]
            public string Password { get; set; } = string.Empty;

            [JsonPropertyName("role")]
            public string Role { get; set; } = string.Empty;
        }

        public class RegisterRequest
        {
            [JsonPropertyName("email")]
            public string Email { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("role")]
            public string Role { get; set; } = "employee";

            [JsonPropertyName("department")]
            public string Department { get; set; } = "Engineering";

            [JsonPropertyName("password")]
            public string Password { get; set; } = string.Empty;

            [JsonPropertyName("shiftId")]
            public int? ShiftId { get; set; }

            [JsonPropertyName("employeeId")]
            public string EmployeeId { get; set; } = string.Empty;

            [JsonPropertyName("allowedLat")]
            public double? AllowedLat { get; set; }

            [JsonPropertyName("allowedLng")]
            public double? AllowedLng { get; set; }

            [JsonPropertyName("allowedRadius")]
            public double AllowedRadius { get; set; } = 500.0;
        }

        public class UpdateProfileRequest
        {
            [JsonPropertyName("userId")]
            public int UserId { get; set; }

            [JsonPropertyName("avatarUrl")]
            public string AvatarUrl { get; set; } = string.Empty;

            [JsonPropertyName("oldPassword")]
            public string OldPassword { get; set; } = string.Empty;

            [JsonPropertyName("newPassword")]
            public string NewPassword { get; set; } = string.Empty;
        }
        
        public class UpdateStatusRequest
        {
            public bool IsActive { get; set; }
        }

        public class UpdateShiftRequest
        {
            public int ShiftId { get; set; }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password) || string.IsNullOrWhiteSpace(request.Role))
            {
                return BadRequest(new { error = "All fields (email, password, role) are required." });
            }

            string sanitizedEmail = request.Email.Trim().ToLower();
            
            // Query user from SQL Server
            var user = await _context.Users
                .Include(u => u.Shift)
                .FirstOrDefaultAsync(u => u.Email.ToLower() == sanitizedEmail || u.EmployeeId.ToLower() == sanitizedEmail);

            if (user == null || user.Password != request.Password)
            {
                return Unauthorized(new { error = "Invalid email or password." });
            }

            // Support comma-separated multi-roles (e.g. "employee,manager")
            var userRoles = user.Role.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Trim().ToLower()).ToList();
            if (!userRoles.Contains(request.Role.Trim().ToLower()))
            {
                return StatusCode(403, new { error = $"Unauthorized. Selected role does not match this user's account." });
            }
            // Use the requested role as the effective role for this session
            string effectiveRole = request.Role.Trim().ToLower();

            // Block unassigned accounts from logging in
            if (string.IsNullOrWhiteSpace(user.Department) ||
                string.Equals(user.Department.Trim(), "Unassigned", StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(403, new { error = "Your account has not been assigned to a department yet. Please contact your administrator." });
            }

            if (user.IsActive == false)
            {
                return StatusCode(403, new { error = "Your account has been deactivated. Please contact your administrator." });
            }

            // Generate token
            string mockToken = $"mock-jwt-token-for-{effectiveRole}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

            // Dynamically gather stats depending on role from live SQL tables
            object? specializedStats = null;

            if (effectiveRole == "employee")
            {
                var logs = await _context.AttendanceLogs.AsNoTracking()
                    .Where(l => l.UserId == user.Id)
                    .OrderByDescending(l => l.Date)
                    .ToListAsync();

                specializedStats = new
                {
                    presentDays = user.PresentDays,
                    absentDays = user.AbsentDays,
                    leaveBalance = user.LeaveBalance,
                    workHoursThisMonth = user.WorkHoursThisMonth,
                    attendanceRate = user.AttendanceRate,
                    recentActivity = logs.Select(l => new {
                        id = l.Id,
                        date = l.Date,
                        clockIn = l.ClockIn,
                        clockOut = l.ClockOut,
                        status = l.Status,
                        hours = l.Hours,
                        lateEntry = l.LateEntry,
                        earlyExit = l.EarlyExit,
                        clockInLat = l.ClockInLat,
                        clockInLng = l.ClockInLng,
                        clockInAddress = l.ClockInAddress,
                        clockOutLat = l.ClockOutLat,
                        clockOutLng = l.ClockOutLng,
                        clockOutAddress = l.ClockOutAddress
                    }).ToList()
                };
            }
            else if (effectiveRole == "manager")
            {
                var rawDepts = user.Department.Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries).Select(d => d.Trim()).ToList();
                var managerDepts = new List<string>();
                foreach (var d in rawDepts)
                {
                    managerDepts.Add(d);
                    managerDepts.Add(d.ToLower());
                    managerDepts.Add(d.ToUpper());
                }
                managerDepts = managerDepts.Distinct().ToList();

                string todayString = DateTime.Now.ToString("yyyy-MM-dd");

                int teamCount = await _context.Users.CountAsync(u => u.Role != null && u.Role.Contains("employee") && managerDepts.Contains(u.Department));
                int presentCount = await _context.AttendanceLogs.CountAsync(l => l.Date == todayString && (l.Status == "Present" || l.Status == "Active") && l.User != null && managerDepts.Contains(l.User.Department));
                int onLeaveCount = await _context.AttendanceLogs.CountAsync(l => l.Date == todayString && l.Status == "On Leave" && l.User != null && managerDepts.Contains(l.User.Department));
                int absentCount = teamCount - presentCount - onLeaveCount;
                if (absentCount < 0) absentCount = 0;
                
                var pendingRequests = await _context.LeaveRequests.AsNoTracking()
                    .Where(r => r.Status == "Pending")
                    .Include(r => r.User)
                    .Where(r => r.User != null && managerDepts.Contains(r.User.Department))
                    .Select(r => new {
                        id = r.Id,
                        name = r.Name,
                        type = r.Type,
                        duration = r.Duration,
                        reason = r.Reason,
                        status = r.Status
                    }).ToListAsync();

                specializedStats = new
                {
                    teamSize = teamCount,
                    presentToday = presentCount,
                    onLeaveToday = onLeaveCount,
                    absentToday = absentCount,
                    pendingLeavesCount = pendingRequests.Count,
                    teamAttendanceRate = teamCount > 0 ? Math.Round(((double)presentCount / teamCount) * 100) : 100,
                    pendingRequests = pendingRequests
                };
            }
            else if (effectiveRole == "admin")
            {
                int totalEmployees = await _context.Users.CountAsync();
                
                var deptStatsRaw = await _context.Users
                    .Where(u => u.Department != null && u.Department != "")
                    .GroupBy(u => u.Department)
                    .Select(g => new {
                        Name = g.Key,
                        Count = g.Count(),
                        AvgAtt = g.Average(u => (double?)u.AttendanceRate) ?? 0
                    })
                    .ToListAsync();

                var dynamicDeptStats = deptStatsRaw.Select(d => new {
                    name = d.Name,
                    count = d.Count,
                    attendance = $"{Math.Round(d.AvgAtt, 1)}%"
                }).ToList();

                int activeShiftsCount = await _context.Shifts.CountAsync(s => s.Status == "Active");

                var dynamicAlerts = new List<object>();
                int currentAlertId = 1;
                string todayString = DateTime.Now.ToString("yyyy-MM-dd");

                var pendingLeavesAdmin = await _context.LeaveRequests.CountAsync(r => r.Status == "Pending");
                if (pendingLeavesAdmin > 0)
                {
                    dynamicAlerts.Add(new { id = currentAlertId++, type = "warning", message = $"{pendingLeavesAdmin} leave request(s) are awaiting approval.", time = DateTime.Now.ToString("HH:mm") });
                }

                var pendingFlexyAdmin = await _context.FlexyHourRequests.CountAsync(r => r.Status == "Pending");
                if (pendingFlexyAdmin > 0)
                {
                    dynamicAlerts.Add(new { id = currentAlertId++, type = "warning", message = $"{pendingFlexyAdmin} flexy hour request(s) are pending.", time = DateTime.Now.ToString("HH:mm") });
                }

                var todayAbsences = await _context.AttendanceLogs.CountAsync(l => l.Date == todayString && (l.Status == "Absent" || l.Status == "On Leave"));
                if (todayAbsences > 0)
                {
                    dynamicAlerts.Add(new { id = currentAlertId++, type = "info", message = $"{todayAbsences} employee(s) are absent or on leave today.", time = DateTime.Now.ToString("HH:mm") });
                }

                if (dynamicAlerts.Count == 0)
                {
                    dynamicAlerts.Add(new { id = currentAlertId++, type = "info", message = "System running smoothly. No new alerts.", time = DateTime.Now.ToString("HH:mm") });
                }

                double avgHours = totalEmployees > 0 ? await _context.Users.AverageAsync(u => (double?)u.WorkHoursThisMonth) ?? 0 : 0;
                double avgAttendance = totalEmployees > 0 ? await _context.Users.AverageAsync(u => (double?)u.AttendanceRate) ?? 0 : 0;

                int totalEmployeesCount = await _context.Users.CountAsync(u => u.Role != null && u.Role.Contains("employee"));
                int activeEmployeesCount = await _context.Users.CountAsync(u => u.Role != null && u.Role.Contains("employee") && u.IsActive);
                int inactiveEmployeesCount = await _context.Users.CountAsync(u => u.Role != null && u.Role.Contains("employee") && !u.IsActive);

                var todayLogs = await _context.AttendanceLogs.AsNoTracking().Where(l => l.Date == todayString).ToListAsync();
                var activeEmployeesList = await _context.Users.AsNoTracking().Where(u => u.Role != null && u.Role.Contains("employee") && u.IsActive).ToListAsync();

                int presentToday = todayLogs.Count(l => l.Status == "Present" || l.Status == "Active");
                int absentToday = activeEmployeesList.Count(e => !todayLogs.Any(l => l.UserId == e.Id));
                int lateToday = todayLogs.Count(l => l.LateEntry);
                int earlyToday = todayLogs.Count(l => l.EarlyExit);
                int missingPunchesToday = todayLogs.Count(l => l.Status == "Active" || l.ClockOut == "---");

                var last7Days = Enumerable.Range(0, 7)
                    .Select(i => DateTime.Now.AddDays(-i).ToString("yyyy-MM-dd"))
                    .Reverse()
                    .ToList();
                
                var weeklyTrend = new List<object>();
                foreach (var date in last7Days)
                {
                    var logsForDay = await _context.AttendanceLogs.AsNoTracking().Where(l => l.Date == date).ToListAsync();
                    int dayPresent = logsForDay.Count(l => l.Status == "Present" || l.Status == "Active");
                    int dayLate = logsForDay.Count(l => l.LateEntry);
                    weeklyTrend.Add(new {
                        date = DateTime.Parse(date).ToString("dd MMM"),
                        present = dayPresent,
                        late = dayLate
                    });
                }

                int monthlyPresent = activeEmployeesList.Sum(e => e.PresentDays);
                int monthlyAbsent = activeEmployeesList.Sum(e => e.AbsentDays);
                int monthlyOnLeave = await _context.LeaveRequests.CountAsync(r => r.Status == "Approved");

                specializedStats = new
                {
                    totalEmployees = totalEmployees,
                    activeShifts = activeShiftsCount,
                    avgWorkingHours = Math.Round(avgHours, 1),
                    overallAttendanceRate = Math.Round(avgAttendance, 1),
                    departmentStats = dynamicDeptStats,
                    systemAlerts = dynamicAlerts,
                    empStats = new {
                        total = totalEmployeesCount,
                        active = activeEmployeesCount,
                        inactive = inactiveEmployeesCount
                    },
                    attStats = new {
                        presentToday = presentToday,
                        absentToday = absentToday,
                        lateArrivals = lateToday,
                        earlyDepartures = earlyToday,
                        missingPunches = missingPunchesToday
                    },
                    weeklyTrend = weeklyTrend,
                    monthlySummary = new {
                        present = monthlyPresent,
                        absent = monthlyAbsent,
                        onLeave = monthlyOnLeave
                    }
                };
            }

            return Ok(new
            {
                message = "Login successful",
                token = mockToken,
                user = new
                {
                    id = user.Id,
                    name = user.Name,
                    email = user.Email,
                    role = effectiveRole,
                    department = user.Department,
                    avatar = user.Avatar,
                    shift = user.Shift,
                    employeeId = user.EmployeeId,
                    stats = specializedStats
                }
            });
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { error = "Email and Name are required." });
            }

            if (string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { error = "A password must be set for each new account." });
            }

            if (string.IsNullOrWhiteSpace(request.EmployeeId))
            {
                return BadRequest(new { error = "Employee ID is required." });
            }

            // Check if user already exists by email
            var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.Trim().ToLower());
            if (existingUser != null)
            {
                return BadRequest(new { error = "Employee with this email already exists." });
            }

            // Check if user already exists by employee ID
            var existingUserById = await _context.Users.FirstOrDefaultAsync(u => u.EmployeeId.ToLower() == request.EmployeeId.Trim().ToLower());
            if (existingUserById != null)
            {
                return BadRequest(new { error = "Employee with this Employee ID already exists." });
            }

            // Create new Employee record using HR-specified password, no auto-generated avatar
            var newUser = new User
            {
                Email = request.Email.Trim(),
                Password = request.Password.Trim(),
                Name = request.Name.Trim(),
                Role = request.Role.Trim().ToLower(),
                Department = request.Department,
                Avatar = string.Empty,   // Initials avatar rendered on frontend
                PresentDays = 0,
                AbsentDays = 0,
                LeaveBalance = 15,
                WorkHoursThisMonth = 0,
                AttendanceRate = 100,
                ShiftId = request.ShiftId,
                EmployeeId = request.EmployeeId.Trim(),
                AllowedLat = request.AllowedLat,
                AllowedLng = request.AllowedLng,
                AllowedRadius = request.AllowedRadius > 0 ? request.AllowedRadius : 500.0
            };

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            // Record initial password history
            _context.PasswordHistories.Add(new PasswordHistory
            {
                UserId = newUser.Id,
                PasswordHash = newUser.Password,
                CreatedAt = DateTime.Now
            });
            await _context.SaveChangesAsync();

            // Fetch shift info for the email
            string shiftInfo = "Not Assigned";
            if (newUser.ShiftId.HasValue)
            {
                var assignedShift = await _context.Shifts.FindAsync(newUser.ShiftId.Value);
                if (assignedShift != null)
                {
                    shiftInfo = $"{assignedShift.Name} ({assignedShift.StartTime} - {assignedShift.EndTime})";
                }
            }

            // Send welcome email with credentials
            string welcomeSubject = "Welcome to the Company - Your Account is Ready";
            string welcomeBody = $@"
                <p>Hi {newUser.Name},</p>
                <p>Your employee account has been created on the Attendance Management System. Here are your credentials to log in:</p>
                <table style='width: 100%; border-collapse: collapse; margin: 16px 0;'>
                    <tr>
                        <td style='padding: 6px 0; font-weight: bold; width: 150px;'>Employee ID:</td>
                        <td style='padding: 6px 0;'>{newUser.EmployeeId}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; font-weight: bold;'>Corporate Email:</td>
                        <td style='padding: 6px 0;'>{newUser.Email}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; font-weight: bold;'>Initial Password:</td>
                        <td style='padding: 6px 0;'><code>{newUser.Password}</code></td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; font-weight: bold;'>Role:</td>
                        <td style='padding: 6px 0; text-transform: capitalize;'>{newUser.Role}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; font-weight: bold;'>Department:</td>
                        <td style='padding: 6px 0;'>{newUser.Department}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; font-weight: bold;'>Assigned Shift:</td>
                        <td style='padding: 6px 0;'>{shiftInfo}</td>
                    </tr>
                </table>
                <p>Please visit the portal and log in using either your Corporate Email or Employee ID. We recommend changing your password after your first login.</p>";

            _ = _emailService.SendEmailAsync(newUser.Email, welcomeSubject, welcomeBody);

            return Ok(new { message = "User registered successfully!", userId = newUser.Id });
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { status = "ok", service = "Employee Attendance System SQL Server API" });
        }

        [HttpPost("update-profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
        {
            var user = await _context.Users.FindAsync(request.UserId);
            if (user == null)
            {
                return NotFound(new { error = "User not found." });
            }

            // Update Avatar if provided
            if (!string.IsNullOrWhiteSpace(request.AvatarUrl))
            {
                user.Avatar = request.AvatarUrl.Trim();
            }

            // Update Password if old and new are provided
            if (!string.IsNullOrWhiteSpace(request.NewPassword))
            {
                if (user.Password != request.OldPassword)
                {
                    return BadRequest(new { error = "Incorrect old password." });
                }

                // Password Policy check
                string newPassword = request.NewPassword;
                if (newPassword.Length < 8 ||
                    !newPassword.Any(char.IsUpper) ||
                    !newPassword.Any(char.IsLower) ||
                    !newPassword.Any(char.IsDigit) ||
                    !newPassword.Any(ch => !char.IsLetterOrDigit(ch)))
                {
                    return BadRequest(new { error = "Password must be at least 8 characters long and contain at least one uppercase letter, one lowercase letter, one number, and one special character." });
                }

                // Password History check (last 3 passwords)
                var pastPasswords = await _context.PasswordHistories
                    .Where(ph => ph.UserId == user.Id)
                    .OrderByDescending(ph => ph.CreatedAt)
                    .Take(3)
                    .Select(ph => ph.PasswordHash)
                    .ToListAsync();

                if (pastPasswords.Contains(newPassword))
                {
                    return BadRequest(new { error = "You cannot reuse any of your last 3 passwords." });
                }

                user.Password = newPassword;

                // Save to history
                _context.PasswordHistories.Add(new PasswordHistory
                {
                    UserId = user.Id,
                    PasswordHash = newPassword,
                    CreatedAt = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();

            return Ok(new { 
                message = "Profile updated successfully!", 
                avatar = user.Avatar 
            });
        }
        
        [HttpGet("admin-stats")]
        public async Task<IActionResult> GetAdminStats()
        {
            int totalEmployees = await _context.Users.CountAsync();
            
            var deptStatsRaw = await _context.Users
                .Where(u => u.Department != null && u.Department != "")
                .GroupBy(u => u.Department)
                .Select(g => new {
                    Name = g.Key,
                    Count = g.Count(),
                    AvgAtt = g.Average(u => (double?)u.AttendanceRate) ?? 0
                })
                .ToListAsync();

            var dynamicDeptStats = deptStatsRaw.Select(d => new {
                name = d.Name,
                count = d.Count,
                attendance = $"{Math.Round(d.AvgAtt, 1)}%"
            }).ToList();

            int activeShiftsCount = await _context.Shifts.CountAsync(s => s.Status == "Active");

            var dynamicAlerts = new List<object>();
            int currentAlertId = 1;
            string todayString = DateTime.Now.ToString("yyyy-MM-dd");

            var pendingLeavesAdmin = await _context.LeaveRequests.CountAsync(r => r.Status == "Pending");
            if (pendingLeavesAdmin > 0)
            {
                dynamicAlerts.Add(new { id = currentAlertId++, type = "warning", message = $"{pendingLeavesAdmin} leave request(s) are awaiting approval.", time = DateTime.Now.ToString("HH:mm") });
            }

            var pendingFlexyAdmin = await _context.FlexyHourRequests.CountAsync(r => r.Status == "Pending");
            if (pendingFlexyAdmin > 0)
            {
                dynamicAlerts.Add(new { id = currentAlertId++, type = "warning", message = $"{pendingFlexyAdmin} flexy hour request(s) are pending.", time = DateTime.Now.ToString("HH:mm") });
            }

            var todayAbsences = await _context.AttendanceLogs.CountAsync(l => l.Date == todayString && (l.Status == "Absent" || l.Status == "On Leave"));
            if (todayAbsences > 0)
            {
                dynamicAlerts.Add(new { id = currentAlertId++, type = "info", message = $"{todayAbsences} employee(s) are absent or on leave today.", time = DateTime.Now.ToString("HH:mm") });
            }

            if (dynamicAlerts.Count == 0)
            {
                dynamicAlerts.Add(new { id = currentAlertId++, type = "info", message = "System running smoothly. No new alerts.", time = DateTime.Now.ToString("HH:mm") });
            }

            double avgHours = totalEmployees > 0 ? await _context.Users.AverageAsync(u => (double?)u.WorkHoursThisMonth) ?? 0 : 0;
            double avgAttendance = totalEmployees > 0 ? await _context.Users.AverageAsync(u => (double?)u.AttendanceRate) ?? 0 : 0;

            int totalEmployeesCount = await _context.Users.CountAsync(u => u.Role != null && u.Role.Contains("employee"));
            int activeEmployeesCount = await _context.Users.CountAsync(u => u.Role != null && u.Role.Contains("employee") && u.IsActive);
            int inactiveEmployeesCount = await _context.Users.CountAsync(u => u.Role != null && u.Role.Contains("employee") && !u.IsActive);

            var todayLogs = await _context.AttendanceLogs.AsNoTracking().Where(l => l.Date == todayString).ToListAsync();
            var activeEmployeesList = await _context.Users.AsNoTracking().Where(u => u.Role != null && u.Role.Contains("employee") && u.IsActive).ToListAsync();

            var presentList = activeEmployeesList
                .Where(e => todayLogs.Any(l => l.UserId == e.Id && (l.Status == "Present" || l.Status == "Active")))
                .Select(e => {
                    var log = todayLogs.First(l => l.UserId == e.Id);
                    return new {
                        id = e.Id,
                        employeeId = e.EmployeeId,
                        name = e.Name,
                        department = e.Department ?? "N/A",
                        clockIn = log.ClockIn,
                        clockOut = log.ClockOut
                    };
                }).ToList();

            var absentList = activeEmployeesList
                .Where(e => !todayLogs.Any(l => l.UserId == e.Id))
                .Select(e => new {
                    id = e.Id,
                    employeeId = e.EmployeeId,
                    name = e.Name,
                    department = e.Department ?? "N/A"
                }).ToList();

            var lateList = activeEmployeesList
                .Where(e => todayLogs.Any(l => l.UserId == e.Id && l.LateEntry))
                .Select(e => {
                    var log = todayLogs.First(l => l.UserId == e.Id);
                    return new {
                        id = e.Id,
                        employeeId = e.EmployeeId,
                        name = e.Name,
                        department = e.Department ?? "N/A",
                        clockIn = log.ClockIn
                    };
                }).ToList();

            var earlyList = activeEmployeesList
                .Where(e => todayLogs.Any(l => l.UserId == e.Id && l.EarlyExit))
                .Select(e => {
                    var log = todayLogs.First(l => l.UserId == e.Id);
                    return new {
                        id = e.Id,
                        employeeId = e.EmployeeId,
                        name = e.Name,
                        department = e.Department ?? "N/A",
                        clockOut = log.ClockOut
                    };
                }).ToList();

            var missingList = activeEmployeesList
                .Where(e => todayLogs.Any(l => l.UserId == e.Id && (l.Status == "Active" || l.ClockOut == "---")))
                .Select(e => {
                    var log = todayLogs.First(l => l.UserId == e.Id);
                    return new {
                        id = e.Id,
                        employeeId = e.EmployeeId,
                        name = e.Name,
                        department = e.Department ?? "N/A",
                        clockIn = log.ClockIn
                    };
                }).ToList();

            int presentToday = presentList.Count;
            int absentToday = absentList.Count;
            int lateToday = lateList.Count;
            int earlyToday = earlyList.Count;
            int missingPunchesToday = missingList.Count;

            var last7Days = Enumerable.Range(0, 7)
                .Select(i => DateTime.Now.AddDays(-i).ToString("yyyy-MM-dd"))
                .Reverse()
                .ToList();
            
            var weeklyTrend = new List<object>();
            foreach (var date in last7Days)
            {
                var logsForDay = await _context.AttendanceLogs.AsNoTracking().Where(l => l.Date == date).ToListAsync();
                int dayPresent = logsForDay.Count(l => l.Status == "Present" || l.Status == "Active");
                int dayLate = logsForDay.Count(l => l.LateEntry);
                weeklyTrend.Add(new {
                    date = DateTime.Parse(date).ToString("dd MMM"),
                    present = dayPresent,
                    late = dayLate
                });
            }

            int monthlyPresent = activeEmployeesList.Sum(e => e.PresentDays);
            int monthlyAbsent = activeEmployeesList.Sum(e => e.AbsentDays);
            int monthlyOnLeave = await _context.LeaveRequests.CountAsync(r => r.Status == "Approved");

            var stats = new
            {
                totalEmployees = totalEmployees,
                activeShifts = activeShiftsCount,
                avgWorkingHours = Math.Round(avgHours, 1),
                overallAttendanceRate = Math.Round(avgAttendance, 1),
                departmentStats = dynamicDeptStats,
                systemAlerts = dynamicAlerts,
                empStats = new {
                    total = totalEmployeesCount,
                    active = activeEmployeesCount,
                    inactive = inactiveEmployeesCount
                },
                attStats = new {
                    presentToday = presentToday,
                    absentToday = absentToday,
                    lateArrivals = lateToday,
                    earlyDepartures = earlyToday,
                    missingPunches = missingPunchesToday,
                    presentList = presentList,
                    absentList = absentList,
                    lateList = lateList,
                    earlyList = earlyList,
                    missingList = missingList
                },
                weeklyTrend = weeklyTrend,
                monthlySummary = new {
                    present = monthlyPresent,
                    absent = monthlyAbsent,
                    onLeave = monthlyOnLeave
                }
            };

            return Ok(stats);
        }

        [HttpGet("user/{id}")]
        public async Task<IActionResult> GetUserById(int id)
        {
            var user = await _context.Users.Include(u => u.Shift).FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound(new { error = "User not found." });
            return Ok(new
            {
                id = user.Id,
                name = user.Name,
                email = user.Email,
                role = user.Role,
                department = user.Department,
                avatar = user.Avatar,
                shift = user.Shift,
                employeeId = user.EmployeeId
            });
        }

        [HttpGet("users")]
        public async Task<ActionResult<IEnumerable<object>>> GetUsers()
        {
            var users = await _context.Users.AsNoTracking().Select(u => new
            {
                u.Id,
                u.Name,
                u.Email,
                u.EmployeeId,
                u.Role,
                u.Department,
                u.Shift,
                u.ShiftId,
                u.IsActive
            }).ToListAsync();
            return Ok(users);
        }

        [HttpPut("users/{id}/status")]
        public async Task<IActionResult> UpdateUserStatus(int id, [FromBody] UpdateStatusRequest request)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound(new { error = "User not found." });

            user.IsActive = request.IsActive;

            string statusStr = user.IsActive ? "Activated" : "Deactivated";
            var statusNotification = new Notification
            {
                UserId = user.Id,
                Title = $"Account {statusStr}",
                Message = $"Your account has been {statusStr.ToLower()} by an administrator.",
                Type = user.IsActive ? "success" : "danger"
            };
            _context.Notifications.Add(statusNotification);

            await _context.SaveChangesAsync();

            string body = $"<p>Hi {user.Name},</p><p>Your account has been <strong>{statusStr}</strong> by an administrator.</p>";
            _ = _emailService.SendEmailAsync(user.Email, $"Account {statusStr}", body);

            return Ok(new { message = "Status updated successfully." });
        }

        [HttpPut("users/{id}/shift")]
        public async Task<IActionResult> UpdateUserShift(int id, [FromBody] UpdateShiftRequest request)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound(new { error = "User not found." });

            if ((user.Role != null && user.Role.ToLower().Contains("manager")) || (user.Role != null && user.Role.ToLower().Contains("admin")))
            {
                return BadRequest(new { error = "Shifts cannot be modified for managers or administrators." });
            }

            var shift = await _context.Shifts.FindAsync(request.ShiftId);
            if (shift != null)
            {
                user.ShiftId = shift.Id;

                var shiftNotification = new Notification
                {
                    UserId = user.Id,
                    Title = "Shift Assignment Updated",
                    Message = $"Your shift has been updated to: {shift.Name} ({shift.StartTime} - {shift.EndTime}).",
                    Type = "info"
                };
                _context.Notifications.Add(shiftNotification);

                await _context.SaveChangesAsync();

                string body = $"<p>Hi {user.Name},</p><p>Your shift has been updated to: <strong>{shift.Name}</strong> ({shift.StartTime} - {shift.EndTime}).</p>";
                _ = _emailService.SendEmailAsync(user.Email, "Shift Assignment Updated", body);
            }
            
            return Ok(new { message = "Shift updated successfully." });
        }

        public class ForgotPasswordRequest
        {
            [JsonPropertyName("emailOrId")]
            public string EmailOrId { get; set; } = string.Empty;
        }

        public class VerifyOtpRequest
        {
            [JsonPropertyName("emailOrId")]
            public string EmailOrId { get; set; } = string.Empty;

            [JsonPropertyName("otpCode")]
            public string OtpCode { get; set; } = string.Empty;
        }

        public class ResetPasswordRequest
        {
            [JsonPropertyName("emailOrId")]
            public string EmailOrId { get; set; } = string.Empty;

            [JsonPropertyName("resetToken")]
            public string ResetToken { get; set; } = string.Empty;

            [JsonPropertyName("newPassword")]
            public string NewPassword { get; set; } = string.Empty;
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.EmailOrId))
            {
                return BadRequest(new { error = "Email or Employee ID is required." });
            }

            string input = request.EmailOrId.Trim();
            User? user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == input.ToLower() || u.EmployeeId.ToLower() == input.ToLower());

            if (user == null && int.TryParse(input, out int id))
            {
                user = await _context.Users.FindAsync(id);
            }

            if (user == null)
            {
                return NotFound(new { error = "No employee account was found matching that Email or ID." });
            }

            // Generate 6-digit random OTP
            var rand = new Random();
            string otpCode = rand.Next(100000, 999999).ToString();

            // Save OTP
            var newOtp = new Otp
            {
                UserId = user.Id,
                OtpCode = otpCode,
                ExpiryTime = DateTime.Now.AddMinutes(5),
                IsUsed = false
            };

            // Remove any existing unused OTPs for this user
            var oldOtps = await _context.Otps.Where(o => o.UserId == user.Id && !o.IsUsed).ToListAsync();
            _context.Otps.RemoveRange(oldOtps);

            _context.Otps.Add(newOtp);
            await _context.SaveChangesAsync();

            // Print to console for server logs verification
            Console.WriteLine($"[SECURITY - OTP GENERATED] User ID: {user.Id}, Email: {user.Email}, OTP: {otpCode}, Expires: {newOtp.ExpiryTime}");

            string emailBody = $"<p>Hi {user.Name},</p><p>We received a request to reset your password.</p><p>Your verification code is: <strong style='font-size: 1.5em; letter-spacing: 2px;'>{otpCode}</strong></p><p>This code will expire in 5 minutes.</p>";
            _ = _emailService.SendEmailAsync(user.Email, "Password Reset Verification Code", emailBody);

            // Return OTP in response payload for easy local front-end verification
            return Ok(new
            {
                message = "Verification code generated and sent.",
                email = user.Email,
                otpCode = otpCode // Provided in response for easy local debugging
            });
        }

        [HttpPost("verify-otp")]
        public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.EmailOrId) || string.IsNullOrWhiteSpace(request.OtpCode))
            {
                return BadRequest(new { error = "Email/ID and Verification Code are required." });
            }

            string input = request.EmailOrId.Trim();
            User? user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == input.ToLower() || u.EmployeeId.ToLower() == input.ToLower());

            if (user == null && int.TryParse(input, out int id))
            {
                user = await _context.Users.FindAsync(id);
            }

            if (user == null)
            {
                return NotFound(new { error = "User not found." });
            }

            var otp = await _context.Otps
                .Where(o => o.UserId == user.Id && o.OtpCode == request.OtpCode.Trim() && !o.IsUsed)
                .OrderByDescending(o => o.ExpiryTime)
                .FirstOrDefaultAsync();

            if (otp == null)
            {
                return BadRequest(new { error = "Invalid verification code." });
            }

            if (DateTime.Now > otp.ExpiryTime)
            {
                return BadRequest(new { error = "Verification code has expired." });
            }

            return Ok(new
            {
                message = "Verification successful.",
                resetToken = otp.OtpCode
            });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.EmailOrId) || string.IsNullOrWhiteSpace(request.ResetToken) || string.IsNullOrWhiteSpace(request.NewPassword))
            {
                return BadRequest(new { error = "All fields are required." });
            }

            string input = request.EmailOrId.Trim();
            User? user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == input.ToLower() || u.EmployeeId.ToLower() == input.ToLower());

            if (user == null && int.TryParse(input, out int id))
            {
                user = await _context.Users.FindAsync(id);
            }

            if (user == null)
            {
                return NotFound(new { error = "User not found." });
            }

            // Find valid OTP
            var otp = await _context.Otps
                .Where(o => o.UserId == user.Id && o.OtpCode == request.ResetToken.Trim() && !o.IsUsed)
                .OrderByDescending(o => o.ExpiryTime)
                .FirstOrDefaultAsync();

            if (otp == null || DateTime.Now > otp.ExpiryTime)
            {
                return BadRequest(new { error = "Invalid or expired reset token. Please request a new code." });
            }

            // Password Policy check
            string newPassword = request.NewPassword;
            if (newPassword.Length < 8 ||
                !newPassword.Any(char.IsUpper) ||
                !newPassword.Any(char.IsLower) ||
                !newPassword.Any(char.IsDigit) ||
                !newPassword.Any(ch => !char.IsLetterOrDigit(ch)))
            {
                return BadRequest(new { error = "Password must be at least 8 characters long and contain at least one uppercase letter, one lowercase letter, one number, and one special character." });
            }

            // Password History check (last 3 passwords)
            var pastPasswords = await _context.PasswordHistories
                .Where(ph => ph.UserId == user.Id)
                .OrderByDescending(ph => ph.CreatedAt)
                .Take(3)
                .Select(ph => ph.PasswordHash)
                .ToListAsync();

            if (pastPasswords.Contains(newPassword))
            {
                return BadRequest(new { error = "You cannot reuse any of your last 3 passwords." });
            }

            // Update user password
            user.Password = newPassword;

            // Save to history
            _context.PasswordHistories.Add(new PasswordHistory
            {
                UserId = user.Id,
                PasswordHash = newPassword,
                CreatedAt = DateTime.Now
            });

            // Mark OTP as used
            otp.IsUsed = true;

            await _context.SaveChangesAsync();

            string emailBody = $"<p>Hi {user.Name},</p><p>Your password was successfully reset.</p><p>If you did not make this change, please contact your administrator immediately.</p>";
            _ = _emailService.SendEmailAsync(user.Email, "Password Reset Successful", emailBody);

            return Ok(new { message = "Password has been reset successfully." });
        }
    }
}
