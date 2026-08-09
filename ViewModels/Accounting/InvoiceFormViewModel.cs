using RowadUmrahSystem.Web.Models.Accounting;
using System.ComponentModel.DataAnnotations;

namespace RowadUmrahSystem.Web.ViewModels.Accounting
{
    public class InvoiceFormViewModel
    {
        public int? TravelerId { get; set; }
        public int? TripId { get; set; }
        public int? CurrencyId { get; set; }
        public int? CostCenterId { get; set; }
        public int? PaymentTermId { get; set; }

        [Required]
        [MaxLength(150)]
        public string CustomerName { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? PassportNumber { get; set; }

        public DateTime InvoiceDate { get; set; } = DateTime.Now;
        public DateTime? DueDate { get; set; }

        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

        [MaxLength(100)]
        public string ReferenceNumber { get; set; } = string.Empty;

        [Required]
        public int ReceivableAccountId { get; set; }

        [Required]
        public int RevenueAccountId { get; set; }

        [MaxLength(500)]
        public string Notes { get; set; } = string.Empty;

        public List<InvoiceItemFormViewModel> Items { get; set; } = new()
        {
            new InvoiceItemFormViewModel()
        };
    }

    public class InvoiceItemFormViewModel
    {
        [Required]
        [MaxLength(200)]
        public string Description { get; set; } = string.Empty;

        public decimal Quantity { get; set; } = 1;
        public decimal UnitPrice { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxRate { get; set; }
    }
}