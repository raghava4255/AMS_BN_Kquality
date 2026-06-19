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
        [MaxLength(100)]
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
        public bool IsActive { get; set; } = true;

        [MaxLength(100)]
        public string EmployeeId { get; set; } = string.Empty;

        // Geofencing constraints
        public double? AllowedLat { get; set; }
        public double? AllowedLng { get; set; }
        public double AllowedRadius { get; set; } = 500.0; // Default 500 meters

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

        public double? ClockInLat { get; set; }
        public double? ClockInLng { get; set; }
        public string? ClockInAddress { get; set; }

        public double? ClockOutLat { get; set; }
        public double? ClockOutLng { get; set; }
        public string? ClockOutAddress { get; set; }

        public double Hours { get; set; } = 0;

        // Shift calculations
        public bool LateEntry { get; set; } = false;
        public bool EarlyExit { get; set; } = false;
        public double OvertimeHours { get; set; } = 0;
        public double BreakDuration { get; set; } = 0; // In hours or minutes, let's keep hours for consistency with `Hours` or we can use the shift's break time.
        public double PendingHours { get; set; } = 0;
        public bool MissedPunchEmailSent { get; set; } = false;
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
        // Overall status: "Pending" | "Pending HR Approval" | "Approved" | "Rejected"

        // Stage 1: Team Lead (Manager) approval
        [MaxLength(50)]
        public string TlApprovalStatus { get; set; } = "Pending";
        // "Pending" | "Approved" | "Rejected"

        public string TlApproverSignature { get; set; } = string.Empty;

        // Stage 2: HR (Admin) approval
        [MaxLength(50)]
        public string HrApprovalStatus { get; set; } = "Pending";
        // "Pending" | "Approved" | "Rejected"

        public string HrApproverSignature { get; set; } = string.Empty;

        // Legacy field kept for backward compatibility
        public string ApproverSignature { get; set; } = string.Empty;
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

        public string ApproverSignature { get; set; } = string.Empty;

        // Stage 1: Team Lead (Manager) approval
        [MaxLength(50)]
        public string TlApprovalStatus { get; set; } = "Pending";
        public string TlApproverSignature { get; set; } = string.Empty;

        // Stage 2: HR (Admin) approval
        [MaxLength(50)]
        public string HrApprovalStatus { get; set; } = "Pending";
        public string HrApproverSignature { get; set; } = string.Empty;
    }

    public class Otp
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
        [MaxLength(10)]
        public string OtpCode { get; set; } = string.Empty;

        [Required]
        public System.DateTime ExpiryTime { get; set; }

        public bool IsUsed { get; set; } = false;
    }

    public class PasswordHistory
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
        [MaxLength(255)]
        public string PasswordHash { get; set; } = string.Empty;

        public System.DateTime CreatedAt { get; set; } = System.DateTime.Now;
    }

    public class EmailLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(255)]
        public string RecipientEmail { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string Subject { get; set; } = string.Empty;

        [Required]
        public string Body { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = "Pending";

        public System.DateTime SentAt { get; set; } = System.DateTime.Now;

        public string ErrorMessage { get; set; } = string.Empty;

        public int RetryCount { get; set; } = 0;
    }

    public class Notification
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
        [MaxLength(255)]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Message { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Type { get; set; } = "info"; // "info", "success", "warning", "danger"

        public System.DateTime CreatedAt { get; set; } = System.DateTime.Now;

        public bool IsRead { get; set; } = false;

        public bool IsEmailSent { get; set; } = false;
    }

    public class ShiftRequest
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
        public int RequestedShiftId { get; set; }

        [ForeignKey("RequestedShiftId")]
        public Shift? RequestedShift { get; set; }

        public string Reason { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected

        public string ApproverSignature { get; set; } = string.Empty;

        // Stage 1: Team Lead (Manager) approval
        [MaxLength(50)]
        public string TlApprovalStatus { get; set; } = "Pending";
        public string TlApproverSignature { get; set; } = string.Empty;

        // Stage 2: HR (Admin) approval
        [MaxLength(50)]
        public string HrApprovalStatus { get; set; } = "Pending";
        public string HrApproverSignature { get; set; } = string.Empty;
    }

    public class LeaveType
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;
    }

    public class IncomingEmail
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(255)]
        public string MessageId { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string SenderAddress { get; set; } = string.Empty;

        [MaxLength(255)]
        public string SenderName { get; set; } = string.Empty;

        [Required]
        [MaxLength(500)]
        public string Subject { get; set; } = string.Empty;

        public string BodyText { get; set; } = string.Empty;
        
        public string BodyHtml { get; set; } = string.Empty;

        public System.DateTime ReceivedDate { get; set; }

        public bool IsProcessed { get; set; } = false;
        
        public System.DateTime CreatedAt { get; set; } = System.DateTime.Now;
    }

    public class PasswordPolicy
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public int MinLength { get; set; } = 8;
        public int MaxLength { get; set; } = 64;
        public bool RequireUpper { get; set; } = true;
        public bool RequireLower { get; set; } = true;
        public bool RequireNumber { get; set; } = true;
        public bool RequireSpecial { get; set; } = true;
    }
}

