using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ams;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;

namespace Ams.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReportsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ReportsController(AppDbContext context)
        {
            _context = context;
        }

        public class DailyAttendanceReportDto
        {
            public string EmployeeId { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Department { get; set; } = string.Empty;
            public string Shift { get; set; } = string.Empty;
            public string PunchIn { get; set; } = string.Empty;
            public string PunchOut { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
        }

        [HttpGet("daily-attendance")]
        public async Task<ActionResult<IEnumerable<DailyAttendanceReportDto>>> GetDailyAttendance([FromQuery] string? date)
        {
            if (string.IsNullOrEmpty(date))
            {
                date = DateTime.Now.ToString("yyyy-MM-dd");
            }

            var activeUsers = await _context.Users.Include(u => u.Shift).Where(u => u.IsActive).ToListAsync();
            var logsForDate = await _context.AttendanceLogs.Where(l => l.Date == date).ToListAsync();

            var report = new List<DailyAttendanceReportDto>();

            foreach (var user in activeUsers)
            {
                var log = logsForDate.FirstOrDefault(l => l.UserId == user.Id);
                
                string status = "Absent";
                string punchIn = "--:--";
                string punchOut = "--:--";

                if (log != null)
                {
                    punchIn = string.IsNullOrEmpty(log.ClockIn) ? "--:--" : log.ClockIn;
                    punchOut = string.IsNullOrEmpty(log.ClockOut) ? "--:--" : log.ClockOut;

                    if (log.Status == "Active")
                    {
                        status = "Active";
                    }
                    else if (log.Status == "On Leave" || log.Status == "Leave")
                    {
                        status = "On Leave";
                    }
                    else
                    {
                        if (log.LateEntry && log.EarlyExit)
                        {
                            status = "Late & Early";
                        }
                        else if (log.LateEntry)
                        {
                            status = "Late";
                        }
                        else if (log.EarlyExit)
                        {
                            status = "Early";
                        }
                        else if (log.Status == "Present")
                        {
                            status = "Present";
                        }
                        else
                        {
                            status = log.Status;
                        }
                    }
                }

                report.Add(new DailyAttendanceReportDto
                {
                    EmployeeId = string.IsNullOrEmpty(user.EmployeeId) ? "N/A" : user.EmployeeId,
                    Name = user.Name ?? "Unknown",
                    Department = string.IsNullOrEmpty(user.Department) ? "Unassigned" : user.Department,
                    Shift = user.Shift != null ? $"{user.Shift.Name} ({user.Shift.StartTime} - {user.Shift.EndTime})" : "---",
                    PunchIn = punchIn,
                    PunchOut = punchOut,
                    Status = status
                });
            }

            return Ok(report);
        }
    }
}
