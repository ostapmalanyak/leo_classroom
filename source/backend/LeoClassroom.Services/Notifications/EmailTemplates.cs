using LeoClassroom.Persistence.Model;
using LeoClassroom.Shared;
using NodaTime.Text;

namespace LeoClassroom.Services.Notifications;

public static class EmailTemplates
{
    private static readonly LocalDateTimePattern DeadlinePattern =
        LocalDateTimePattern.CreateWithInvariantCulture("dd.MM.yyyy HH:mm");

    public static EmailMessage Render(Notification notification, string appBaseUrl)
    {
        bool german = notification.Language.StartsWith("de", StringComparison.OrdinalIgnoreCase);
        string link = $"{appBaseUrl.TrimEnd('/')}/my-assignments/{notification.AssignmentId}";
        string deadline = FormatDeadline(notification.Deadline, german);

        (string subject, string body) = (notification.Trigger, german) switch
        {
            (NotificationTrigger.NewAssignment, false) => (
                $"New assignment: {notification.AssignmentTitle}",
                $"A new assignment \"{notification.AssignmentTitle}\" is available.\n"
                + $"Deadline: {deadline}\n\nOpen it here: {link}\n"),
            (NotificationTrigger.NewAssignment, true) => (
                $"Neue Aufgabe: {notification.AssignmentTitle}",
                $"Eine neue Aufgabe \"{notification.AssignmentTitle}\" ist verfügbar.\n"
                + $"Frist: {deadline}\n\nHier öffnen: {link}\n"),
            (NotificationTrigger.DeadlineChanged, false) => (
                $"Deadline changed: {notification.AssignmentTitle}",
                $"The deadline for \"{notification.AssignmentTitle}\" has changed.\n"
                + $"New deadline: {deadline}\n\nOpen it here: {link}\n"),
            _ => (
                $"Frist geändert: {notification.AssignmentTitle}",
                $"Die Frist für \"{notification.AssignmentTitle}\" wurde geändert.\n"
                + $"Neue Frist: {deadline}\n\nHier öffnen: {link}\n")
        };

        return new EmailMessage(notification.RecipientEmail, subject, body);
    }

    private static string FormatDeadline(Instant? deadline, bool german)
    {
        if (deadline is null)
        {
            return german ? "keine Frist" : "no deadline";
        }

        LocalDateTime local = deadline.Value.InZone(Const.TimeZone).LocalDateTime;

        return DeadlinePattern.Format(local);
    }
}
