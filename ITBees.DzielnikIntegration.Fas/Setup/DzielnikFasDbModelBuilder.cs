using Microsoft.EntityFrameworkCore;

namespace ITBees.DzielnikIntegration.Fas.Setup;

public static class DzielnikFasDbModelBuilder
{
    /// <summary>
    /// Rejestruje encję rekordów faktur Dzielnika w modelu EF aplikacji hosta - wywołaj
    /// w OnModelCreating (razem z ITBees.DzielnikIntegration.Setup.DbModelBuilder.Register)
    /// i dodaj migrację.
    /// </summary>
    public static void RegisterDbModels(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DzielnikInvoiceRecord>().HasKey(x => x.Id);
        modelBuilder.Entity<DzielnikInvoiceRecord>().HasIndex(x => x.PaymentSessionGuid).IsUnique();
        modelBuilder.Entity<DzielnikInvoiceRecord>().Property(x => x.GrossTotal).HasColumnType("decimal(18,2)");
    }
}
