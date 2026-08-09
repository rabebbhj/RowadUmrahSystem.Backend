using System.ComponentModel.DataAnnotations;

namespace RowadUmrahSystem.Web.Models.Accounting
{
    public class Currency
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(10)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(10)]
        public string Symbol { get; set; } = string.Empty;

        public bool IsBaseCurrency { get; set; } = false;

        public bool IsActive { get; set; } = true;
    }
}