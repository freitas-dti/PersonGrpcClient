﻿using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Compression;
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
            var channelOptions = new GrpcChannelOptions
            {
                MaxReceiveMessageSize = null, // Remove limite de tamanho
                MaxSendMessageSize = null,    // Remove limite de tamanho
                HttpHandler = GetHttpHandler(),
            };

            var channel = GrpcChannel.ForAddress("http://localhost:50051", channelOptions);
            _client = new PersonService.PersonServiceClient(channel);
        }

        private HttpClientHandler GetHttpHandler()
        {
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
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
                var startTime = DateTime.Now;
                Debug.WriteLine("gRPC: Starting fetch");

                var response = await _client.GetAllPeopleAsync(new EmptyRequest());

                // Verificar se response.Items existe e tem dados
                if (response?.Items == null)
                {
                    Debug.WriteLine("gRPC: Response or Items is null");
                    return new List<PersonResponse>();
                }

                var duration = DateTime.Now - startTime;
                Debug.WriteLine($"gRPC: Fetch completed in {duration.TotalSeconds:F2}s");
                Debug.WriteLine($"gRPC: Retrieved {response.Items.Count} records");

                return response.Items.ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"gRPC Error: {ex.Message}");
                throw;
            }
        }

        public async Task<PersonResponse> UpdatePersonAsync(Person person)
        {
            try
            {
                var startTime = DateTime.Now;
                Debug.WriteLine($"gRPC: Starting update for ServerId: {person.ServerId}");

                var request = new PersonRequest
                {
                    Name = person.Name,
                    LastName = person.LastName,
                    Age = person.Age,
                    Weight = person.Weight,
                    LocalId = person.ServerId.ToString(),
                    CreatedAt = person.CreatedAt.ToString("O")
                };

                var response = await _client.UpdatePersonAsync(request);
                var duration = DateTime.Now - startTime;

                Debug.WriteLine($"gRPC: Update completed in {duration.TotalMilliseconds}ms");
                Debug.WriteLine($"gRPC: Response received for ID: {response.Id}");

                return response;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"gRPC Error: {ex.Message}");
                throw;
            }
        }
    }
}

