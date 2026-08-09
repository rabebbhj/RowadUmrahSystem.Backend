using System.ComponentModel.DataAnnotations.Schema;

namespace RowadUmrahSystem.Web.Models.Accounting
{
    public class AccountBalance
    {
        public int Id { get; set; }

        public int AccountId { get; set; }
        public Account Account { get; set; } = null!;

        public int? FiscalYearId { get; set; }
        public FiscalYear? FiscalYear { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal OpeningDebit { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal OpeningCredit { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal DebitMovement { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal CreditMovement { get; set; }

        [NotMapped]
        public decimal ClosingBalance =>
            OpeningDebit - OpeningCredit + DebitMovement - CreditMovement;
    }
}