namespace Booking.Booking.ValueObjects;

using Exceptions;
using Newtonsoft.Json;

public record PassengerInfo
{
    public string Name { get; }

    [JsonConstructor]
    private PassengerInfo(string name)
    {
        Name = name;
    }

    public static PassengerInfo Of(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidPassengerNameException(name);
        }

        return new PassengerInfo(name);
    }
}