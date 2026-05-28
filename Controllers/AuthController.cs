using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Ams.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AuthController(AppDbContext context)
        {
            _context = context;
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
                .FirstOrDefaultAsync(u => u.Email.ToLower() == sanitizedEmail);

            if (user == null || user.Password != request.Password)
            {
                return Unauthorized(new { error = "Invalid email or password." });
            }

            if (!string.Equals(user.Role, request.Role, StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(403, new { error = $"Unauthorized. Selected role does not match this user's account." });
            }

            // Generate token
            string mockToken = $"mock-jwt-token-for-{user.Role}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

            // Dynamically gather stats depending on role from live SQL tables
            object? specializedStats = null;

            if (user.Role.ToLower() == "employee")
            {
                var logs = await _context.AttendanceLogs
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
                        hours = l.Hours
                    }).ToList()
                };
            }
            else if (user.Role.ToLower() == "manager")
            {
                int teamCount = await _context.Users.CountAsync(u => u.Role == "employee");
                int presentCount = await _context.AttendanceLogs.CountAsync(l => l.Date == "2026-05-20" && l.Status == "Present");
                int onLeaveCount = await _context.AttendanceLogs.CountAsync(l => l.Date == "2026-05-20" && l.Status == "On Leave");
                
                var pendingRequests = await _context.LeaveRequests
                    .Where(r => r.Status == "Pending")
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
                    presentToday = presentCount > 0 ? presentCount : 10, // fallbacks for default view
                    onLeaveToday = onLeaveCount > 0 ? onLeaveCount : 1,
                    absentToday = 1,
                    pendingLeavesCount = pendingRequests.Count,
                    teamAttendanceRate = 94,
                    pendingRequests = pendingRequests
                };
            }
            else if (user.Role.ToLower() == "admin")
            {
                int totalEmployees = await _context.Users.CountAsync();
                
                var allDepts = await _context.Departments.ToListAsync();
                var dynamicDeptStats = new List<object>();
                foreach (var d in allDepts)
                {
                    int deptCount = await _context.Users.CountAsync(u => u.Department == d.Name);
                    // Use a mock attendance rate for now, or calculate if needed
                    dynamicDeptStats.Add(new { name = d.Name, count = deptCount, attendance = "96.0%" });
                }

                specializedStats = new
                {
                    totalEmployees = totalEmployees,
                    activeShifts = 3,
                    avgWorkingHours = 8.2,
                    overallAttendanceRate = 96.5,
                    departmentStats = dynamicDeptStats,
                    systemAlerts = new[]
                    {
                        new { id = 1, type = "warning", message = "High absence rate in Sales department today.", time = "10:00 AM" },
                        new { id = 2, type = "info", message = "Monthly attendance report auto-generated successfully.", time = "08:30 AM" }
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
                    role = user.Role,
                    department = user.Department,
                    avatar = user.Avatar,
                    shift = user.Shift,
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

            // Check if user already exists
            var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.Trim().ToLower());
            if (existingUser != null)
            {
                return BadRequest(new { error = "Employee with this email already exists." });
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
                ShiftId = request.ShiftId
            };

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

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
                user.Password = request.NewPassword;
            }

            await _context.SaveChangesAsync();

            return Ok(new { 
                message = "Profile updated successfully!", 
                avatar = user.Avatar 
            });
        }
    }
}
