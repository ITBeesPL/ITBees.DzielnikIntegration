namespace ITBees.DzielnikIntegration.Fas.Models;

/// <summary>Wynik utworzenia w Dzielniku faktur za płatności z bieżącego miesiąca.</summary>
public class DzielnikMonthlyInvoicesVm
{
    /// <summary>Ile faktur utworzono w Dzielniku w tym przebiegu.</summary>
    public int CreatedCount { get; set; }

    /// <summary>Ile płatności pominięto, bo faktura w Dzielniku już istniała (poprzednie przebiegi).</summary>
    public int AlreadyCreatedCount { get; set; }

    /// <summary>Ile płatności pominięto, bo dotyczyły planów darmowych (nie ma czego fakturować).</summary>
    public int SkippedFreeCount { get; set; }

    /// <summary>Ile faktur zostało w tym przebiegu przyjętych przez KSeF (gdy włączone "Wysyłaj od razu do KSeF").</summary>
    public int SentToKsefCount { get; set; }

    public List<DzielnikCreatedInvoiceVm> CreatedInvoices { get; set; } = new();

    public List<DzielnikInvoiceErrorVm> Errors { get; set; } = new();
}

public class DzielnikCreatedInvoiceVm
{
    public string InvoiceNumber { get; set; } = "";
    public string BuyerName { get; set; } = "";
    public string? BuyerEmail { get; set; }
    public decimal GrossTotal { get; set; }
    public DateTime SaleDate { get; set; }

    /// <summary>Numer KSeF, gdy dokument został w tym przebiegu przyjęty przez KSeF.</summary>
    public string? KsefNumber { get; set; }
}

public class DzielnikInvoiceErrorVm
{
    public Guid PaymentSessionGuid { get; set; }
    public string? BuyerEmail { get; set; }
    public string Error { get; set; } = "";
}
