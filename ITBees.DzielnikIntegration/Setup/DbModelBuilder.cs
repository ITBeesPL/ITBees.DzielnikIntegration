using ITBees.DzielnikIntegration.Entities;
using Microsoft.EntityFrameworkCore;

namespace ITBees.DzielnikIntegration.Setup;

public static class DbModelBuilder
{
    /// <summary>
    /// Rejestruje encję ustawień integracji z dzielnik.com w modelu EF aplikacji hosta -
    /// wywołaj w OnModelCreating i dodaj migrację.
    /// </summary>
    public static void Register(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DzielnikIntegrationSettings>().HasKey(x => x.Id);
    }
}
