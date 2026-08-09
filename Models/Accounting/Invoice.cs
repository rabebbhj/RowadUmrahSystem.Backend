using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RowadUmrahSystem.Web.Models.Accounting
{
    public class Invoice
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string InvoiceNumber { get; set; } = string.Empty;

        public DateTime InvoiceDate { get; set; } = DateTime.Now;

        public DateTime? DueDate { get; set; }

        [Required]
        [MaxLength(150)]
        public string CustomerName { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? PassportNumber { get; set; }

        public int? TravelerId { get; set; }
        public Traveler? Traveler { get; set; }

        public int? TripId { get; set; }
        public Trip? Trip { get; set; }

        public int? CurrencyId { get; set; }
        public Currency? Currency { get; set; }

        public int? CostCenterId { get; set; }
        public CostCenter? CostCenter { get; set; }

        public int? PaymentTermId { get; set; }
        public PaymentTerm? PaymentTerm { get; set; }

        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

        [Column(TypeName = "decimal(18,3)")]
        public decimal SubTotal { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal DiscountAmount { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal TaxAmount { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal TotalAmount { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal PaidAmount { get; set; }

        [NotMapped]
        public decimal RemainingAmount => TotalAmount - PaidAmount;

        public InvoiceStatus Status { get; set; } = InvoiceStatus.Unpaid;

        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }

        [MaxLength(100)]
        public string ReferenceNumber { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Notes { get; set; } = string.Empty;

        public bool IsArchived { get; set; } = false;

        public DateTime? PaidAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public List<InvoiceItem> Items { get; set; } = new();
    }
}