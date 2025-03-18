using PersonGrpcClient.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PersonGrpcClient.Services
{
    public class RestClientService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl = "http://localhost:3000/api/person";
        private readonly JsonSerializerOptions _jsonOptions;


        public RestClientService()
        {
            _httpClient = new HttpClient();
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString,
                Converters =
          {
              new JsonStringDateTimeConverter(),
              new DoubleConverter() // Adicionar converter específico para double
          }
            };
        }

        public async Task<(List<Person> People, TimeSpan Duration)> GetAllPeopleAsync()
        {
            try
            {
                var startTime = DateTime.Now;
                Debug.WriteLine("REST: Starting to fetch all people");

                var response = await _httpClient.GetAsync($"{_baseUrl}/rest/all");
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var restPeople = JsonSerializer.Deserialize<List<RestPersonResponse>>(content, _jsonOptions);

                // Converter explicitamente para List<Person>
                var people = restPeople.Select(p => new Person
                {
                    ServerId = p.Id,
                    Name = p.Name,
                    LastName = p.LastName,
                    Age = p.Age,
                    Weight = p.Weight,
                    CreatedAt = DateTime.Parse(p.CreatedAt), // Converter string para DateTime
                    LastSyncAttempt = !string.IsNullOrEmpty(p.SyncedAt) ?
                    DateTime.Parse(p.SyncedAt) : null, // Converter string para DateTime?
                    IsSynced = true // Definir como sincronizado já que veio do servidor
                }).ToList();

                var duration = DateTime.Now - startTime;
                Debug.WriteLine($"REST: Retrieved {people.Count} people in {duration.TotalMilliseconds}ms");

                return (people, duration);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"REST Error: {ex.Message}");
                throw;
            }
        }

        public async Task<(Person Person, TimeSpan Duration)> UpdatePersonAsync(Person person)
        {
            try
            {
                var startTime = DateTime.Now;
                Debug.WriteLine($"REST: Starting to update person {person.ServerId}");

                var updateRequest = new
                {
                    name = person.Name,
                    lastName = person.LastName,
                    age = person.Age,
                    weight = person.Weight
                };

                var json = JsonSerializer.Serialize(updateRequest);
                Debug.WriteLine($"REST: Sending JSON: {json}");

                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PutAsync(
                    $"{_baseUrl}/rest/update/{person.ServerId}",
                    content);

                var responseContent = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"REST: Received response: {responseContent}");

                response.EnsureSuccessStatusCode();

                var restPerson = JsonSerializer.Deserialize<RestPersonResponse>(responseContent, _jsonOptions);

                var updatedPerson = new Person
                {
                    ServerId = restPerson.Id,
                    Name = restPerson.Name,
                    LastName = restPerson.LastName,
                    Age = restPerson.Age,
                    Weight = restPerson.Weight,
                    CreatedAt = DateTime.Parse(restPerson.CreatedAt),
                    LastSyncAttempt = DateTime.Parse(restPerson.SyncedAt),
                    IsSynced = true
                };

                var duration = DateTime.Now - startTime;
                Debug.WriteLine($"REST: Updated person in {duration.TotalMilliseconds}ms");

                return (updatedPerson, duration);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"REST Error: {ex.Message}");
                throw;
            }
        }
    }

    public class DoubleConverter : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                if (double.TryParse(reader.GetString(),
                    NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out double value))
                {
                    return value;
                }
            }
            else if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.GetDouble();
            }

            throw new JsonException("Unable to convert value to double");
        }

        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value);
        }
    }

    // Converter personalizado para DateTime se necessário
    public class JsonStringDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var dateString = reader.GetString();
            return DateTime.Parse(dateString, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToUniversalTime().ToString("O")); // Formato ISO 8601
        }
    }

    // Classes para serialização/deserialização REST
    public class RestPersonResponse
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string LastName { get; set; }
            public int Age { get; set; }
            public double Weight { get; set; }
            public string LocalId { get; set; }
            public String CreatedAt { get; set; }
            public String SyncedAt { get; set; }
        }

        public class RestUpdatePersonRequest
        {
            public string Name { get; set; }
            public string LastName { get; set; }
            public int Age { get; set; }
            public double Weight { get; set; }
            public string LocalId { get; set; }
            public String CreatedAt { get; set; }
        }

    }

