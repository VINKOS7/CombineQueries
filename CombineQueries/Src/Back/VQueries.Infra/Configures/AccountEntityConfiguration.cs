using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using CombineQueries.Domain.Aggregates.Account;

namespace CombineQueries.Infra.Configures;

public class AccountEntityConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Token).IsRequired().HasMaxLength(Account.TokenMax);
        builder.Property(a => a.Name).HasMaxLength(64);
        builder.Property(a => a.Description).HasMaxLength(256);
        builder.Property(a => a.Active).IsRequired();

        builder.HasIndex(a => a.Token).IsUnique();

        builder.HasData(new Account
        {
            Id = new Guid("61742182-1a33-460e-bf32-38344c41958c"),
            Token = "p1hfc9m8vzjgrstd",
            Name = "world",
            Description = "CombineQueries alpha",
            Active = true
        });
    }
}
