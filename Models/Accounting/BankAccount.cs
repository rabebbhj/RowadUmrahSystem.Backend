using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RowadUmrahSystem.Web.Models.Accounting
{
    public class BankAccount
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(150)]
        public string BankName { get; set; } = string.Empty;

        [MaxLength(100)]
        public string AccountNumber { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Iban { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,3)")]
        public decimal OpeningBalance { get; set; }

        public bool IsCashBox { get; set; } = false;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}