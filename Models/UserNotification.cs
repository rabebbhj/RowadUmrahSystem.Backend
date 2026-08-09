namespace RowadUmrahSystem.Web.Models
{
    public class UserNotification
    {
        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty;

        public int NotificationId { get; set; }

        public Notification Notification { get; set; } = null!;

        public bool IsRead { get; set; } = false;

        public DateTime? ReadAt { get; set; }
        public bool IsDeleted { get; set; } = false;
        public DateTime? DeletedAt { get; set; }
    }
}