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
            return await _context.Departments.ToListAsync();
        }

        // GET: api/Departments/stats
        [HttpGet("stats")]
        public async Task<IActionResult> GetDepartmentStats()
        {
            var allDepts = await _context.Departments.ToListAsync();
            var dynamicDeptStats = new List<object>();
            foreach (var d in allDepts)
            {
                int deptCount = await _context.Users.CountAsync(u => u.Department == d.Name);
                // Use a mock attendance rate for now
                dynamicDeptStats.Add(new { name = d.Name, count = deptCount, attendance = "96.0%" });
            }
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

        // DELETE: api/Departments/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteDepartment(int id)
        {
            var department = await _context.Departments.FindAsync(id);
            if (department == null)
            {
                return NotFound(new { error = "Department not found" });
            }

            _context.Departments.Remove(department);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
