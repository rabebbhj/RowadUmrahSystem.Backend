using System.ComponentModel.DataAnnotations;

namespace RowadUmrahSystem.Web.Models.Accounting
{
    public class FinancialPeriod
    {
        public int Id { get; set; }

        public int FiscalYearId { get; set; }
        public FiscalYear FiscalYear { get; set; } = null!;

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }

        public bool IsClosed { get; set; } = false;
    }
}