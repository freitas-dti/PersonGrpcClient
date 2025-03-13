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
    }
}

