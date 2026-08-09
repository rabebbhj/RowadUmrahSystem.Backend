using System.ComponentModel.DataAnnotations;

namespace RowadUmrahSystem.Web.Models.Accounting
{
    public class Customer
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(50)]
        public string CivilId { get; set; } = string.Empty;

        [MaxLength(50)]
        public string PassportNumber { get; set; } = string.Empty;

        [MaxLength(30)]
        public string PhoneNumber { get; set; } = string.Empty;

        [MaxLength(150)]
        public string Email { get; set; } = string.Empty;

        [MaxLength(300)]
        public string Address { get; set; } = string.Empty;

        public int? TravelerId { get; set; }
        public Traveler? Traveler { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}