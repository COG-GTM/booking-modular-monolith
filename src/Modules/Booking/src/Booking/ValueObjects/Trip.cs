namespace Booking.Booking.ValueObjects;

using Exceptions;

public record Trip
{
    public string FlightNumber { get; init; }
    public Guid AircraftId { get; init; }
    public Guid DepartureAirportId { get; init; }
    public Guid ArriveAirportId { get; init; }
    public DateTime FlightDate { get; init; }
    public decimal Price { get; init; }
    public string Description { get; init; }
    public string SeatNumber { get; init; }

    private Trip(
        string flightNumber,
        Guid aircraftId,
        Guid departureAirportId,
        Guid arriveAirportId,
        DateTime flightDate,
        decimal price,
        string description,
        string seatNumber
    )
    {
        FlightNumber = flightNumber;
        AircraftId = aircraftId;
        DepartureAirportId = departureAirportId;
        ArriveAirportId = arriveAirportId;
        FlightDate = flightDate;
        Price = price;
        Description = description;
        SeatNumber = seatNumber;
    }

    public static Trip Of(
        string flightNumber,
        Guid aircraftId,
        Guid departureAirportId,
        Guid arriveAirportId,
        DateTime flightDate,
        decimal price,
        string description,
        string seatNumber
    )
    {
        if (string.IsNullOrWhiteSpace(flightNumber))
        {
            throw new InvalidFlightNumberException(flightNumber);
        }

        if (aircraftId == Guid.Empty)
        {
            throw new InvalidAircraftIdException(aircraftId);
        }

        if (departureAirportId == Guid.Empty)
        {
            throw new InvalidDepartureAirportIdException(departureAirportId);
        }

        if (arriveAirportId == Guid.Empty)
        {
            throw new InvalidArriveAirportIdException(departureAirportId);
        }

        if (flightDate == default)
        {
            throw new InvalidFlightDateException(flightDate);
        }

        if (price < 0)
        {
            throw new InvalidPriceException(price);
        }

        if (string.IsNullOrWhiteSpace(seatNumber))
        {
            throw new SeatNumberException(seatNumber);
        }

        return new Trip(
            flightNumber,
            aircraftId,
            departureAirportId,
            arriveAirportId,
            flightDate,
            price,
            description,
            seatNumber
        );
    }
}
