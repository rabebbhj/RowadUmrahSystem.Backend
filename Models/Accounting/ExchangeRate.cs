using System.ComponentModel.DataAnnotations.Schema;

namespace RowadUmrahSystem.Web.Models.Accounting
{
    public class ExchangeRate
    {
        public int Id { get; set; }

        public int CurrencyId { get; set; }
        public Currency Currency { get; set; } = null!;

        public DateTime RateDate { get; set; } = DateTime.Now;

        [Column(TypeName = "decimal(18,6)")]
        public decimal RateToBase { get; set; }
    }
}