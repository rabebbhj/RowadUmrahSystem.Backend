using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RowadUmrahSystem.Web.Models.Accounting
{
    public class Tax
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Column(TypeName = "decimal(5,2)")]
        public decimal Rate { get; set; }

        public bool IsActive { get; set; } = true;
    }
}