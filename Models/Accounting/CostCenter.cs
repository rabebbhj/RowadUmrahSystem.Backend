using System.ComponentModel.DataAnnotations;

namespace RowadUmrahSystem.Web.Models.Accounting
{
    public class CostCenter
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(30)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        public int? TripId { get; set; }
        public Trip? Trip { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}