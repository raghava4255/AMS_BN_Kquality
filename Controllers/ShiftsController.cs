using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace Ams.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ShiftsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly Ams.Services.IEmailService _emailService;

        public ShiftsController(AppDbContext context, Ams.Services.IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        public class ShiftSubmission
        {
            [JsonPropertyName("userId")]
            public int UserId { get; set; }

            [JsonPropertyName("requestedShiftId")]
            public int RequestedShiftId { get; set; }

            [JsonPropertyName("reason")]
            public string Reason { get; set; } = string.Empty;
        }

        public class ResolveShiftRequestDto
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

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Shift>>> GetShifts()
        {
            return await _context.Shifts.AsNoTracking().ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Shift>> GetShift(int id)
        {
            var shift = await _context.Shifts.FindAsync(id);
            if (shift == null)
            {
                return NotFound();
            }
            return shift;
        }

        [HttpPost]
        public async Task<ActionResult<Shift>> CreateShift([FromBody] Shift shift)
        {
            _context.Shifts.Add(shift);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetShift), new { id = shift.Id }, shift);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateShift(int id, [FromBody] Shift shift)
        {
            if (id != shift.Id)
            {
                return BadRequest("ID mismatch");
            }

            _context.Entry(shift).State = EntityState.Modified;
            
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.Shifts.Any(s => s.Id == id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteShift(int id)
        {
            var shift = await _context.Shifts.FindAsync(id);
            if (shift == null)
            {
                return NotFound();
            }

            // Prevent foreign key constraint errors by unassigning this shift from users
            var users = await _context.Users.Where(u => u.ShiftId == id).ToListAsync();
            foreach (var user in users)
            {
                user.ShiftId = null;
            }

            _context.Shifts.Remove(shift);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // GET /api/shifts/requests/user/{userId} - Fetch employee's shift requests
        [HttpGet("requests/user/{userId}")]
        public async Task<IActionResult> GetUserShiftRequests(int userId)
        {
            if (userId <= 0) return BadRequest(new { error = "Invalid User ID." });

            var requests = await _context.ShiftRequests.AsNoTracking()
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.Id)
                .Select(r => new
                {
                    id = r.Id,
                    userId = r.UserId,
                    requestedShiftId = r.RequestedShiftId,
                    requestedShiftName = r.RequestedShift != null ? r.RequestedShift.Name : "Unknown",
                    reason = r.Reason,
                    status = r.Status
                })
                .ToListAsync();

            return Ok(new { requests });
        }

        // GET /api/shifts/pending - Fetch all pending requests for managers
        [HttpGet("pending")]
        public async Task<IActionResult> GetPendingRequests([FromQuery] int? managerId)
        {
            var query = _context.ShiftRequests.AsNoTracking()
                .Where(r => r.Status == "Pending")
                .Include(r => r.User)
                .Include(r => r.RequestedShift)
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

            var requests = await query
                .OrderBy(r => r.Id)
                .Select(r => new
                {
                    id = r.Id,
                    userId = r.UserId,
                    userName = r.User != null ? r.User.Name : "Unknown",
                    userDept = r.User != null ? r.User.Department : "N/A",
                    currentShiftName = (r.User != null && r.User.Shift != null) ? r.User.Shift.Name : "General Shift",
                    requestedShiftId = r.RequestedShiftId,
                    requestedShiftName = r.RequestedShift != null ? r.RequestedShift.Name : "Unknown",
                    reason = r.Reason,
                    status = r.Status
                })
                .ToListAsync();

            return Ok(new { requests });
        }

        // GET /api/shifts/requests - Fetch all requests for admin
        [HttpGet("requests")]
        public async Task<IActionResult> GetAllShiftRequests()
        {
            var requests = await _context.ShiftRequests.AsNoTracking()
                .Include(r => r.User)
                .Include(r => r.RequestedShift)
                .OrderByDescending(r => r.Id)
                .Select(r => new
                {
                    id = r.Id,
                    userId = r.UserId,
                    userName = r.User != null ? r.User.Name : "Unknown",
                    userDept = r.User != null ? r.User.Department : "N/A",
                    currentShiftName = (r.User != null && r.User.Shift != null) ? r.User.Shift.Name : "General Shift",
                    requestedShiftId = r.RequestedShiftId,
                    requestedShiftName = r.RequestedShift != null ? r.RequestedShift.Name : "Unknown",
                    reason = r.Reason,
                    status = r.Status,
                    approverSignature = r.ApproverSignature
                })
                .ToListAsync();

            return Ok(new { requests });
        }

        [HttpPost("request")]
        public async Task<IActionResult> RequestShift([FromBody] ShiftSubmission request)
        {
            if (request == null || request.UserId <= 0 || request.RequestedShiftId <= 0)
            {
                return BadRequest(new { error = "Invalid shift request parameters." });
            }

            var user = await _context.Users.Include(u => u.Shift).FirstOrDefaultAsync(u => u.Id == request.UserId);
            if (user == null) return NotFound(new { error = "User not found." });

            var shift = await _context.Shifts.FindAsync(request.RequestedShiftId);
            if (shift == null) return NotFound(new { error = "Requested shift not found." });

            if (user.ShiftId == shift.Id)
            {
                return BadRequest(new { error = "You are already assigned to this shift." });
            }

            // Check if already requested
            var existingPending = await _context.ShiftRequests
                .Where(r => r.UserId == request.UserId && r.RequestedShiftId == request.RequestedShiftId && r.Status == "Pending")
                .FirstOrDefaultAsync();

            if (existingPending != null)
            {
                return BadRequest(new { error = "You have already applied for this shift change." });
            }

            var shiftRequest = new ShiftRequest
            {
                UserId = request.UserId,
                RequestedShiftId = request.RequestedShiftId,
                Reason = request.Reason,
                Status = "Pending"
            };

            _context.ShiftRequests.Add(shiftRequest);
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
                assignedManagers = managers;
            }

            // Create notification for employee
            _context.Notifications.Add(new Notification
            {
                UserId = request.UserId,
                Title = "Shift Change Requested",
                Message = $"Your request to change shift to {shift.Name} was submitted successfully and is pending approval.",
                Type = "success"
            });

            // Create notification for managers
            foreach (var m in assignedManagers)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = m.Id,
                    Title = "New Shift Request",
                    Message = $"{user.Name} requested shift change to {shift.Name}.",
                    Type = "warning"
                });
            }
            await _context.SaveChangesAsync();

            foreach (var m in assignedManagers)
            {
                _ = _emailService.SendTemplatedEmailAsync(m.Email, $"New Shift Change Request from {user.Name}", "LeaveRequested.html", new Dictionary<string, string>
                {
                    { "ManagerName", m.Name ?? "Manager" },
                    { "EmployeeName", user.Name ?? "Employee" },
                    { "LeaveType", $"Shift Change → {shift.Name}" },
                    { "StartDate", "Immediate" },
                    { "EndDate", "Ongoing" },
                    { "Reason", string.IsNullOrWhiteSpace(shiftRequest.Reason) ? "No reason provided" : shiftRequest.Reason },
                    { "PortalLink", "http://localhost:5173" }
                });
            }

            return Ok(new
            {
                message = "Shift change request submitted successfully!",
                request = new
                {
                    id = shiftRequest.Id,
                    userId = shiftRequest.UserId,
                    requestedShiftId = shiftRequest.RequestedShiftId,
                    requestedShiftName = shift.Name,
                    reason = shiftRequest.Reason,
                    status = shiftRequest.Status
                }
            });
        }

        [HttpPost("resolve")]
        public async Task<IActionResult> ResolveShiftRequest([FromBody] ResolveShiftRequestDto request)
        {
            if (request == null || request.Id <= 0 || string.IsNullOrWhiteSpace(request.Decision))
            {
                return BadRequest(new { error = "Invalid resolution parameters." });
            }

            var shiftReq = await _context.ShiftRequests.Include(r => r.User).Include(r => r.RequestedShift).FirstOrDefaultAsync(r => r.Id == request.Id);
            if (shiftReq == null) return NotFound(new { error = "Shift request not found." });
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

            if (shiftReq.Status == "Pending")
            {
                if (!isManager && !isAdmin) return StatusCode(403, new { error = "Unauthorized." });
                if (!isAdmin)
                {
                    var employee = shiftReq.User;
                    if (employee != null)
                    {
                        var empDept = (employee.Department ?? string.Empty).Trim().ToLower();
                        var managerDepts = (approver.Department ?? string.Empty).Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries).Select(d => d.Trim().ToLower()).ToList();
                        if (!managerDepts.Contains(empDept)) return StatusCode(403, new { error = "Unauthorized. You can only resolve requests for employees in your assigned department(s)." });
                    }
                }

                bool isApproved = normalizedDecision == "approve";
                shiftReq.TlApprovalStatus = isApproved ? "Approved" : "Rejected";
                shiftReq.TlApproverSignature = approver.Name;

                if (isApproved)
                {
                    shiftReq.Status = "Pending HR Approval";
                    shiftReq.ApproverSignature = approver.Name;

                    var hrAdmins = await _context.Users.Where(u => u.Role != null && u.Role.ToLower().Contains("admin")).ToListAsync();
                    foreach (var hr in hrAdmins)
                    {
                        _context.Notifications.Add(new Notification
                        {
                            UserId = hr.Id,
                            Title = "Shift Needs HR Approval",
                            Message = $"A Shift change request to {shiftReq.RequestedShift?.Name} was approved by TL {approver.Name} and awaits your final approval.",
                            Type = "warning"
                        });
                    }

                    notificationTitle = "Shift — TL Approved";
                    notificationMessage = $"Your Shift request was approved by your Team Lead ({approver.Name}). Awaiting HR final approval.";
                    notificationColor = "warning";
                }
                else
                {
                    shiftReq.Status = "Rejected";
                    shiftReq.HrApprovalStatus = "N/A";
                    shiftReq.ApproverSignature = approver.Name;

                    notificationTitle = "Shift Request Rejected";
                    notificationMessage = $"Your Shift request to {shiftReq.RequestedShift?.Name} was rejected by your Team Lead ({approver.Name}).";
                    notificationColor = "danger";
                }
            }
            else if (shiftReq.Status == "Pending HR Approval")
            {
                if (!isAdmin) return StatusCode(403, new { error = "Unauthorized. Only HR Admins can perform final approval." });

                bool isApproved = normalizedDecision == "approve";
                shiftReq.HrApprovalStatus = isApproved ? "Approved" : "Rejected";
                shiftReq.HrApproverSignature = approver.Name;

                if (isApproved)
                {
                    shiftReq.Status = "Approved";
                    shiftReq.ApproverSignature = approver.Name;
                    if (shiftReq.User != null)
                    {
                        shiftReq.User.ShiftId = shiftReq.RequestedShiftId;
                    }
                    notificationTitle = "Shift — HR Approved";
                    notificationMessage = $"Your Shift request to {shiftReq.RequestedShift?.Name} has been fully approved by HR.";
                    notificationColor = "success";
                }
                else
                {
                    shiftReq.Status = "Rejected";
                    shiftReq.ApproverSignature = approver.Name;
                    notificationTitle = "Shift Request Rejected";
                    notificationMessage = $"Your Shift request to {shiftReq.RequestedShift?.Name} was rejected by HR.";
                    notificationColor = "danger";
                }
            }
            else
            {
                return BadRequest(new { error = "This request has already been resolved." });
            }

            var employeeUser = shiftReq.User;
            if (employeeUser != null)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = employeeUser.Id,
                    Title = notificationTitle,
                    Message = notificationMessage,
                    Type = notificationColor
                });
            }

            await _context.SaveChangesAsync();

            if (employeeUser != null && shiftReq.Status != "Pending HR Approval")
            {
                string statusColor = shiftReq.Status == "Approved" ? "#16a34a" : "#dc2626";
                string approverName = shiftReq.ApproverSignature ?? "Management";
                _ = _emailService.SendTemplatedEmailAsync(employeeUser.Email, $"Your Shift Request has been {shiftReq.Status}", "ShiftChanged.html", new Dictionary<string, string>
                {
                    { "EmployeeName", employeeUser.Name ?? "Employee" },
                    { "Status", shiftReq.Status },
                    { "StatusColor", statusColor },
                    { "ApproverName", approverName },
                    { "ShiftName", shiftReq.RequestedShift?.Name ?? "Requested Shift" },
                    { "ShiftTiming", shiftReq.RequestedShift?.StartTime != null ? $"{shiftReq.RequestedShift.StartTime} - {shiftReq.RequestedShift.EndTime}" : "As scheduled" },
                    { "Remarks", "Your request has been reviewed." },
                    { "DecisionDate", DateTime.Now.ToString("dd MMM yyyy") }
                });
            }

            return Ok(new
            {
                message = $"Request successfully processed!",
                request = new
                {
                    id = shiftReq.Id,
                    status = shiftReq.Status
                }
            });
        }
    }
}
