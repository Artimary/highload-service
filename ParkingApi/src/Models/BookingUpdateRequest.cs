namespace ParkingApi.Models;

public class BookingUpdateRequest
{
    public required string VehicleId { get; set; }
    public bool Active { get; set; }
}