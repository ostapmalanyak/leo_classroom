using LeoClassroom.Persistence.Model;
using LeoClassroom.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LeoClassroom.Persistence.Util;

public sealed class DatabaseContext(DbContextOptions<DatabaseContext> options) : DbContext(options)
{
    public const string SchemaName = "leo_classroom";

    public DbSet<User> Users { get; set; }
    public DbSet<Roster> Rosters { get; set; }
    public DbSet<Course> Courses { get; set; }
    public DbSet<Assignment> Assignments { get; set; }
    public DbSet<Acceptance> Acceptances { get; set; }
    public DbSet<WebhookEvent> WebhookEvents { get; set; }
    public DbSet<AuditEvent> AuditEvents { get; set; }
    public DbSet<NotificationPreference> NotificationPreferences { get; set; }
    public DbSet<Notification> Notifications { get; set; }
    public DbSet<MoodleLink> MoodleLinks { get; set; }
    public DbSet<MoodleSyncOp> MoodleSyncOps { get; set; }
    public DbSet<MoodleItemMapping> MoodleItemMappings { get; set; }
    public DbSet<DownloadJob> DownloadJobs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(SchemaName);

        ConfigureUser(modelBuilder);
        ConfigureRoster(modelBuilder);
        ConfigureCourse(modelBuilder);
        ConfigureAssignment(modelBuilder);
        ConfigureAcceptance(modelBuilder);
        ConfigureWebhookEvent(modelBuilder);
        ConfigureAuditEvent(modelBuilder);
        ConfigureNotifications(modelBuilder);
        ConfigureMoodle(modelBuilder);
        ConfigureDownloadJob(modelBuilder);
    }

    private static void ConfigureUser(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<User> user = modelBuilder.Entity<User>();
        user.HasKey(u => u.Id);
        user.Property(u => u.Id).ValueGeneratedOnAdd();
        user.HasIndex(u => u.StudentId).IsUnique();
        user.Property(u => u.Role).HasConversion(new EnumToStringConverter<Role>());
        user.Property(u => u.State).HasConversion(new EnumToStringConverter<UserState>());
    }

    private static void ConfigureRoster(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<Roster> roster = modelBuilder.Entity<Roster>();
        roster.HasKey(r => r.Id);
        roster.Property(r => r.Id).ValueGeneratedOnAdd();
        roster.Property(r => r.Kind).HasConversion(new EnumToStringConverter<RosterKind>());
        roster.HasIndex(r => r.ClassKey).IsUnique().HasFilter("\"ClassKey\" IS NOT NULL");
        roster.HasMany(r => r.Members)
              .WithMany(u => u.Rosters);
        roster.HasOne(r => r.Owner)
              .WithMany()
              .HasForeignKey(r => r.OwnerId)
              .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureCourse(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<Course> course = modelBuilder.Entity<Course>();
        course.HasKey(c => c.Id);
        course.Property(c => c.Id).ValueGeneratedOnAdd();
        course.HasOne(c => c.Roster)
              .WithMany()
              .HasForeignKey(c => c.RosterId)
              .OnDelete(DeleteBehavior.Restrict);
        course.HasOne(c => c.Owner)
              .WithMany()
              .HasForeignKey(c => c.OwnerId)
              .OnDelete(DeleteBehavior.Restrict);
        course.HasMany(c => c.CoTeachers)
              .WithMany()
              .UsingEntity(j => j.ToTable("CourseTeacher"));
        course.HasIndex(c => new { c.RosterId, c.Title });
    }

    private static void ConfigureAssignment(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<Assignment> assignment = modelBuilder.Entity<Assignment>();
        assignment.HasKey(a => a.Id);
        assignment.Property(a => a.Id).ValueGeneratedOnAdd();
        assignment.Property(a => a.DeadlineKind).HasConversion(new EnumToStringConverter<DeadlineKind>());
        assignment.Property(a => a.StarterSourceKind).HasConversion(new EnumToStringConverter<StarterSourceKind>());
        assignment.Property(a => a.DownloadSnapshotMode).HasConversion(new EnumToStringConverter<DownloadSnapshotMode>());
        assignment.HasOne(a => a.Course)
                  .WithMany()
                  .HasForeignKey(a => a.CourseId)
                  .OnDelete(DeleteBehavior.Cascade);
        assignment.HasOne(a => a.Owner)
                  .WithMany()
                  .HasForeignKey(a => a.OwnerId)
                  .OnDelete(DeleteBehavior.Restrict);
        assignment.HasMany(a => a.CoTeachers).WithMany().UsingEntity(j => j.ToTable("AssignmentTeacher"));
        assignment.HasIndex(a => new { a.CourseId, a.Slug }).IsUnique();
    }

    private static void ConfigureAcceptance(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<Acceptance> acceptance = modelBuilder.Entity<Acceptance>();
        acceptance.HasKey(a => a.Id);
        acceptance.Property(a => a.Id).ValueGeneratedOnAdd();
        acceptance.Property(a => a.Status).HasConversion(new EnumToStringConverter<SubmissionStatus>());
        acceptance.Property(a => a.FeedbackState)
                  .HasConversion(new EnumToStringConverter<FeedbackState>())
                  .HasDefaultValue(FeedbackState.None);
        acceptance.OwnsOne(a => a.Analytics);
        acceptance.HasIndex(a => a.Late);
        acceptance.HasOne(a => a.Assignment)
                  .WithMany(a => a.Acceptances)
                  .HasForeignKey(a => a.AssignmentId)
                  .OnDelete(DeleteBehavior.Cascade);
        acceptance.HasOne(a => a.Student)
                  .WithMany()
                  .HasForeignKey(a => a.StudentId)
                  .OnDelete(DeleteBehavior.Restrict);
        acceptance.HasIndex(a => new { a.AssignmentId, a.StudentId }).IsUnique();
    }

    private static void ConfigureWebhookEvent(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<WebhookEvent> webhookEvent = modelBuilder.Entity<WebhookEvent>();
        webhookEvent.HasKey(e => e.Id);
        webhookEvent.Property(e => e.Id).ValueGeneratedOnAdd();
        webhookEvent.Property(e => e.Payload).HasColumnType("jsonb");
        webhookEvent.HasIndex(e => e.ReceivedAt);
        webhookEvent.HasIndex(e => new { e.RepoOwner, e.RepoName });
    }

    private static void ConfigureAuditEvent(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<AuditEvent> auditEvent = modelBuilder.Entity<AuditEvent>();
        auditEvent.HasKey(e => e.Id);
        auditEvent.Property(e => e.Id).ValueGeneratedOnAdd();
        auditEvent.Property(e => e.Action).HasConversion(new EnumToStringConverter<AuditAction>());
        auditEvent.Property(e => e.Metadata).HasColumnType("jsonb");
        auditEvent.HasIndex(e => e.At);
        auditEvent.HasIndex(e => e.ActorStudentId);
        auditEvent.HasIndex(e => e.Action);
        auditEvent.HasIndex(e => new { e.TargetType, e.TargetId });
    }

    private static void ConfigureNotifications(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<NotificationPreference> preference = modelBuilder.Entity<NotificationPreference>();
        preference.HasKey(p => p.Id);
        preference.Property(p => p.Id).ValueGeneratedOnAdd();
        preference.HasOne(p => p.User)
                  .WithMany()
                  .HasForeignKey(p => p.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        preference.HasIndex(p => p.UserId).IsUnique();

        EntityTypeBuilder<Notification> notification = modelBuilder.Entity<Notification>();
        notification.HasKey(n => n.Id);
        notification.Property(n => n.Id).ValueGeneratedOnAdd();
        notification.Property(n => n.Trigger).HasConversion(new EnumToStringConverter<NotificationTrigger>());
        notification.Property(n => n.Status).HasConversion(new EnumToStringConverter<NotificationStatus>());
        notification.HasOne(n => n.Recipient)
                    .WithMany()
                    .HasForeignKey(n => n.RecipientUserId)
                    .OnDelete(DeleteBehavior.Cascade);
        notification.HasIndex(n => new { n.EventKey, n.RecipientUserId }).IsUnique();
        notification.HasIndex(n => new { n.Status, n.NextAttemptAt });
    }

    private static void ConfigureMoodle(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<MoodleLink> link = modelBuilder.Entity<MoodleLink>();
        link.HasKey(l => l.Id);
        link.Property(l => l.Id).ValueGeneratedOnAdd();
        link.HasOne(l => l.Course)
            .WithMany()
            .HasForeignKey(l => l.CourseId)
            .OnDelete(DeleteBehavior.Cascade);
        link.HasIndex(l => l.CourseId).IsUnique();

        EntityTypeBuilder<MoodleSyncOp> op = modelBuilder.Entity<MoodleSyncOp>();
        op.HasKey(o => o.Id);
        op.Property(o => o.Id).ValueGeneratedOnAdd();
        op.Property(o => o.OpType).HasConversion(new EnumToStringConverter<MoodleSyncOpType>());
        op.Property(o => o.Status).HasConversion(new EnumToStringConverter<MoodleSyncStatus>());
        op.HasIndex(o => new { o.Status, o.NextAttemptAt });
        op.HasIndex(o => o.AssignmentId);

        EntityTypeBuilder<MoodleItemMapping> mapping = modelBuilder.Entity<MoodleItemMapping>();
        mapping.HasKey(m => m.Id);
        mapping.Property(m => m.Id).ValueGeneratedOnAdd();
        mapping.HasIndex(m => m.AssignmentId).IsUnique();
        mapping.HasIndex(m => m.CourseId);
    }

    private static void ConfigureDownloadJob(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<DownloadJob> job = modelBuilder.Entity<DownloadJob>();
        job.HasKey(j => j.Id);
        job.Property(j => j.Id).ValueGeneratedOnAdd();
        job.Property(j => j.Mode).HasConversion(new EnumToStringConverter<DownloadSnapshotMode>());
        job.Property(j => j.Status).HasConversion(new EnumToStringConverter<DownloadJobStatus>());
        job.HasIndex(j => j.AssignmentId);
        job.HasIndex(j => new { j.Status, j.CreatedAt });
        job.HasIndex(j => j.ExpiresAt);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.Conventions.Remove<TableNameFromDbSetConvention>();
    }
}
