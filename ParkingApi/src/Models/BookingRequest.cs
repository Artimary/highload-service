using System.Text.Json.Serialization;

namespace ParkingApi.Models;

public class BookingRequest
{
    [JsonPropertyName("vehicleid")]
    public required string VehicleId { get; set; }
    [JsonPropertyName("spotnumber")]
    public int SpotNumber { get; set; }
}