using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace Ams.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LeavesController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly Ams.Services.IEmailService _emailService;

        public LeavesController(AppDbContext context, Ams.Services.IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        public class LeaveSubmission
        {
            [JsonPropertyName("userId")]
            public int UserId { get; set; }

            [JsonPropertyName("type")]
            public string Type { get; set; } = string.Empty;

            [JsonPropertyName("duration")]
            public string Duration { get; set; } = string.Empty;

            [JsonPropertyName("reason")]
            public string Reason { get; set; } = string.Empty;
        }

        public class ResolveRequest
        {
            [JsonPropertyName("id")]
            public int Id { get; set; }

            [JsonPropertyName("decision")]
            public string Decision { get; set; } = string.Empty; // "approve" or "reject"

            [JsonPropertyName("managerId")]
            public int? ManagerId { get; set; }

            [JsonPropertyName("signature")]
            public string? Signature { get; set; }

            [JsonPropertyName("approverEmail")]
            public string ApproverEmail { get; set; } = string.Empty;

            [JsonPropertyName("approverPassword")]
            public string ApproverPassword { get; set; } = string.Empty;
        }

        // GET /api/leaves/user/{userId} — Employee fetches their own leave history
        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetUserLeaves(int userId)
        {
            if (userId <= 0)
            {
                return BadRequest(new { error = "Invalid User ID." });
            }

            var leaves = await _context.LeaveRequests.AsNoTracking()
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.Id)
                .Select(r => new
                {
                    id = r.Id,
                    type = r.Type,
                    duration = r.Duration,
                    reason = r.Reason,
                    status = r.Status,
                    approverSignature = r.ApproverSignature,
                    tlApprovalStatus = r.TlApprovalStatus,
                    tlApproverSignature = r.TlApproverSignature,
                    hrApprovalStatus = r.HrApprovalStatus,
                    hrApproverSignature = r.HrApproverSignature
                })
                .ToListAsync();

            return Ok(new { leaves = leaves });
        }

        [HttpPost("request")]
        public async Task<IActionResult> RequestLeave([FromBody] LeaveSubmission request)
        {
            if (request == null || request.UserId <= 0 || string.IsNullOrWhiteSpace(request.Type) || string.IsNullOrWhiteSpace(request.Duration))
            {
                return BadRequest(new { error = "Invalid leave request parameters." });
            }

            var user = await _context.Users.FindAsync(request.UserId);
            if (user == null)
            {
                return NotFound(new { error = "User not found." });
            }

            var leave = new LeaveRequest
            {
                UserId = request.UserId,
                Name = user.Name,
                Type = request.Type,
                Duration = request.Duration,
                Reason = request.Reason,
                Status = "Pending"
            };

            _context.LeaveRequests.Add(leave);
            await _context.SaveChangesAsync();

            // Notify Managers matching employee's department
            var managers = await _context.Users.Where(u => u.Role != null && u.Role.Contains("manager")).ToListAsync();
            var employeeDept = (user.Department ?? string.Empty).Trim().ToLower();
            var assignedManagers = managers.Where(m => (m.Department ?? string.Empty)
                .Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim().ToLower())
                .Contains(employeeDept)).ToList();

            if (!assignedManagers.Any())
            {
                assignedManagers = managers; // Fallback to all managers if none are assigned specifically
            }
            
            // Create notification for employee
            var notification = new Notification
            {
                UserId = request.UserId,
                Title = "Leave Request Submitted",
                Message = $"Your {request.Type} request for {(request.Duration ?? "").Split('|')[0]} was submitted successfully and is pending approval.",
                Type = "success"
            };
            _context.Notifications.Add(notification);

            // Create notification for managers
            foreach (var m in assignedManagers)
            {
                var mgrNotification = new Notification
                {
                    UserId = m.Id,
                    Title = "New Leave Request",
                    Message = $"{user.Name} has submitted a {leave.Type} request for {leave.Duration}.",
                    Type = "warning"
                };
                _context.Notifications.Add(mgrNotification);
            }
            await _context.SaveChangesAsync();

            foreach (var m in assignedManagers)
            {
                _ = _emailService.SendTemplatedEmailAsync(m.Email, $"New Leave Request from {user.Name}", "LeaveRequested.html", new System.Collections.Generic.Dictionary<string, string>
                {
                    { "ManagerName", m.Name ?? "Manager" },
                    { "EmployeeName", user.Name ?? "Employee" },
                    { "LeaveType", leave.Type },
                    { "StartDate", leave.Duration },
                    { "EndDate", leave.Duration },
                    { "Reason", string.IsNullOrWhiteSpace(leave.Reason) ? "No reason provided" : leave.Reason },
                    { "PortalLink", "http://localhost:5173" }
                });
            }

            return Ok(new
            {
                message = "Leave request submitted successfully!",
                leave = new
                {
                    id = leave.Id,
                    type = leave.Type,
                    duration = leave.Duration,
                    reason = leave.Reason,
                    status = leave.Status
                }
            });
        }

        [HttpPost("resolve")]
        public async Task<IActionResult> ResolveLeave([FromBody] ResolveRequest request)
        {
            if (request == null || request.Id <= 0 || string.IsNullOrWhiteSpace(request.Decision))
            {
                return BadRequest(new { error = "Invalid resolution parameters." });
            }

            var leave = await _context.LeaveRequests.FindAsync(request.Id);
            if (leave == null)
            {
                return NotFound(new { error = "Leave request not found." });
            }

            // Validate approver credentials
            if (string.IsNullOrWhiteSpace(request.ApproverEmail) || string.IsNullOrWhiteSpace(request.ApproverPassword))
            {
                return BadRequest(new { error = "Approver email and password are required." });
            }

            string sanitizedEmail = request.ApproverEmail.Trim().ToLower();
            var approver = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == sanitizedEmail || u.EmployeeId.ToLower() == sanitizedEmail);

            if (approver == null || approver.Password != request.ApproverPassword)
            {
                return Unauthorized(new { error = "Invalid approver email or password." });
            }

            if (approver.Role == null)
            {
                return StatusCode(403, new { error = "Approver has no role assigned." });
            }

            bool isAdmin = approver.Role.ToLower().Contains("admin");
            bool isManager = approver.Role.ToLower().Contains("manager");

            if (!isAdmin && !isManager)
            {
                return StatusCode(403, new { error = "Unauthorized. Only Team Leads or HR Admins can resolve leave requests." });
            }

            string normalizedDecision = request.Decision.Trim().ToLower();
            if (normalizedDecision != "approve" && normalizedDecision != "reject")
            {
                return BadRequest(new { error = "Decision must be 'approve' or 'reject'." });
            }

            string notificationTitle = string.Empty;
            string notificationMessage = string.Empty;
            string notificationColor = string.Empty;

            // ── STAGE 1: Team Lead (Manager) approval ─────────────────────────────
            if (leave.Status == "Pending")
            {
                if (!isManager && !isAdmin)
                {
                    return StatusCode(403, new { error = "Unauthorized." });
                }

                // Dept check: TL can only approve own dept. Admins can bypass this check.
                if (!isAdmin)
                {
                var employee = await _context.Users.FindAsync(leave.UserId);
                if (employee != null)
                {
                    var empDept = (employee.Department ?? string.Empty).Trim().ToLower();
                    var managerDepts = (approver.Department ?? string.Empty)
                        .Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(d => d.Trim().ToLower())
                        .ToList();

                    if (!managerDepts.Contains(empDept))
                    {
                        return StatusCode(403, new { error = "Unauthorized. You can only resolve requests for employees in your assigned department(s)." });
                    }
                }
                }

                bool isApproved = normalizedDecision == "approve";
                leave.TlApprovalStatus = isApproved ? "Approved" : "Rejected";
                leave.TlApproverSignature = approver.Name;

                // Auto-approve HR step for casual/sick leaves if TL approves
                if (isApproved && (leave.Type == "Casual Leave" || leave.Type == "Sick Leave"))
                {
                    leave.HrApprovalStatus = "Approved";
                    leave.Status = "Approved";
                    leave.ApproverSignature = approver.Name;
                }
                else if (isApproved)
                {
                    // Move to Stage 2: awaiting HR
                    leave.Status = "Pending HR Approval";
                    leave.ApproverSignature = approver.Name;

                    // Notify HR admins that this leave needs their approval
                    var hrAdmins = await _context.Users
                        .Where(u => u.Role != null && u.Role.ToLower().Contains("admin"))
                        .ToListAsync();

                    foreach (var hr in hrAdmins)
                    {
                        _context.Notifications.Add(new Notification
                        {
                            UserId = hr.Id,
                            Title = "Leave Needs HR Approval",
                            Message = $"{leave.Name}'s {leave.Type} leave ({(leave.Duration ?? "").Split('|')[0]}) was approved by TL {approver.Name} and awaits your final approval.",
                            Type = "warning"
                        });
                    }

                    notificationTitle = "Leave — TL Approved";
                    notificationMessage = $"Your {leave.Type} request was approved by your Team Lead ({approver.Name}). Awaiting HR final approval.";
                    notificationColor = "warning";
                }
                else
                {
                    // TL rejected — final rejection
                    leave.Status = "Rejected";
                    leave.HrApprovalStatus = "N/A";
                    leave.ApproverSignature = approver.Name;

                    notificationTitle = "Leave Request Rejected";
                    notificationMessage = $"Your {leave.Type} request for {(leave.Duration ?? "").Split('|')[0]} was rejected by your Team Lead ({approver.Name}).";
                    notificationColor = "danger";
                }
            }
            // ── STAGE 2: HR (Admin) final approval ────────────────────────────────
            else if (leave.Status == "Pending HR Approval")
            {
                if (!isAdmin)
                {
                    return StatusCode(403, new { error = "Unauthorized. Only HR Admins can perform final approval." });
                }

                bool isApproved = normalizedDecision == "approve";
                leave.HrApprovalStatus = isApproved ? "Approved" : "Rejected";
                leave.HrApproverSignature = approver.Name;

                if (isApproved)
                {
                    leave.Status = "Approved";
                    leave.ApproverSignature = approver.Name;

                    // Deduct leave balance and create attendance logs
                    var employeeForLeave = await _context.Users.FindAsync(leave.UserId);
                    if (employeeForLeave != null)
                    {
                        int daysToDeduct = 1;
                        var match = System.Text.RegularExpressions.Regex.Match(leave.Duration ?? "", @"^(\d+)\s+day");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out int parsedDays))
                        {
                            daysToDeduct = parsedDays;
                        }
                        employeeForLeave.LeaveBalance -= daysToDeduct;

                        var durationParts = (leave.Duration ?? "").Split('|');
                        if (durationParts.Length >= 3 && DateTime.TryParse(durationParts[1], out DateTime startDate) && DateTime.TryParse(durationParts[2], out DateTime endDate))
                        {
                            for (DateTime dt = startDate.Date; dt <= endDate.Date; dt = dt.AddDays(1))
                            {
                                _context.AttendanceLogs.Add(new AttendanceLog
                                {
                                    UserId = employeeForLeave.Id,
                                    Date = dt.ToString("yyyy-MM-dd"),
                                    ClockIn = "---",
                                    ClockOut = "---",
                                    Status = "On Leave",
                                    Hours = 0.0
                                });
                            }
                        }
                        else
                        {
                            _context.AttendanceLogs.Add(new AttendanceLog
                            {
                                UserId = employeeForLeave.Id,
                                Date = DateTime.Now.ToString("yyyy-MM-dd"),
                                ClockIn = "---",
                                ClockOut = "---",
                                Status = "On Leave",
                                Hours = 0.0
                            });
                        }
                    }

                    notificationTitle = "Leave Request Fully Approved";
                    notificationMessage = $"Great news! Your {leave.Type} request for {(leave.Duration ?? "").Split('|')[0]} has been fully approved by HR ({approver.Name}).";
                    notificationColor = "success";
                }
                else
                {
                    // HR rejected
                    leave.Status = "Rejected";
                    leave.ApproverSignature = approver.Name;

                    notificationTitle = "Leave Request Rejected";
                    notificationMessage = $"Your {leave.Type} request for {(leave.Duration ?? "").Split('|')[0]} was rejected.";
                    notificationColor = "danger";
                }
            }
            else
            {
                return BadRequest(new { error = "This request has already been fully resolved." });
            }

            // Notify the employee
            var leaveUser = await _context.Users.FindAsync(leave.UserId);
            if (leaveUser != null)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = leaveUser.Id,
                    Title = notificationTitle,
                    Message = notificationMessage,
                    Type = notificationColor
                });
            }

            await _context.SaveChangesAsync();

            // Email the employee on final resolution
            if (leaveUser != null && (leave.Status == "Approved" || leave.Status == "Rejected"))
            {
                string statusColor = leave.Status == "Approved" ? "#16a34a" : "#dc2626";
                _ = _emailService.SendTemplatedEmailAsync(leaveUser.Email, $"Your Leave Request has been {leave.Status}", "LeaveResolved.html", new System.Collections.Generic.Dictionary<string, string>
                {
                    { "EmployeeName", leaveUser.Name ?? "Employee" },
                    { "Status", leave.Status },
                    { "StatusColor", statusColor },
                    { "ApproverName", $"TL: {leave.TlApproverSignature}, HR: {leave.HrApproverSignature}" },
                    { "LeaveType", leave.Type },
                    { "StartDate", leave.Duration ?? "N/A" },
                    { "EndDate", leave.Duration ?? "N/A" },
                    { "Remarks", "Your request has been reviewed by both Team Lead and HR." },
                    { "DecisionDate", DateTime.Now.ToString("dd MMM yyyy") }
                });
            }

            return Ok(new
            {
                message = $"Leave request {leave.Status}!",
                leave = new
                {
                    id = leave.Id,
                    type = leave.Type,
                    duration = leave.Duration,
                    reason = leave.Reason,
                    status = leave.Status,
                    tlApprovalStatus = leave.TlApprovalStatus,
                    tlApproverSignature = leave.TlApproverSignature,
                    hrApprovalStatus = leave.HrApprovalStatus,
                    hrApproverSignature = leave.HrApproverSignature
                }
            });
        }

        // GET /api/leaves - Fetch all leave requests (Admin) or department-filtered (Manager)
        [HttpGet]
        public async Task<IActionResult> GetAllLeaves([FromQuery] int? managerId, [FromQuery] string? status)
        {
            var query = _context.LeaveRequests.AsNoTracking()
                .Include(r => r.User)
                .AsQueryable();

            if (managerId.HasValue)
            {
                var manager = await _context.Users.FindAsync(managerId.Value);
                if (manager != null && manager.Role != null && manager.Role.ToLower().Contains("manager"))
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

                    query = query.Where(r => r.User != null && managerDepts.Contains(r.User.Department));
                }
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(r => r.Status == status);
            }

            var leaves = await query
                .OrderByDescending(r => r.Id)
                .Select(r => new
                {
                    id = r.Id,
                    userId = r.UserId,
                    name = r.Name,
                    department = r.User != null ? r.User.Department : "N/A",
                    type = r.Type,
                    duration = r.Duration,
                    reason = r.Reason,
                    status = r.Status,
                    approverSignature = r.ApproverSignature,
                    tlApprovalStatus = r.TlApprovalStatus,
                    tlApproverSignature = r.TlApproverSignature,
                    hrApprovalStatus = r.HrApprovalStatus,
                    hrApproverSignature = r.HrApproverSignature
                })
                .ToListAsync();

            return Ok(new { leaves });
        }

        public class LeaveTypeInput
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;
        }

        // GET /api/leaves/types
        [HttpGet("types")]
        public async Task<IActionResult> GetLeaveTypes()
        {
            var types = await _context.LeaveTypes.AsNoTracking()
                .OrderBy(t => t.Name)
                .Select(t => new
                {
                    id = t.Id,
                    name = t.Name
                })
                .ToListAsync();

            return Ok(types);
        }

        // POST /api/leaves/types
        [HttpPost("types")]
        public async Task<IActionResult> CreateLeaveType([FromBody] LeaveTypeInput input)
        {
            if (input == null || string.IsNullOrWhiteSpace(input.Name))
            {
                return BadRequest(new { error = "Leave type name cannot be empty." });
            }

            string trimmedName = input.Name.Trim();

            // Check if already exists
            var exists = await _context.LeaveTypes.AnyAsync(t => t.Name.ToLower() == trimmedName.ToLower());
            if (exists)
            {
                return BadRequest(new { error = "Leave type already exists." });
            }

            var newType = new LeaveType { Name = trimmedName };
            _context.LeaveTypes.Add(newType);
            await _context.SaveChangesAsync();

            return Ok(new { id = newType.Id, name = newType.Name });
        }

        // DELETE /api/leaves/types/{id}
        [HttpDelete("types/{id}")]
        public async Task<IActionResult> DeleteLeaveType(int id)
        {
            var type = await _context.LeaveTypes.FindAsync(id);
            if (type == null)
            {
                return NotFound(new { error = "Leave type not found." });
            }

            _context.LeaveTypes.Remove(type);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Leave type deleted successfully." });
        }
    }
}

