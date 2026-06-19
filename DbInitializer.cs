using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace Ams
{
    public static class DbInitializer
    {
        public static void Initialize(AppDbContext context)
        {
            // Add IsActive to Users table if missing
            context.Database.ExecuteSqlRaw(@"
                IF EXISTS (SELECT * FROM sysobjects WHERE name='Users' and xtype='U')
                AND NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'IsActive' AND Object_ID = Object_ID(N'Users'))
                BEGIN
                    ALTER TABLE Users ADD IsActive BIT NOT NULL DEFAULT 1
                END
            ");

            // Add EmployeeId to Users table if missing
            context.Database.ExecuteSqlRaw(@"
                IF EXISTS (SELECT * FROM sysobjects WHERE name='Users' and xtype='U')
                AND NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'EmployeeId' AND Object_ID = Object_ID(N'Users'))
                BEGIN
                    ALTER TABLE Users ADD EmployeeId NVARCHAR(100) NOT NULL DEFAULT '';
                END
            ");

            // Seed initial EmployeeIds for already existing users who have empty EmployeeId
            context.Database.ExecuteSqlRaw(@"
                UPDATE Users SET EmployeeId = 'EMP-001' WHERE Email = 'employee@company.com' AND (EmployeeId = '' OR EmployeeId IS NULL);
                UPDATE Users SET EmployeeId = 'MGR-001' WHERE Email = 'manager@company.com' AND (EmployeeId = '' OR EmployeeId IS NULL);
                UPDATE Users SET EmployeeId = 'ADM-001' WHERE Email = 'admin@company.com' AND (EmployeeId = '' OR EmployeeId IS NULL);
            ");

            // Add Location fields to AttendanceLogs table if missing
            context.Database.ExecuteSqlRaw(@"
                IF EXISTS (SELECT * FROM sysobjects WHERE name='AttendanceLogs' and xtype='U')
                BEGIN
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'ClockInLat' AND Object_ID = Object_ID(N'AttendanceLogs'))
                        ALTER TABLE AttendanceLogs ADD ClockInLat FLOAT NULL;
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'ClockInLng' AND Object_ID = Object_ID(N'AttendanceLogs'))
                        ALTER TABLE AttendanceLogs ADD ClockInLng FLOAT NULL;
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'ClockInAddress' AND Object_ID = Object_ID(N'AttendanceLogs'))
                        ALTER TABLE AttendanceLogs ADD ClockInAddress NVARCHAR(MAX) NULL;
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'ClockOutLat' AND Object_ID = Object_ID(N'AttendanceLogs'))
                        ALTER TABLE AttendanceLogs ADD ClockOutLat FLOAT NULL;
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'ClockOutLng' AND Object_ID = Object_ID(N'AttendanceLogs'))
                        ALTER TABLE AttendanceLogs ADD ClockOutLng FLOAT NULL;
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'ClockOutAddress' AND Object_ID = Object_ID(N'AttendanceLogs'))
                        ALTER TABLE AttendanceLogs ADD ClockOutAddress NVARCHAR(MAX) NULL;
                END
            ");

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

            // Initialize ShiftRequests if table missing
            context.Database.ExecuteSqlRaw(@"
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ShiftRequests' and xtype='U')
                BEGIN
                    CREATE TABLE ShiftRequests (
                        Id INT IDENTITY(1,1) PRIMARY KEY,
                        UserId INT NOT NULL,
                        RequestedShiftId INT NOT NULL,
                        Reason NVARCHAR(MAX) NULL,
                        Status NVARCHAR(50) NOT NULL DEFAULT 'Pending',
                        FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE,
                        FOREIGN KEY (RequestedShiftId) REFERENCES Shifts(Id)
                    )
                END
            ");

            // Initialize Otps if table missing
            context.Database.ExecuteSqlRaw(@"
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Otps' and xtype='U')
                BEGIN
                    CREATE TABLE Otps (
                        Id INT IDENTITY(1,1) PRIMARY KEY,
                        UserId INT NOT NULL,
                        OtpCode NVARCHAR(10) NOT NULL,
                        ExpiryTime DATETIME NOT NULL,
                        IsUsed BIT NOT NULL DEFAULT 0,
                        FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
                    )
                END
            ");

            // Initialize PasswordHistories if table missing
            context.Database.ExecuteSqlRaw(@"
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='PasswordHistories' and xtype='U')
                BEGIN
                    CREATE TABLE PasswordHistories (
                        Id INT IDENTITY(1,1) PRIMARY KEY,
                        UserId INT NOT NULL,
                        PasswordHash NVARCHAR(255) NOT NULL,
                        CreatedAt DATETIME NOT NULL DEFAULT GETDATE(),
                        FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
                    )
                END
            ");

            // Initialize PasswordPolicies if table missing
            context.Database.ExecuteSqlRaw(@"
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='PasswordPolicies' and xtype='U')
                BEGIN
                    CREATE TABLE PasswordPolicies (
                        Id INT IDENTITY(1,1) PRIMARY KEY,
                        MinLength INT NOT NULL DEFAULT 8,
                        MaxLength INT NOT NULL DEFAULT 64,
                        RequireUpper BIT NOT NULL DEFAULT 1,
                        RequireLower BIT NOT NULL DEFAULT 1,
                        RequireNumber BIT NOT NULL DEFAULT 1,
                        RequireSpecial BIT NOT NULL DEFAULT 1
                    )
                END
            ");

            // Initialize EmailLogs if table missing
            context.Database.ExecuteSqlRaw(@"
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='EmailLogs' and xtype='U')
                BEGIN
                    CREATE TABLE EmailLogs (
                        Id INT IDENTITY(1,1) PRIMARY KEY,
                        RecipientEmail NVARCHAR(255) NOT NULL,
                        Subject NVARCHAR(255) NOT NULL,
                        Body NVARCHAR(MAX) NOT NULL,
                        Status NVARCHAR(50) NOT NULL DEFAULT 'Pending',
                        SentAt DATETIME NOT NULL DEFAULT GETDATE(),
                        ErrorMessage NVARCHAR(MAX) NULL,
                        RetryCount INT NOT NULL DEFAULT 0
                    )
                END
            ");

            // Initialize Notifications if table missing
            context.Database.ExecuteSqlRaw(@"
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Notifications' and xtype='U')
                BEGIN
                    CREATE TABLE Notifications (
                        Id INT IDENTITY(1,1) PRIMARY KEY,
                        UserId INT NOT NULL,
                        Title NVARCHAR(255) NOT NULL,
                        Message NVARCHAR(MAX) NOT NULL,
                        Type NVARCHAR(50) NOT NULL DEFAULT 'info',
                        CreatedAt DATETIME NOT NULL DEFAULT GETDATE(),
                        IsRead BIT NOT NULL DEFAULT 0,
                        FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
                    )
                END
            ");

            // Initialize LeaveTypes if table missing
            context.Database.ExecuteSqlRaw(@"
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='LeaveTypes' and xtype='U')
                BEGIN
                    CREATE TABLE LeaveTypes (
                        Id INT IDENTITY(1,1) PRIMARY KEY,
                        Name NVARCHAR(100) NOT NULL
                    )
                END
            ");

            // Seed default LeaveTypes if empty
            if (!context.LeaveTypes.Any())
            {
                context.LeaveTypes.AddRange(
                    new LeaveType { Name = "Sick Leave" },
                    new LeaveType { Name = "Casual Leave" },
                    new LeaveType { Name = "Earned Leave" },
                    new LeaveType { Name = "Maternity Leave" }
                );
                context.SaveChanges();
            }

            // Seed default PasswordPolicy if empty
            if (!context.PasswordPolicies.Any())
            {
                context.PasswordPolicies.Add(new PasswordPolicy
                {
                    MinLength = 8,
                    MaxLength = 64,
                    RequireUpper = true,
                    RequireLower = true,
                    RequireNumber = true,
                    RequireSpecial = true
                });
                context.SaveChanges();
            }

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
                    ShiftId = generalShift?.Id,
                    EmployeeId = "EMP-001"
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
                    ShiftId = generalShift?.Id,
                    EmployeeId = "MGR-001"
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
                    ShiftId = generalShift?.Id,
                    EmployeeId = "ADM-001"
                };

                context.Users.AddRange(employee, manager, admin);
                context.SaveChanges(); // Generates IDs for users!

                // Seed logs for Employee
                context.AttendanceLogs.AddRange(
                    new AttendanceLog { UserId = employee.Id, Date = "2026-05-20", ClockIn = "09:02", ClockOut = "18:05", Status = "Present", Hours = 9.0, ClockInLat = 17.5314223, ClockInLng = 78.3951969, ClockInAddress = "ALEAP, Pragathi Nagar, Bachupally mandal, Greater Hyderabad Municipal Corporation North Zone, Hyderabad, Medchal-Malkajgiri, Telangana, 501002, India", ClockOutLat = 17.5314223, ClockOutLng = 78.3951969, ClockOutAddress = "ALEAP, Pragathi Nagar, Bachupally mandal, Greater Hyderabad Municipal Corporation North Zone, Hyderabad, Medchal-Malkajgiri, Telangana, 501002, India" },
                    new AttendanceLog { UserId = employee.Id, Date = "2026-05-19", ClockIn = "08:55", ClockOut = "17:45", Status = "Present", Hours = 8.8, ClockInLat = 17.5314223, ClockInLng = 78.3951969, ClockInAddress = "ALEAP, Pragathi Nagar, Bachupally mandal, Greater Hyderabad Municipal Corporation North Zone, Hyderabad, Medchal-Malkajgiri, Telangana, 501002, India", ClockOutLat = 17.5314223, ClockOutLng = 78.3951969, ClockOutAddress = "ALEAP, Pragathi Nagar, Bachupally mandal, Greater Hyderabad Municipal Corporation North Zone, Hyderabad, Medchal-Malkajgiri, Telangana, 501002, India" },
                    new AttendanceLog { UserId = employee.Id, Date = "2026-05-18", ClockIn = "09:15", ClockOut = "18:10", Status = "Present", Hours = 8.9, ClockInLat = 17.5314223, ClockInLng = 78.3951969, ClockInAddress = "ALEAP, Pragathi Nagar, Bachupally mandal, Greater Hyderabad Municipal Corporation North Zone, Hyderabad, Medchal-Malkajgiri, Telangana, 501002, India", ClockOutLat = 17.5314223, ClockOutLng = 78.3951969, ClockOutAddress = "ALEAP, Pragathi Nagar, Bachupally mandal, Greater Hyderabad Municipal Corporation North Zone, Hyderabad, Medchal-Malkajgiri, Telangana, 501002, India" },
                    new AttendanceLog { UserId = employee.Id, Date = "2026-05-17", ClockIn = "---", ClockOut = "---", Status = "On Leave", Hours = 0.0 },
                    new AttendanceLog { UserId = employee.Id, Date = "2026-05-16", ClockIn = "08:58", ClockOut = "17:30", Status = "Present", Hours = 8.5, ClockInLat = 17.5314223, ClockInLng = 78.3951969, ClockInAddress = "ALEAP, Pragathi Nagar, Bachupally mandal, Greater Hyderabad Municipal Corporation North Zone, Hyderabad, Medchal-Malkajgiri, Telangana, 501002, India", ClockOutLat = 17.5314223, ClockOutLng = 78.3951969, ClockOutAddress = "ALEAP, Pragathi Nagar, Bachupally mandal, Greater Hyderabad Municipal Corporation North Zone, Hyderabad, Medchal-Malkajgiri, Telangana, 501002, India" }
                );

                // Seed leave requests for Manager's inbox
                context.LeaveRequests.AddRange(
                    new LeaveRequest { UserId = employee.Id, Name = "Sarah Connor", Type = "Sick Leave", Duration = "2 days (May 24-25)", Reason = "Dental surgery recovery", Status = "Pending" },
                    new LeaveRequest { UserId = employee.Id, Name = "Sarah Connor", Type = "Annual Leave", Duration = "5 days (Jun 01-05)", Reason = "Family vacation", Status = "Pending" }
                );

                context.SaveChanges();
            }

            // Sync password history for existing users who do not have any history
            var usersWithoutHistory = context.Users.Where(u => !context.PasswordHistories.Any(ph => ph.UserId == u.Id)).ToList();
            if (usersWithoutHistory.Any())
            {
                foreach (var u in usersWithoutHistory)
                {
                    context.PasswordHistories.Add(new PasswordHistory { UserId = u.Id, PasswordHash = u.Password, CreatedAt = System.DateTime.Now });
                }
                context.SaveChanges();
            }


        }
    }
}
