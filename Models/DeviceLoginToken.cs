namespace RowadUmrahSystem.Web.Models
{
    /// <summary>
    /// Long-lived token of one phone for biometric sign-in: the phone keeps the token in its secure
    /// storage (Keychain / Keystore) behind Face ID or a fingerprint; only its SHA-256 is stored here.
    /// </summary>
    public class DeviceLoginToken
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string TokenHash { get; set; } = string.Empty;
        public string? DeviceName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastUsedAt { get; set; }
        public DateTime? RevokedAt { get; set; }
    }
}
