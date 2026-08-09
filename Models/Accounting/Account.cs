using System.ComponentModel.DataAnnotations;

namespace RowadUmrahSystem.Web.Models.Accounting
{
    public class Account
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(30)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        public AccountType Type { get; set; }

        public int? ParentAccountId { get; set; }
        public Account? ParentAccount { get; set; }

        public bool IsActive { get; set; } = true;

        public bool IsSystemAccount { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}