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

        public FlexyHoursController(AppDbContext context)
        {
            _context = context;
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
        }

        // GET /api/flexyhours/user/{userId} - Fetch employee's flexy requests
        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetUserFlexyRequests(int userId)
        {
            if (userId <= 0) return BadRequest(new { error = "Invalid User ID." });

            var requests = await _context.FlexyHourRequests
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
        public async Task<IActionResult> GetPendingRequests()
        {
            var requests = await _context.FlexyHourRequests
                .Where(f => f.Status == "Pending")
                .Include(f => f.User)
                .OrderBy(f => f.Id)
                .Select(f => new
                {
                    id = f.Id,
                    userId = f.UserId,
                    userName = f.User.Name,
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
            if (flexy.Status != "Pending") return BadRequest(new { error = "This request has already been resolved." });

            string decision = request.Decision.Trim().ToLower();
            if (decision == "approve") flexy.Status = "Approved";
            else if (decision == "reject") flexy.Status = "Rejected";
            else return BadRequest(new { error = "Decision must be 'approve' or 'reject'." });

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = $"Request successfully {flexy.Status}!",
                request = new
                {
                    id = flexy.Id,
                    status = flexy.Status
                }
            });
        }
    }
}
