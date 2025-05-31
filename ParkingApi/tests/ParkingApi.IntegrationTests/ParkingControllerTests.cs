using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using ParkingApi.Models;
using Xunit;

namespace ParkingApi.IntegrationTests
{
    public class ParkingControllerTests : IClassFixture<CustomWebApplicationFactory>, IClassFixture<TestDataFixture>
    {
        private readonly HttpClient _client;
        private readonly TestDataFixture _fixture;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public ParkingControllerTests(CustomWebApplicationFactory factory, TestDataFixture fixture)
        {
            _client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
            _fixture = fixture;
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task GetParkingStatus_ReturnsSuccessAndCorrectContentType()
        {
            // Arrange
            var request = new HttpRequestMessage(HttpMethod.Get, "/parking/status");

            // Act
            var response = await _client.SendAsync(request);

            // Assert            response.EnsureSuccessStatusCode();
            Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task GetParkingStatus_ReturnsCorrectData()
        {
            // Act
            var response = await _client.GetAsync("/parking/status");

            // Assert
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            var parkingStatus = JsonSerializer.Deserialize<List<ParkingStatus>>(content, _jsonOptions);

            Assert.NotNull(parkingStatus);
            Assert.NotEmpty(parkingStatus);

            var firstParking = parkingStatus[0];
            Assert.Equal(1, firstParking.Id);
            Assert.Equal(5, firstParking.FreeSpots);
            Assert.Equal(59.9343, firstParking.Lat);
            Assert.Equal(30.3351, firstParking.Lon);
        }
        [Fact]
        [Trait("Category", "Integration")]
        public async Task BookSpot_ReturnsSuccess_WhenSpotIsAvailable()
        {
            // Arrange
            var booking = new BookingRequest
            {
                VehicleId = "TEST_CAR_123",
                SpotNumber = 1
            };
            var content = new StringContent(
                JsonSerializer.Serialize(booking),
                Encoding.UTF8,
                "application/json");

            try
            {
                // Act
                var response = await _client.PostAsync("/parking/1/book", content);

                // Log the response for debugging
                Console.WriteLine($"BookSpot Response Status: {response.StatusCode}");
                var responseBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"BookSpot Response Body: {responseBody}");

                // Assert
                response.EnsureSuccessStatusCode();
                var bookingResult = JsonDocument.Parse(responseBody).RootElement;

                Assert.True(bookingResult.TryGetProperty("booking_id", out var bookingId),
                    "Response should contain booking_id property");
                Assert.True(bookingId.GetInt32() > 0,
                    "Booking ID should be greater than 0");
                Assert.True(bookingResult.TryGetProperty("status", out var status),
                    "Response should contain status property");
                Assert.Equal("success", status.GetString());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in BookSpot test: {ex.Message}");
                throw;
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task BookSpot_ReturnsBadRequest_WhenSpotAlreadyBooked()
        {
            // Arrange - First booking
            var firstBooking = new BookingRequest
            {
                VehicleId = "TEST_CAR_456",
                SpotNumber = 2
            };
            var firstContent = new StringContent(
                JsonSerializer.Serialize(firstBooking),
                Encoding.UTF8,
                "application/json");
            await _client.PostAsync("/parking/1/book", firstContent);

            // Arrange - Second booking for the same spot
            var secondBooking = new BookingRequest
            {
                VehicleId = "TEST_CAR_789",
                SpotNumber = 2
            };
            var secondContent = new StringContent(
                JsonSerializer.Serialize(secondBooking),
                Encoding.UTF8,
                "application/json");

            // Act
            var response = await _client.PostAsync("/parking/1/book", secondContent);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task GetRoute_ReturnsCorrectParkingLocation()
        {
            // Act
            var response = await _client.GetAsync("/parking/1/route");

            // Assert
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            var routeInfo = JsonDocument.Parse(content).RootElement;

            Assert.True(routeInfo.TryGetProperty("id", out var id));
            Assert.Equal(1, id.GetInt32());
            Assert.True(routeInfo.TryGetProperty("lat", out var lat));
            Assert.Equal(59.9343, lat.GetDouble());
            Assert.True(routeInfo.TryGetProperty("lon", out var lon));
            Assert.Equal(30.3351, lon.GetDouble());
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task GetRoute_ReturnsNotFound_WhenParkingDoesNotExist()
        {
            // Act
            var response = await _client.GetAsync("/parking/999/route");

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task DeleteBooking_ReturnsSuccess_WhenBookingExists()
        {
            // Arrange - Create booking first
            var booking = new BookingRequest
            {
                VehicleId = "TEST_CAR_DELETE",
                SpotNumber = 3
            };
            var content = new StringContent(
                JsonSerializer.Serialize(booking),
                Encoding.UTF8,
                "application/json");
            var bookResponse = await _client.PostAsync("/parking/1/book", content);
            var bookContent = await bookResponse.Content.ReadAsStringAsync();
            var bookingId = JsonDocument.Parse(bookContent).RootElement.GetProperty("booking_id").GetInt32();

            // Act
            var response = await _client.DeleteAsync($"/parking/{bookingId}");

            // Assert
            response.EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task UpdateBooking_ReturnsSuccess_WhenBookingExists()
        {
            // Arrange - Create booking first
            var booking = new BookingRequest
            {
                VehicleId = "TEST_CAR_UPDATE",
                SpotNumber = 4
            };
            var content = new StringContent(
                JsonSerializer.Serialize(booking),
                Encoding.UTF8,
                "application/json");
            var bookResponse = await _client.PostAsync("/parking/1/book", content);
            var bookContent = await bookResponse.Content.ReadAsStringAsync();
            var bookingId = JsonDocument.Parse(bookContent).RootElement.GetProperty("booking_id").GetInt32();

            // Prepare update request
            var updateRequest = new BookingUpdateRequest
            {
                VehicleId = "TEST_CAR_UPDATED",
                Active = false
            };
            var updateContent = new StringContent(
                JsonSerializer.Serialize(updateRequest),
                Encoding.UTF8,
                "application/json");

            // Act
            var response = await _client.PutAsync($"/parking/{bookingId}", updateContent);

            // Assert
            response.EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}