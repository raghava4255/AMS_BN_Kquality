using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using Ams.Services;

namespace Ams.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EmailsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IEmailService _emailService;

        public EmailsController(AppDbContext context, IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        [HttpGet("logs")]
        public async Task<IActionResult> GetEmailLogs()
        {
            var logs = await _context.EmailLogs.AsNoTracking()
                .OrderByDescending(e => e.SentAt)
                .Take(200)
                .Select(e => new {
                    id = e.Id,
                    recipientEmail = e.RecipientEmail,
                    subject = e.Subject,
                    status = e.Status,
                    sentAt = e.SentAt,
                    retryCount = e.RetryCount,
                    errorMessage = e.ErrorMessage
                })
                .ToListAsync();

            return Ok(logs);
        }

        [HttpPost("retry/{id}")]
        public async Task<IActionResult> RetryEmail(int id)
        {
            var log = await _context.EmailLogs.FindAsync(id);
            if (log == null) return NotFound(new { error = "Email log not found." });

            if (log.Status == "Sent") return BadRequest(new { error = "Email has already been sent." });

            await _emailService.RetryEmailAsync(id);
            return Ok(new { message = "Retry attempt executed." });
        }
    }
}
