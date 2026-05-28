using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Ams
{
    public class User
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(255)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Role { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Department { get; set; } = string.Empty;

        public string Avatar { get; set; } = string.Empty;

        public int PresentDays { get; set; } = 0;
        public int AbsentDays { get; set; } = 0;
        public int LeaveBalance { get; set; } = 5;
        public double WorkHoursThisMonth { get; set; } = 0;
        public double AttendanceRate { get; set; } = 100.0;

        public int? ShiftId { get; set; }

        [ForeignKey("ShiftId")]
        public Shift? Shift { get; set; }

        [JsonIgnore]
        public List<AttendanceLog> AttendanceLogs { get; set; } = new List<AttendanceLog>();

        [JsonIgnore]
        public List<LeaveRequest> LeaveRequests { get; set; } = new List<LeaveRequest>();

        [JsonIgnore]
        public List<FlexyHourRequest> FlexyHourRequests { get; set; } = new List<FlexyHourRequest>();
    }

    public class Shift
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string StartTime { get; set; } = string.Empty; // e.g. "09:00"

        [Required]
        [MaxLength(20)]
        public string EndTime { get; set; } = string.Empty; // e.g. "18:00"

        public int BreakTime { get; set; } = 0; // in minutes
        public int GraceTime { get; set; } = 0; // in minutes

        [Required]
        [MaxLength(50)]
        public string ShiftType { get; set; } = "General"; // Day, Night, General, Custom

        [MaxLength(100)]
        public string WeeklyOffs { get; set; } = "Saturday,Sunday";

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = "Active"; // Active, Inactive
    }

    public class AttendanceLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [ForeignKey("UserId")]
        [JsonIgnore]
        public User? User { get; set; }

        [Required]
        [MaxLength(50)]
        public string Date { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string ClockIn { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string ClockOut { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = string.Empty;

        public double Hours { get; set; } = 0;

        // Shift calculations
        public bool LateEntry { get; set; } = false;
        public bool EarlyExit { get; set; } = false;
        public double OvertimeHours { get; set; } = 0;
        public double BreakDuration { get; set; } = 0; // In hours or minutes, let's keep hours for consistency with `Hours` or we can use the shift's break time.
        public double PendingHours { get; set; } = 0;
    }

    public class LeaveRequest
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [ForeignKey("UserId")]
        [JsonIgnore]
        public User? User { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string Type { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string Duration { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = "Pending";
    }

    public class Department
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;
    }

    public class FlexyHourRequest
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [ForeignKey("UserId")]
        [JsonIgnore]
        public User? User { get; set; }

        [Required]
        [MaxLength(50)]
        public string Date { get; set; } = string.Empty; // e.g. "yyyy-MM-dd"

        [Required]
        [MaxLength(50)]
        public string Type { get; set; } = string.Empty; // "Morning Flexy" or "Evening Flexy"

        public int HoursRequested { get; set; } = 0;

        public string Reason { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = "Pending";
    }
}
