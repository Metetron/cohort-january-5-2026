using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BudgetTracker.Api.Auth;

namespace BudgetTracker.Api.Features.Transactions;

public class Transaction
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [Column(TypeName = "timestamptz")]
    public DateTime Date { get; set; }

    [Required]
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? Balance { get; set; }

    [MaxLength(100)]
    public string? Category { get; set; }

    [MaxLength(200)]
    public string? Labels { get; set; }

    [Required]
    [Column(TypeName = "timestamptz")]
    public DateTime ImportedAt { get; set; }

    [Required]
    [MaxLength(100)]
    public string Account { get; set; } = string.Empty;

    [Required]
    public string UserId { get; set; } = string.Empty;
}
