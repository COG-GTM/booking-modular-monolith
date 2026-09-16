using Payments.FPS.OutboundPayments.Exceptions;

namespace Payments.FPS.OutboundPayments.ValueObjects;

public record UkAccount
{
    public string SortCode { get; }
    public string AccountNumber { get; }

    private UkAccount(string sortCode, string accountNumber)
    {
        SortCode = sortCode;
        AccountNumber = accountNumber;
    }

    public static UkAccount Of(string sortCode, string accountNumber) =>
        sortCode is { Length: 6 }
        && sortCode.All(char.IsAsciiDigit)
        && accountNumber is { Length: 8 }
        && accountNumber.All(char.IsAsciiDigit)
            ? new(sortCode, accountNumber)
            : throw new InvalidUkAccountException();
}
