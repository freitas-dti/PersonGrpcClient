using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using PersonGrpcClient.Models;
using System.Diagnostics;

namespace PersonGrpcClient.Services
{
    public class GrpcClientService
    {
        private readonly PersonService.PersonServiceClient _client;
        private readonly ILogger<GrpcClientService> _logger;

        public GrpcClientService()
        {
            var channel = GrpcChannel.ForAddress("http://localhost:50051", new GrpcChannelOptions
            {
                HttpHandler = GetHttpHandler()
            });
            _client = new PersonService.PersonServiceClient(channel);
        }

        private HttpClientHandler GetHttpHandler()
        {
            var handler = new HttpClientHandler();
#if DEBUG
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
#endif
            return handler;
        }

        public async Task PingServerAsync()
        {
            try
            {
                // Você precisará adicionar este método no seu proto
                await _client.PingAsync(new Google.Protobuf.WellKnownTypes.Empty());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ping failed: {ex.Message}");
                throw;
            }
        }

        public async Task<PersonResponse> SavePersonAsync(Person person)
        {
            try
            {
                Debug.WriteLine($"Preparing to send person with LocalId: {person.Id}");

                var request = new PersonRequest
                {
                    Name = person.Name,
                    LastName = person.LastName,
                    Age = person.Age,
                    Weight = person.Weight,
                    LocalId = person.Id.ToString(),  // Certifique-se que está convertendo para string
                    CreatedAt = person.CreatedAt.ToString("O")  // Envia a data de criação

                };

                Debug.WriteLine($"Sending person with LocalId: {request.LocalId} to server");
                var response = await _client.SavePersonAsync(request);
                Debug.WriteLine($"Received response for LocalId: {request.LocalId}");

                return response;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in SavePersonAsync: {ex.Message}");
                throw;
            }
        }

        public async Task<SyncResponse> SyncPeopleAsync(IEnumerable<Person> people)
        {
            try
            {
                using var call = _client.SyncPeople();

                foreach (var person in people)
                {
                    try
                    {
                        var request = new PersonRequest
                        {
                            Name = person.Name,
                            LastName = person.LastName,
                            Age = person.Age,
                            Weight = person.Weight,
                            LocalId = person.Id.ToString()
                        };

                        await call.RequestStream.WriteAsync(request);
                        Debug.WriteLine($"Sent person with LocalId: {person.Id} to server");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error sending person {person.Id}: {ex.Message}");
                        throw;
                    }
                }

                await call.RequestStream.CompleteAsync();
                Debug.WriteLine("Completed sending all people");

                var response = await call;
                Debug.WriteLine($"Received response: Success={response.Success}, IDs={string.Join(",", response.SyncedIds)}");

                return response;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in SyncPeopleAsync: {ex.Message}");
                throw;
            }
        }

        public async Task<List<PersonResponse>> GetAllPeopleFromServerAsync()
        {
            try
            {
                Debug.WriteLine("Starting GetAllPeopleFromServerAsync");
                var people = new List<PersonResponse>();

                using var call = _client.GetAllPeople(new EmptyRequest());

                var count = 0;
                while (await call.ResponseStream.MoveNext(CancellationToken.None))
                {
                    var person = call.ResponseStream.Current;
                    Debug.WriteLine($"Received from server - ID: {person.Id}, Name: {person.Name}, Created: {person.CreatedAt}");

                    // Validar os dados recebidos
                    if (string.IsNullOrEmpty(person.CreatedAt))
                    {
                        Debug.WriteLine($"Warning: CreatedAt is empty for person {person.Id}");
                    }

                    people.Add(person);
                    count++;
                }

                Debug.WriteLine($"Total records retrieved: {people.Count}");

                // Log detalhado dos registros recebidos
                foreach (var person in people)
                {
                    Debug.WriteLine($"Person in list - ID: {person.Id}, Name: {person.Name}, Created: {person.CreatedAt}");
                }

                return people;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting people from server: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        public async Task<(PersonResponse Response, TimeSpan Duration)> UpdatePersonAsync(Person person)
        {
            try
            {
                Debug.WriteLine($"Updating person with ServerId: {person.ServerId}");
                var startTime = DateTime.Now;

                var request = new PersonRequest
                {
                    Name = person.Name,
                    LastName = person.LastName,
                    Age = person.Age,
                    Weight = person.Weight,
                    LocalId = person.ServerId.ToString(), // Aqui usamos o ServerId
                    CreatedAt = person.CreatedAt.ToString("O")
                };

                var response = await _client.UpdatePersonAsync(request);
                var duration = DateTime.Now - startTime;

                Debug.WriteLine($"Update response received for ServerId: {response.Id}. Duration: {duration.TotalMilliseconds}ms");

                return (response, duration);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in UpdatePersonAsync: {ex.Message}");
                throw;
            }
        }
    }
}

