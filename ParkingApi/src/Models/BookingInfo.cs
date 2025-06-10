namespace ParkingApi.Models;

public class BookingInfo
{
    public int BookingId { get; set; }
    public string VehicleId { get; set; }
    public int ParkingId { get; set; }
    public int SpotNumber { get; set; }
    public bool Active { get; set; }
}
