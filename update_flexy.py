import re

file_path = r'c:\Users\VINAY DURGA\OneDrive\Desktop\attendance\AMS\Controllers\FlexyHoursController.cs'
with open(file_path, 'r', encoding='utf-8') as f:
    content = f.read()

new_resolve = '''        public async Task<IActionResult> ResolveFlexyRequest([FromBody] ResolveFlexyDto request)
        {
            if (request == null || request.Id <= 0 || string.IsNullOrWhiteSpace(request.Decision))
                return BadRequest(new { error = "Invalid resolution parameters." });

            var flexy = await _context.FlexyHourRequests.FindAsync(request.Id);
            if (flexy == null) return NotFound(new { error = "Request not found." });

            if (!request.ManagerId.HasValue) return BadRequest(new { error = "Approver ID is required." });
            if (string.IsNullOrWhiteSpace(request.Signature)) return BadRequest(new { error = "An E-Signature is required to approve or reject." });

            var approver = await _context.Users.FindAsync(request.ManagerId.Value);
            if (approver == null) return Unauthorized(new { error = "Approver not found." });
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

                flexy.TlApprovalStatus = normalizedDecision == "approve" ? "Approved" : "Rejected";
                flexy.TlApproverSignature = request.Signature;

                if (normalizedDecision == "approve")
                {
                    flexy.Status = "Pending HR Approval";
                    flexy.ApproverSignature = request.Signature;

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
                    flexy.ApproverSignature = request.Signature;

                    notificationTitle = "Flexy Request Rejected";
                    notificationMessage = $"Your Flexy request for {flexy.Date} was rejected by your Team Lead ({approver.Name}).";
                    notificationColor = "danger";
                }
            }
            else if (flexy.Status == "Pending HR Approval")
            {
                if (!isAdmin) return StatusCode(403, new { error = "Unauthorized. Only HR Admins can perform final approval." });

                flexy.HrApprovalStatus = normalizedDecision == "approve" ? "Approved" : "Rejected";
                flexy.HrApproverSignature = request.Signature;

                if (normalizedDecision == "approve")
                {
                    flexy.Status = "Approved";
                    flexy.ApproverSignature = request.Signature;
                    notificationTitle = "Flexy — HR Approved";
                    notificationMessage = $"Your Flexy request for {flexy.Date} has been fully approved by HR.";
                    notificationColor = "success";
                }
                else
                {
                    flexy.Status = "Rejected";
                    flexy.ApproverSignature = request.Signature;
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
        }'''

pattern = r'public async Task<IActionResult> ResolveFlexyRequest\(\[FromBody\] ResolveFlexyDto request\)\s*\{.*?(?=\s*// GET /api/flexyhours -)'
new_content = re.sub(pattern, new_resolve, content, flags=re.DOTALL)

with open(file_path, 'w', encoding='utf-8') as f:
    f.write(new_content)
print("Updated FlexyHoursController")
