using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ams;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Ams.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DepartmentsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public DepartmentsController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/Departments
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Department>>> GetDepartments()
        {
            return await _context.Departments.AsNoTracking().ToListAsync();
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetDepartmentStats()
        {
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

            return Ok(dynamicDeptStats);
        }

        // POST: api/Departments
        [HttpPost]
        public async Task<ActionResult<Department>> PostDepartment([FromBody] Department department)
        {
            if (string.IsNullOrWhiteSpace(department.Name))
            {
                return BadRequest(new { error = "Department name is required" });
            }

            _context.Departments.Add(department);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetDepartments), new { id = department.Id }, department);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteDepartment(int id)
        {
            var department = await _context.Departments.FindAsync(id);
            if (department == null)
            {
                return NotFound(new { error = "Department not found" });
            }

            // Remove this department from all users who have it
            var usersInDept = await _context.Users.Where(u => u.Department == department.Name).ToListAsync();
            foreach(var user in usersInDept)
            {
                user.Department = "Unassigned";
            }

            _context.Departments.Remove(department);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
