using Payments.FPS.OutboundPayments.Exceptions;

namespace Payments.FPS.OutboundPayments.ValueObjects;

public class Amount
{
    public decimal Value { get; }
    public string Currency { get; private set; } = "GBP";

    private Amount(decimal value) => Value = value;

    public static Amount Of(decimal value) =>
        value <= 0 || decimal.Round(value, 2) != value ? throw new InvalidAmountException() : new(value);

    public static implicit operator decimal(Amount amount) => amount.Value;
}
