namespace ParkingApi.Models;

public class BookingRequest
{
    public required string VehicleId { get; set; }
    public int SpotNumber { get; set; }
}