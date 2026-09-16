using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payments.FPS.OutboundPayments.Enums;
using Payments.FPS.OutboundPayments.Models;
using Payments.FPS.OutboundPayments.ValueObjects;

namespace Payments.FPS.Data.Configurations;

public class OutboundPaymentConfiguration : IEntityTypeConfiguration<OutboundPayment>
{
    public void Configure(EntityTypeBuilder<OutboundPayment> builder)
    {
        builder.ToTable(nameof(OutboundPayment));
        builder.HasKey(x => x.Id);
        builder
            .Property(x => x.Id)
            .ValueGeneratedNever()
            .HasConversion<Guid>(x => x.Value, x => OutboundPaymentId.Of(x));
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.OwnsOne(
            x => x.Amount,
            a =>
            {
                a.Property(x => x.Value).HasColumnName(nameof(OutboundPayment.Amount)).HasPrecision(18, 2).IsRequired();
                a.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            }
        );
        builder.OwnsOne(
            x => x.DebtorAccount,
            a =>
            {
                a.Property(x => x.SortCode)
                    .HasColumnName(nameof(OutboundPayment.DebtorAccount) + "SortCode")
                    .HasMaxLength(6)
                    .IsRequired();
                a.Property(x => x.AccountNumber)
                    .HasColumnName(nameof(OutboundPayment.DebtorAccount) + "AccountNumber")
                    .HasMaxLength(8)
                    .IsRequired();
            }
        );
        builder.OwnsOne(
            x => x.CreditorAccount,
            a =>
            {
                a.Property(x => x.SortCode)
                    .HasColumnName(nameof(OutboundPayment.CreditorAccount) + "SortCode")
                    .HasMaxLength(6)
                    .IsRequired();
                a.Property(x => x.AccountNumber)
                    .HasColumnName(nameof(OutboundPayment.CreditorAccount) + "AccountNumber")
                    .HasMaxLength(8)
                    .IsRequired();
            }
        );
        builder.Property(x => x.Reference).HasMaxLength(35).IsRequired();
        builder
            .Property(x => x.Status)
            .HasDefaultValue(PaymentStatus.Unknown)
            .HasConversion(x => x.ToString(), x => Enum.Parse<PaymentStatus>(x));
        builder.Property(x => x.RejectionReason).HasMaxLength(200);
    }
}
