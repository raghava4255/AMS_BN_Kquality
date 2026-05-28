using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace Ams
{
    public static class DbInitializer
    {
        public static void Initialize(AppDbContext context)
        {
            // Initialize Departments if table missing or empty
            context.Database.ExecuteSqlRaw(@"
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Departments' and xtype='U')
                BEGIN
                    CREATE TABLE Departments (
                        Id INT IDENTITY(1,1) PRIMARY KEY,
                        Name NVARCHAR(100) NOT NULL
                    )
                END
                
                -- Sync missing departments from Users
                IF EXISTS (SELECT * FROM sysobjects WHERE name='Users' and xtype='U')
                BEGIN
                    INSERT INTO Departments (Name)
                    SELECT DISTINCT Department FROM Users
                    WHERE Department NOT IN (SELECT Name FROM Departments)
                END

                -- Clean up unused old default departments
                DELETE FROM Departments 
                WHERE Name IN ('Marketing', 'Sales', 'HR & Ops')
                AND Name NOT IN (SELECT DISTINCT Department FROM Users)
            ");

            // Initialize FlexyHourRequests if table missing
            context.Database.ExecuteSqlRaw(@"
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='FlexyHourRequests' and xtype='U')
                BEGIN
                    CREATE TABLE FlexyHourRequests (
                        Id INT IDENTITY(1,1) PRIMARY KEY,
                        UserId INT NOT NULL,
                        Date NVARCHAR(50) NOT NULL,
                        Type NVARCHAR(50) NOT NULL,
                        HoursRequested INT NOT NULL,
                        Reason NVARCHAR(MAX) NULL,
                        Status NVARCHAR(50) NOT NULL DEFAULT 'Pending',
                        FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
                    )
                END
            ");

            // Seed Shifts
            if (!context.Shifts.Any())
            {
                context.Shifts.AddRange(
                    new Shift { Name = "General Shift", Code = "GS-01", StartTime = "09:00", EndTime = "18:00", BreakTime = 60, GraceTime = 15, ShiftType = "General", WeeklyOffs = "Saturday,Sunday", Status = "Active" },
                    new Shift { Name = "Night Shift", Code = "NS-01", StartTime = "22:00", EndTime = "06:00", BreakTime = 30, GraceTime = 10, ShiftType = "Night", WeeklyOffs = "Sunday", Status = "Active" }
                );
                context.SaveChanges();
            }

            var generalShift = context.Shifts.FirstOrDefault(s => s.Code == "GS-01");
            var nightShift = context.Shifts.FirstOrDefault(s => s.Code == "NS-01");

            // Seed Users if none exist
            if (!context.Users.Any())
            {
                var employee = new User
                {
                    Email = "employee@company.com",
                    Password = "password123",
                    Role = "employee",
                    Name = "Sarah Connor",
                    Department = "Engineering",
                    Avatar = "https://images.unsplash.com/photo-1494790108377-be9c29b29330?w=150",
                    PresentDays = 18,
                    AbsentDays = 2,
                    LeaveBalance = 5,
                    WorkHoursThisMonth = 144,
                    AttendanceRate = 90,
                    ShiftId = generalShift?.Id
                };

                var manager = new User
                {
                    Email = "manager@company.com",
                    Password = "password123",
                    Role = "manager",
                    Name = "Marcus Wright",
                    Department = "Operations",
                    Avatar = "https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?w=150",
                    PresentDays = 20,
                    AbsentDays = 0,
                    LeaveBalance = 8,
                    WorkHoursThisMonth = 160,
                    AttendanceRate = 100,
                    ShiftId = generalShift?.Id
                };

                var admin = new User
                {
                    Email = "admin@company.com",
                    Password = "password123",
                    Role = "admin",
                    Name = "Elena Rostova",
                    Department = "HR & Administration",
                    Avatar = "https://images.unsplash.com/photo-1573496359142-b8d87734a5a2?w=150",
                    PresentDays = 20,
                    AbsentDays = 0,
                    LeaveBalance = 10,
                    WorkHoursThisMonth = 160,
                    AttendanceRate = 100,
                    ShiftId = generalShift?.Id
                };

                context.Users.AddRange(employee, manager, admin);
                context.SaveChanges(); // Generates IDs for users!

                // Seed logs for Employee
                context.AttendanceLogs.AddRange(
                    new AttendanceLog { UserId = employee.Id, Date = "2026-05-20", ClockIn = "09:02 AM", ClockOut = "06:05 PM", Status = "Present", Hours = 9.0 },
                    new AttendanceLog { UserId = employee.Id, Date = "2026-05-19", ClockIn = "08:55 AM", ClockOut = "05:45 PM", Status = "Present", Hours = 8.8 },
                    new AttendanceLog { UserId = employee.Id, Date = "2026-05-18", ClockIn = "09:15 AM", ClockOut = "06:10 PM", Status = "Present", Hours = 8.9 },
                    new AttendanceLog { UserId = employee.Id, Date = "2026-05-17", ClockIn = "---", ClockOut = "---", Status = "On Leave", Hours = 0.0 },
                    new AttendanceLog { UserId = employee.Id, Date = "2026-05-16", ClockIn = "08:58 AM", ClockOut = "05:30 PM", Status = "Present", Hours = 8.5 }
                );

                // Seed leave requests for Manager's inbox
                context.LeaveRequests.AddRange(
                    new LeaveRequest { UserId = employee.Id, Name = "Sarah Connor", Type = "Sick Leave", Duration = "2 days (May 24-25)", Reason = "Dental surgery recovery", Status = "Pending" },
                    new LeaveRequest { UserId = employee.Id, Name = "Sarah Connor", Type = "Annual Leave", Duration = "5 days (Jun 01-05)", Reason = "Family vacation", Status = "Pending" }
                );

                context.SaveChanges();
            }
        }
    }
}
