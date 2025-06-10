using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using Dapper;
using Npgsql;
using ParkingApi.Models;

namespace ParkingApi.Services;

public class PostgresService
{
    private readonly string _connectionString;

    public PostgresService(string connectionString)
    {
        _connectionString = connectionString;
    }
    public async Task<(double lat, double lon)?> GetParkingLotLocationAsync(int id)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new NpgsqlCommand("SELECT lat, lon FROM parking_lots WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return (reader.GetDouble(0), reader.GetDouble(1));
        }
        return null;
    }

    public async Task<bool> IsSpotBookedAsync(int parkingId, int spotNumber)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new NpgsqlCommand(
            "SELECT EXISTS(SELECT 1 FROM bookings WHERE parking_id = @parkingId AND spot_number = @spotNumber AND active = true)",
            conn);
        cmd.Parameters.AddWithValue("parkingId", parkingId);
        cmd.Parameters.AddWithValue("spotNumber", spotNumber);
        var result = await cmd.ExecuteScalarAsync();
        return result != null && (bool)result;
    }

    public async Task<int> BookSpotAsync(string vehicleId, int parkingId, int spotNumber)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new NpgsqlCommand(
            "INSERT INTO bookings (vehicle_id, parking_id, spot_number, active) VALUES (@vehicleId, @parkingId, @spotNumber, true) RETURNING id",
            conn);
        cmd.Parameters.AddWithValue("vehicleId", vehicleId);
        cmd.Parameters.AddWithValue("parkingId", parkingId);
        cmd.Parameters.AddWithValue("spotNumber", spotNumber);
        var result = await cmd.ExecuteScalarAsync();
        return result != null ? (int)result : throw new InvalidOperationException("Failed to get booking ID");
    }

    public async Task<bool> DeleteBookingAsync(int bookingId)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM bookings WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", bookingId);
        int rowsAffected = await cmd.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }

    public async Task<bool> UpdateBookingAsync(int bookingId, string vehicleId, bool active)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("UPDATE bookings SET vehicle_id = @vehicleId, active = @active WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", bookingId);
        cmd.Parameters.AddWithValue("vehicleId", vehicleId);
        cmd.Parameters.AddWithValue("active", active);
        int rowsAffected = await cmd.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }

    // Добавьте этот метод в PostgresService для получения информации о бронировании
    public async Task<BookingInfo> GetBookingInfoAsync(int bookingId)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        string sql = @"SELECT booking_id, vehicle_id, parking_id, spot_number, active 
                    FROM bookings 
                    WHERE booking_id = @bookingId";

        var bookingInfo = await connection.QueryFirstOrDefaultAsync<BookingInfo>(
            sql,
            new { bookingId });

        return bookingInfo;
    }

    // Добавьте этот метод в PostgresService для получения детальной информации о парковочном месте
    public async Task<object> GetParkingSpotDetailsAsync(int spotId)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        string sql = @"SELECT p.parking_id, p.name, p.address, p.capacity, 
                            p.latitude, p.longitude, p.hourly_rate, 
                            b.spot_number, b.active
                    FROM parking_lots p
                    LEFT JOIN bookings b ON p.parking_id = b.parking_id AND b.spot_number = @spotId
                    WHERE p.parking_id = (SELECT parking_id FROM bookings WHERE spot_number = @spotId LIMIT 1)";

        var spotDetails = await connection.QueryFirstOrDefaultAsync(sql, new { spotId });
        return spotDetails;
    }
}