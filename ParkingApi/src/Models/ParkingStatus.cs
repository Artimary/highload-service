using System;
using System.Text.Json.Serialization;

namespace ParkingApi.Models;

public class ParkingStatus
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("freeSpots")]
    public int FreeSpots { get; set; }

    [JsonPropertyName("lat")]
    public double Lat { get; set; }

    [JsonPropertyName("lon")]
    public double Lon { get; set; }
}
