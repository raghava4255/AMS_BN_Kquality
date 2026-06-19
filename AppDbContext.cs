using Microsoft.EntityFrameworkCore;

namespace Ams
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<AttendanceLog> AttendanceLogs { get; set; }
        public DbSet<LeaveRequest> LeaveRequests { get; set; }
        public DbSet<Shift> Shifts { get; set; }
        public DbSet<Department> Departments { get; set; }
        public DbSet<FlexyHourRequest> FlexyHourRequests { get; set; }
        public DbSet<ShiftRequest> ShiftRequests { get; set; }
        public DbSet<Otp> Otps { get; set; }
        public DbSet<PasswordHistory> PasswordHistories { get; set; }
        public DbSet<PasswordPolicy> PasswordPolicies { get; set; }
        public DbSet<EmailLog> EmailLogs { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<LeaveType> LeaveTypes { get; set; }
        public DbSet<IncomingEmail> IncomingEmails { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Email as unique
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            // Set up cascade deletes
            modelBuilder.Entity<User>()
                .HasMany(u => u.AttendanceLogs)
                .WithOne(a => a.User)
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<User>()
                .HasMany(u => u.LeaveRequests)
                .WithOne(l => l.User)
                .HasForeignKey(l => l.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<User>()
                .HasMany(u => u.FlexyHourRequests)
                .WithOne(f => f.User)
                .HasForeignKey(f => f.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Otp>()
                .HasOne(o => o.User)
                .WithMany()
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PasswordHistory>()
                .HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Notification>()
                .HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ShiftRequest>()
                .HasOne(sr => sr.User)
                .WithMany()
                .HasForeignKey(sr => sr.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ShiftRequest>()
                .HasOne(sr => sr.RequestedShift)
                .WithMany()
                .HasForeignKey(sr => sr.RequestedShiftId)
                .OnDelete(DeleteBehavior.Restrict);

        }
    }
}
