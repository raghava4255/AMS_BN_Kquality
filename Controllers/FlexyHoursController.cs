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
    public class FlexyHoursController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly Ams.Services.IEmailService _emailService;

        public FlexyHoursController(AppDbContext context, Ams.Services.IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        public class FlexySubmission
        {
            [JsonPropertyName("userId")]
            public int UserId { get; set; }

            [JsonPropertyName("date")]
            public string Date { get; set; } = string.Empty;

            [JsonPropertyName("type")]
            public string Type { get; set; } = string.Empty; // "Morning Flexy" or "Evening Flexy"

            [JsonPropertyName("hoursRequested")]
            public int HoursRequested { get; set; }

            [JsonPropertyName("reason")]
            public string Reason { get; set; } = string.Empty;
        }

        public class ResolveFlexyDto
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

        // GET /api/flexyhours/user/{userId} - Fetch employee's flexy requests
        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetUserFlexyRequests(int userId)
        {
            if (userId <= 0) return BadRequest(new { error = "Invalid User ID." });

            var requests = await _context.FlexyHourRequests.AsNoTracking()
                .Where(f => f.UserId == userId)
                .OrderByDescending(f => f.Id)
                .Select(f => new
                {
                    id = f.Id,
                    date = f.Date,
                    type = f.Type,
                    hoursRequested = f.HoursRequested,
                    reason = f.Reason,
                    status = f.Status
                })
                .ToListAsync();

            return Ok(new { requests });
        }

        // GET /api/flexyhours/pending - Fetch all pending requests for managers
        [HttpGet("pending")]
        public async Task<IActionResult> GetPendingRequests([FromQuery] int? managerId)
        {
            var query = _context.FlexyHourRequests.AsNoTracking()
                .Where(f => f.Status == "Pending")
                .Include(f => f.User)
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

                    query = query.Where(f => f.User != null && managerDepts.Contains(f.User.Department));
                }
            }

            var requests = await query
                .OrderBy(f => f.Id)
                .Select(f => new
                {
                    id = f.Id,
                    userId = f.UserId,
                    userName = f.User!.Name,
                    date = f.Date,
                    type = f.Type,
                    hoursRequested = f.HoursRequested,
                    reason = f.Reason,
                    status = f.Status
                })
                .ToListAsync();

            return Ok(new { requests });
        }

        [HttpPost("request")]
        public async Task<IActionResult> RequestFlexyHour([FromBody] FlexySubmission request)
        {
            if (request == null || request.UserId <= 0) return BadRequest(new { error = "Invalid user." });
            if (request.HoursRequested <= 0 || request.HoursRequested > 2) return BadRequest(new { error = "You can only request up to 2 flexy hours." });
            if (string.IsNullOrWhiteSpace(request.Date)) return BadRequest(new { error = "Date is required." });
            if (request.Type != "Morning Flexy" && request.Type != "Evening Flexy") return BadRequest(new { error = "Invalid flexy type." });

            var user = await _context.Users.FindAsync(request.UserId);
            if (user == null) return NotFound(new { error = "User not found." });

            // Check monthly limit (max 2 days per month)
            // Parse month and year from Date (e.g. "yyyy-MM-dd")
            string targetYearMonth = "";
            try
            {
                var dateObj = DateTime.Parse(request.Date);
                targetYearMonth = dateObj.ToString("yyyy-MM");
            }
            catch
            {
                return BadRequest(new { error = "Invalid date format." });
            }

            var currentMonthRequestsCount = await _context.FlexyHourRequests
                .Where(f => f.UserId == request.UserId && f.Date.StartsWith(targetYearMonth) && (f.Status == "Pending" || f.Status == "Approved"))
                .CountAsync();

            if (currentMonthRequestsCount >= 2)
            {
                return BadRequest(new { error = "Monthly limit reached. You can only request flexy hours 2 days per month." });
            }

            // Check if already requested for this specific date
            var existingDateRequest = await _context.FlexyHourRequests
                .Where(f => f.UserId == request.UserId && f.Date == request.Date && (f.Status == "Pending" || f.Status == "Approved"))
                .FirstOrDefaultAsync();

            if (existingDateRequest != null)
            {
                return BadRequest(new { error = "You have already applied for flexy hours on this date." });
            }

            var flexyRequest = new FlexyHourRequest
            {
                UserId = request.UserId,
                Date = request.Date,
                Type = request.Type,
                HoursRequested = request.HoursRequested,
                Reason = request.Reason,
                Status = "Pending"
            };

            _context.FlexyHourRequests.Add(flexyRequest);
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
            var empNotification = new Notification
            {
                UserId = request.UserId,
                Title = "Flexy Request Submitted",
                Message = $"Your {flexyRequest.Type} request for {flexyRequest.Date} was submitted successfully and is pending approval.",
                Type = "success"
            };
            _context.Notifications.Add(empNotification);

            // Create notification for managers
            foreach (var m in assignedManagers)
            {
                var mgrNotification = new Notification
                {
                    UserId = m.Id,
                    Title = "New Flexy Request",
                    Message = $"{user.Name} has submitted a {flexyRequest.Type} request for {flexyRequest.Date}.",
                    Type = "warning"
                };
                _context.Notifications.Add(mgrNotification);
            }
            await _context.SaveChangesAsync();

            foreach (var m in assignedManagers)
            {
                _ = _emailService.SendTemplatedEmailAsync(m.Email, $"New Flexy Hour Request from {user.Name}", "LeaveRequested.html", new System.Collections.Generic.Dictionary<string, string>
                {
                    { "ManagerName", m.Name ?? "Manager" },
                    { "EmployeeName", user.Name ?? "Employee" },
                    { "LeaveType", $"{flexyRequest.Type} ({flexyRequest.HoursRequested} hours)" },
                    { "StartDate", flexyRequest.Date },
                    { "EndDate", flexyRequest.Date },
                    { "Reason", string.IsNullOrWhiteSpace(flexyRequest.Reason) ? "No reason provided" : flexyRequest.Reason },
                    { "PortalLink", "http://localhost:5173" }
                });
            }

            return Ok(new
            {
                message = "Flexy hour request submitted successfully!",
                request = new
                {
                    id = flexyRequest.Id,
                    date = flexyRequest.Date,
                    type = flexyRequest.Type,
                    hoursRequested = flexyRequest.HoursRequested,
                    reason = flexyRequest.Reason,
                    status = flexyRequest.Status
                }
            });
        }

        [HttpPost("resolve")]
        public async Task<IActionResult> ResolveFlexyRequest([FromBody] ResolveFlexyDto request)
        {
            if (request == null || request.Id <= 0 || string.IsNullOrWhiteSpace(request.Decision))
                return BadRequest(new { error = "Invalid resolution parameters." });

            var flexy = await _context.FlexyHourRequests.FindAsync(request.Id);
            if (flexy == null) return NotFound(new { error = "Request not found." });

            if (string.IsNullOrWhiteSpace(request.ApproverEmail) || string.IsNullOrWhiteSpace(request.ApproverPassword)) return BadRequest(new { error = "Approver email and password are required." });

            string sanitizedEmail = request.ApproverEmail.Trim().ToLower();
            var approver = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == sanitizedEmail || u.EmployeeId.ToLower() == sanitizedEmail);
            
            if (approver == null || approver.Password != request.ApproverPassword) return Unauthorized(new { error = "Invalid approver email or password." });
            if (approver.Role == null) return StatusCode(403, new { error = "Approver has no role assigned." });

            bool isAdmin = approver.Role.ToLower().Contains("admin");
            bool isManager = approver.Role.ToLower().Contains("manager");
            if (!isAdmin && !isManager) return StatusCode(403, new { error = "Unauthorized." });

            string normalizedDecision = request.Decision.Trim().ToLower();
            if (normalizedDecision != "approve" && normalizedDecision != "reject") return BadRequest(new { error = "Decision must be 'approve' or 'reject'." });

            string notificationTitle = "";
            string notificationMessage = "";
            string notificationColor = "";

            if (flexy.Status == "Pending")
            {
                if (!isManager && !isAdmin) return StatusCode(403, new { error = "Unauthorized." });
                if (!isAdmin)
                {
                    var employee = await _context.Users.FindAsync(flexy.UserId);
                    if (employee != null)
                    {
                        var empDept = (employee.Department ?? string.Empty).Trim().ToLower();
                        var managerDepts = (approver.Department ?? string.Empty).Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries).Select(d => d.Trim().ToLower()).ToList();
                        if (!managerDepts.Contains(empDept)) return StatusCode(403, new { error = "Unauthorized. You can only resolve requests for employees in your assigned department(s)." });
                    }
                }

                bool isApproved = normalizedDecision == "approve";
                flexy.TlApprovalStatus = isApproved ? "Approved" : "Rejected";
                flexy.TlApproverSignature = approver.Name;

                if (isApproved)
                {
                    flexy.Status = "Pending HR Approval";
                    flexy.ApproverSignature = approver.Name;

                    var hrAdmins = await _context.Users.Where(u => u.Role != null && u.Role.ToLower().Contains("admin")).ToListAsync();
                    foreach (var hr in hrAdmins)
                    {
                        _context.Notifications.Add(new Notification
                        {
                            UserId = hr.Id,
                            Title = "Flexy Needs HR Approval",
                            Message = $"A Flexy request for {flexy.Date} was approved by TL {approver.Name} and awaits your final approval.",
                            Type = "warning"
                        });
                    }

                    notificationTitle = "Flexy — TL Approved";
                    notificationMessage = $"Your Flexy request was approved by your Team Lead ({approver.Name}). Awaiting HR final approval.";
                    notificationColor = "warning";
                }
                else
                {
                    flexy.Status = "Rejected";
                    flexy.HrApprovalStatus = "N/A";
                    flexy.ApproverSignature = approver.Name;

                    notificationTitle = "Flexy Request Rejected";
                    notificationMessage = $"Your Flexy request for {flexy.Date} was rejected by your Team Lead ({approver.Name}).";
                    notificationColor = "danger";
                }
            }
            else if (flexy.Status == "Pending HR Approval")
            {
                if (!isAdmin) return StatusCode(403, new { error = "Unauthorized. Only HR Admins can perform final approval." });

                bool isApproved = normalizedDecision == "approve";
                flexy.HrApprovalStatus = isApproved ? "Approved" : "Rejected";
                flexy.HrApproverSignature = approver.Name;

                if (isApproved)
                {
                    flexy.Status = "Approved";
                    flexy.ApproverSignature = approver.Name;
                    notificationTitle = "Flexy — HR Approved";
                    notificationMessage = $"Your Flexy request for {flexy.Date} has been fully approved by HR.";
                    notificationColor = "success";
                }
                else
                {
                    flexy.Status = "Rejected";
                    flexy.ApproverSignature = approver.Name;
                    notificationTitle = "Flexy Request Rejected";
                    notificationMessage = $"Your Flexy request for {flexy.Date} was rejected by HR.";
                    notificationColor = "danger";
                }
            }
            else
            {
                return BadRequest(new { error = "This request has already been resolved." });
            }

            var flexyUser = await _context.Users.FindAsync(flexy.UserId);
            if (flexyUser != null)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = flexyUser.Id,
                    Title = notificationTitle,
                    Message = notificationMessage,
                    Type = notificationColor
                });
            }

            await _context.SaveChangesAsync();

            if (flexyUser != null && flexy.Status != "Pending HR Approval")
            {
                string statusColor = flexy.Status == "Approved" ? "#16a34a" : "#dc2626";
                string approverName = flexy.ApproverSignature ?? "Management";
                _ = _emailService.SendTemplatedEmailAsync(flexyUser.Email, $"Your Flexy Hour Request has been {flexy.Status}", "FlexyHoursResolved.html", new System.Collections.Generic.Dictionary<string, string>
                {
                    { "EmployeeName", flexyUser.Name ?? "Employee" },
                    { "Status", flexy.Status },
                    { "StatusColor", statusColor },
                    { "ApproverName", approverName },
                    { "Date", flexy.Date },
                    { "RequestedHours", $"{flexy.HoursRequested} hours" },
                    { "Remarks", "Your request has been reviewed." },
                    { "DecisionDate", DateTime.Now.ToString("dd MMM yyyy") }
                });
            }

            return Ok(new
            {
                message = $"Request successfully processed!",
                request = new
                {
                    id = flexy.Id,
                    status = flexy.Status
                }
            });
        }

        // GET /api/flexyhours - Fetch all flexy requests (Admin)
        [HttpGet]
        public async Task<IActionResult> GetAllFlexyRequests()
        {
            var requests = await _context.FlexyHourRequests.AsNoTracking()
                .Include(f => f.User)
                .OrderByDescending(f => f.Id)
                .Select(f => new
                {
                    id = f.Id,
                    userId = f.UserId,
                    userName = f.User != null ? f.User.Name : "Unknown",
                    department = f.User != null ? f.User.Department : "N/A",
                    date = f.Date,
                    type = f.Type,
                    hoursRequested = f.HoursRequested,
                    reason = f.Reason,
                    status = f.Status,
                    approverSignature = f.ApproverSignature
                })
                .ToListAsync();

            return Ok(new { requests });
        }
    }
}
