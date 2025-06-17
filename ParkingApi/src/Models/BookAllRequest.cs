using System.Text.Json.Serialization;

namespace ParkingApi.Models;

public class BookAllRequest
{
    [JsonPropertyName("vehicleid")]
    public string VehicleId { get; set; } = string.Empty;

    [JsonPropertyName("confirmationcode")]
    public string ConfirmationCode { get; set; } = string.Empty;
}