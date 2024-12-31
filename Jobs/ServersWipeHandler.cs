using Amazon.S3;
using Amazon.S3.Model;
using Quartz;
using System.Text.Json;
using System.Text;
using SurvivalBackend.Services;

namespace SurvivalBackend.Jobs
{
    public class ServersWipeHandler(
        ServersListService serversListService,
        HttpClient httpClient,
        ILogger<ServersWipeHandler> logger,
        IConfiguration configuration) : IJob
    {
        private readonly ServersListService _serversListService = serversListService;
        private readonly HttpClient _httpClient = httpClient;
        private readonly ILogger<ServersWipeHandler> _logger = logger;
        private readonly IConfiguration _configuration = configuration;

        public async Task Execute(IJobExecutionContext context)
        {
            _logger.LogInformation("[ServersWipeHandler]: The wipe process starts.");

            await ClearEdgegap();

            await ClearS3();

            _serversListService.Items.Clear();

            await StartEdgegap();

            _logger.LogInformation("[ServersWipeHandler]: The wipe process was successful.");
        }

        private async Task ClearEdgegap()
        {
            start:

            _logger.LogInformation("[ServersWipeHandler]: Edgegap wiping...");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("authorization", _configuration["EdgegapToken"]);

            HttpResponseMessage response;

            try
            {
                response = await _httpClient.GetAsync("https://api.edgegap.com/v1/fleets");
            }
            catch (HttpRequestException)
            {
                response = new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.ServiceUnavailable,
                    Content = new StringContent("Service unavailable due to network issues.")
                };
            }
            catch (TaskCanceledException)
            {
                response = new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.RequestTimeout,
                    Content = new StringContent("Request timed out.")
                };
            }
            catch (Exception)
            {
                response = new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.InternalServerError,
                    Content = new StringContent("An unexpected error occurred.")
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[ServersWipeHandler]: Edgegap wiping failed, could not get a list of fleets, " +
                    $"status code: {(int)response.StatusCode}, content: {await response.Content.ReadAsStringAsync()}");

                _logger.LogInformation("Try again in 5 seconds...");

                await Task.Delay(5000);

                goto start;
            }

            var content = await response.Content.ReadAsStringAsync();

            using var document = JsonDocument.Parse(content);

            var dataArray = document.RootElement.GetProperty("fleets");

            startLoop:

            foreach (var s in dataArray.EnumerateArray())
            {
                var name = s.GetProperty("name").GetString();
                var enabled = s.GetProperty("enabled").GetBoolean();

                if (!enabled)
                {
                    continue;
                }

                var patchContent = new StringContent("{ \"enabled\": false }", Encoding.UTF8, "application/json");

                try
                {
                    response = await _httpClient.PatchAsync($"https://api.edgegap.com/v1/fleet/{name}", patchContent);
                }
                catch (HttpRequestException)
                {
                    response = new HttpResponseMessage
                    {
                        StatusCode = System.Net.HttpStatusCode.ServiceUnavailable,
                        Content = new StringContent("Service unavailable due to network issues.")
                    };
                }
                catch (TaskCanceledException)
                {
                    response = new HttpResponseMessage
                    {
                        StatusCode = System.Net.HttpStatusCode.RequestTimeout,
                        Content = new StringContent("Request timed out.")
                    };
                }
                catch (Exception)
                {
                    response = new HttpResponseMessage
                    {
                        StatusCode = System.Net.HttpStatusCode.InternalServerError,
                        Content = new StringContent("An unexpected error occurred.")
                    };
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("[ServersWipeHandler]: Edgegap wiping failed, failed to disable one of the fleets, " +
                        $"status code: {(int)response.StatusCode}, content: {await response.Content.ReadAsStringAsync()}");

                    _logger.LogInformation("Try again in 5 seconds...");

                    await Task.Delay(5000);

                    goto startLoop;
                }

                _logger.LogInformation($"[ServersWipeHandler]: Fleet \"{name}\" has been successfully disabled!");
            }

        startBulkDelete:

            try
            {
                response = await _httpClient.PostAsync(
                    "https://api.edgegap.com/v1/deployments/bulk-stop",
                    new StringContent("{ \"filters\": [] }", Encoding.UTF8, "application/json"));
            }
            catch (HttpRequestException)
            {
                response = new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.ServiceUnavailable,
                    Content = new StringContent("Service unavailable due to network issues.")
                };
            }
            catch (TaskCanceledException)
            {
                response = new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.RequestTimeout,
                    Content = new StringContent("Request timed out.")
                };
            }
            catch (Exception)
            {
                response = new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.InternalServerError,
                    Content = new StringContent("An unexpected error occurred.")
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[ServersWipeHandler]: Edgegap wiping failed, failed to bulk delete deployments, " +
                    $"status code: {(int)response.StatusCode}, content: {await response.Content.ReadAsStringAsync()}");

                _logger.LogInformation("Try again in 5 seconds...");

                await Task.Delay(5000);

                goto startBulkDelete;
            }

            _logger.LogInformation("[ServersWipeHandler]: Edgegap successfuly wiped.");
        }

        private async Task ClearS3()
        {
            var config = new AmazonS3Config
            {
                ServiceURL = $"https://{_configuration["S3EndPoint"]}",
                ForcePathStyle = true
            };

            using (var client = new AmazonS3Client(_configuration["S3AccessKey"], _configuration["S3SecretKey"], config))
            {
                start:

                _logger.LogInformation("[ServersWipeHandler]: S3 wiping...");

                try
                {
                    var listRequest = new ListObjectsV2Request
                    {
                        BucketName = _configuration["S3BucketName"],
                        Prefix = _configuration["S3CurrentWipeSavesPath"]
                    };

                    ListObjectsV2Response listResponse;

                    do
                    {
                        listResponse = await client.ListObjectsV2Async(listRequest);

                        if (listResponse.S3Objects.Count == 0)
                        {
                            _logger.LogInformation("[ServersWipeHandler]: There are no files to delete in the " +
                                $"{_configuration["S3CurrentWipeSavesPath"]} folder.");

                            return;
                        }

                        var deleteRequest = new DeleteObjectsRequest
                        {
                            BucketName = _configuration["S3BucketName"],
                            Objects = []
                        };

                        foreach (var s3Object in listResponse.S3Objects)
                        {
                            deleteRequest.Objects.Add(new KeyVersion
                            {
                                Key = s3Object.Key
                            });
                        }

                        DeleteObjectsResponse deleteResponse = await client.DeleteObjectsAsync(deleteRequest);

                        _logger.LogInformation($"[ServersWipeHandler]: Removed {deleteResponse.DeletedObjects.Count} objects from " +
                            $"{_configuration["S3CurrentWipeSavesPath"]}.");

                        // Устанавливаем маркер продолжения, если файлов много
                        listRequest.ContinuationToken = listResponse.NextContinuationToken;

                    }
                    while (listResponse.IsTruncated);  // Продолжаем, если есть еще файлы
                }
                catch (AmazonS3Exception e)
                {
                    _logger.LogError($"[ServersWipeHandler]: S3 wiping failed, S3 error: {e.Message}");

                    await Task.Delay(5000);

                    goto start;
                }
                catch (Exception e)
                {
                    _logger.LogError($"[ServersWipeHandler]: S3 wiping failed, error: {e.Message}");

                    await Task.Delay(5000);

                    goto start;
                }
            }
        }

        private async Task StartEdgegap()
        {
            start:

            _logger.LogInformation("[ServersWipeHandler]: Edgegap activation...");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("authorization", _configuration["EdgegapToken"]);

            HttpResponseMessage response;

            try
            {
                response = await _httpClient.GetAsync("https://api.edgegap.com/v1/fleets");
            }
            catch (HttpRequestException)
            {
                response = new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.ServiceUnavailable,
                    Content = new StringContent("Service unavailable due to network issues.")
                };
            }
            catch (TaskCanceledException)
            {
                response = new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.RequestTimeout,
                    Content = new StringContent("Request timed out.")
                };
            }
            catch (Exception)
            {
                response = new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.InternalServerError,
                    Content = new StringContent("An unexpected error occurred.")
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[ServersWipeHandler]: Edgegap activation failed, could not get a list of fleets, " +
                    $"status code: {(int)response.StatusCode}, content: {await response.Content.ReadAsStringAsync()}");

                _logger.LogInformation("Try again in 5 seconds...");

                await Task.Delay(5000);

                goto start;
            }

            var content = await response.Content.ReadAsStringAsync();

            using var document = JsonDocument.Parse(content);

            var dataArray = document.RootElement.GetProperty("fleets");

            startLoop:

            foreach (var s in dataArray.EnumerateArray())
            {
                var name = s.GetProperty("name").GetString();
                var enabled = s.GetProperty("enabled").GetBoolean();

                if (enabled)
                {
                    continue;
                }

                var patchContent = new StringContent("{ \"enabled\": true }", Encoding.UTF8, "application/json");

                try
                {
                    response = await _httpClient.PatchAsync($"https://api.edgegap.com/v1/fleet/{name}", patchContent);
                }
                catch (HttpRequestException)
                {
                    response = new HttpResponseMessage
                    {
                        StatusCode = System.Net.HttpStatusCode.ServiceUnavailable,
                        Content = new StringContent("Service unavailable due to network issues.")
                    };
                }
                catch (TaskCanceledException)
                {
                    response = new HttpResponseMessage
                    {
                        StatusCode = System.Net.HttpStatusCode.RequestTimeout,
                        Content = new StringContent("Request timed out.")
                    };
                }
                catch (Exception)
                {
                    response = new HttpResponseMessage
                    {
                        StatusCode = System.Net.HttpStatusCode.InternalServerError,
                        Content = new StringContent("An unexpected error occurred.")
                    };
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("[ServersWipeHandler]: Edgegap activation failed, failed to enable one of the fleets, " +
                        $"status code: {(int)response.StatusCode}, content: {await response.Content.ReadAsStringAsync()}");

                    _logger.LogInformation("Try again in 5 seconds...");

                    await Task.Delay(5000);

                    goto startLoop;
                }

                _logger.LogInformation($"[ServersWipeHandler]: Fleet \"{name}\" has been successfully enabled!");
            }

            _logger.LogInformation("[ServersWipeHandler]: Edgegap successfuly activated.");
        }
    }
}
