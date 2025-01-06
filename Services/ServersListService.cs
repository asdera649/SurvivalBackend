using Amazon.S3;
using Amazon.S3.Model;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SurvivalBackend.Services
{
    public class ServersListService(ILogger<ServersListService> logger, IConfiguration configuration)
    {
        #region Structs

        [method: JsonConstructor]
        public struct ServerContainer(string uniqueId, string serverName, string requestId, bool ready)
        {
            public string UniqueId { get; set; } = uniqueId;
            public string ServerName { get; } = serverName;
            public string RequestId { get; set; } = requestId;
            public bool Ready { get; set; } = ready;
        }

        #endregion

        private readonly string _filePath = Path.Combine(AppContext.BaseDirectory, "ServersListSaves");
        private readonly List<ServerContainer> _items = [];

        private readonly JsonSerializerOptions _jsonSerializerOptions = new() { WriteIndented = true };

        private bool _isLoaded;

        public IReadOnlyList<ServerContainer> Items
        {
            get
            {
                if (!_isLoaded)
                {
                    throw new Exception("The server list has not been uploaded yet, you need to upload the server list first!");
                }

                return _items;
            }
        }

        private readonly ILogger<ServersListService> _logger = logger;
        private readonly IConfiguration _configuration = configuration;

        public async Task Load()
        {
            start:

            _logger.LogInformation("[ServersListService]: Servers list saves loading...");

            _items.Clear();

            if (!Directory.Exists(_filePath))
            {
                Directory.CreateDirectory(_filePath);
            }
            else
            {
                var files = Directory.GetFiles(_filePath);

                foreach (var f in files)
                {
                    File.Delete(f);
                }
            }

            var config = new AmazonS3Config
            {
                ServiceURL = $"https://{_configuration["S3EndPoint"]}",
                ForcePathStyle = true
            };

            using (var client = new AmazonS3Client(_configuration["S3AccessKey"], _configuration["S3SecretKey"], config))
            {
                try
                {
                    var listRequest = new ListObjectsV2Request
                    {
                        BucketName = _configuration["S3BucketName"],
                        Prefix = _configuration["S3ServersListSavesPath"]
                    };

                    ListObjectsV2Response listResponse;

                    do
                    {
                        listResponse = await client.ListObjectsV2Async(listRequest);

                        foreach (var s3Object in listResponse.S3Objects)
                        {
                            if (s3Object.Key.EndsWith('/'))
                            {
                                continue;
                            }

                            string localFilePath = Path.Combine(_filePath, Path.GetFileName(s3Object.Key));

                            var getRequest = new GetObjectRequest
                            {
                                BucketName = _configuration["S3BucketName"],
                                Key = s3Object.Key
                            };

                            using (var getObjectResponse = await client.GetObjectAsync(getRequest))
                            using (var responseStream = getObjectResponse.ResponseStream)
                            using (var fileStream = new FileStream(localFilePath, FileMode.Create, FileAccess.Write))
                            {
                                await responseStream.CopyToAsync(fileStream);
                            }

                            var json = File.ReadAllText(localFilePath);

                            var item = JsonSerializer.Deserialize<ServerContainer>(json);

                            _items.Add(item);

                            _logger.LogInformation($"[ServersListService]: Server list save loaded: {s3Object.Key} to {localFilePath}");
                        }

                        listRequest.ContinuationToken = listResponse.NextContinuationToken;

                    } 
                    while (listResponse.IsTruncated);
                }
                catch (AmazonS3Exception e)
                {
                    _logger.LogError($"[ServersListService]: Servers list saves loading failed, S3 error: {e.Message}");

                    await Task.Delay(5000);

                    goto start;
                }
                catch (Exception e)
                {
                    _logger.LogError($"[ServersListService]: Servers list saves loading failed, error: {e.Message}");

                    await Task.Delay(5000);

                    goto start;
                }
            }

            _isLoaded = true;

            _logger.LogInformation("[ServersListService]: Servers list saves successfully loaded.");
        }

        public async Task Add(ServerContainer serverContainer)
        {
            if (!_isLoaded)
            {
                throw new Exception("The server list has not been uploaded yet, you need to upload the server list first!");
            }

            _items.Add(serverContainer);

            var index = _items.Count - 1;

            var path = SaveLocally(index);

            await UnloadSave(path);
        }

        public async Task Edit(int index, ServerContainer serverContainer)
        {
            if (!_isLoaded)
            {
                throw new Exception("The server list has not been uploaded yet, you need to upload the server list first!");
            }

            _items[index] = serverContainer;

            var parh = SaveLocally(index);

            await UnloadSave(parh);
        }

        public async Task Clear()
        {
            if (!_isLoaded)
            {
                throw new Exception("The server list has not been uploaded yet, you need to upload the server list first!");
            }

            _items.Clear();

            var config = new AmazonS3Config
            {
                ServiceURL = $"https://{_configuration["S3EndPoint"]}",
                ForcePathStyle = true
            };

            using (var client = new AmazonS3Client(_configuration["S3AccessKey"], _configuration["S3SecretKey"], config))
            {
                start:

                _logger.LogInformation("[ServersListService]: Servers list saves wiping...");

                try
                {
                    var listRequest = new ListObjectsV2Request
                    {
                        BucketName = _configuration["S3BucketName"],
                        Prefix = _configuration["S3ServersListSavesPath"]
                    };

                    ListObjectsV2Response listResponse;

                    do
                    {
                        listResponse = await client.ListObjectsV2Async(listRequest);

                        if (listResponse.S3Objects.Count == 0)
                        {
                            _logger.LogInformation("[ServersListService]: There are no files to delete in the " +
                                $"{_configuration["S3ServersListSavesPath"]} folder.");

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

                        _logger.LogInformation($"[ServersListService]: Removed {deleteResponse.DeletedObjects.Count} objects from " +
                            $"{_configuration["S3ServersListSavesPath"]}.");

                        listRequest.ContinuationToken = listResponse.NextContinuationToken;

                    }
                    while (listResponse.IsTruncated);
                }
                catch (AmazonS3Exception e)
                {
                    _logger.LogError($"[ServersListService]: Servers list saves wiping failed, S3 error: {e.Message}");

                    await Task.Delay(5000);

                    goto start;
                }
                catch (Exception e)
                {
                    _logger.LogError($"[ServersListService]: Servers list saves wiping failed, error: {e.Message}");

                    await Task.Delay(5000);

                    goto start;
                }
            }

            _logger.LogInformation("[ServersListService]: Servers list saves successfully wiped.");
        }

        private async Task UnloadSave(string path)
        {
            var config = new AmazonS3Config
            {
                ServiceURL = $"https://{_configuration["S3EndPoint"]}",
                ForcePathStyle = true
            };

            using (var client = new AmazonS3Client(_configuration["S3AccessKey"], _configuration["S3SecretKey"], config))
            {
                start:

                _logger.LogInformation($"[ServersListService]: Start unloading a server list save, path: {path}");

                try
                {
                    var putRequest = new PutObjectRequest
                    {
                        BucketName = _configuration["S3BucketName"],
                        Key = _configuration["S3ServersListSavesPath"] + Path.GetFileName(path),
                        FilePath = path,
                        ContentType = "application/octet-stream"
                    };

                    await client.PutObjectAsync(putRequest);
                }
                catch (AmazonS3Exception e)
                {
                    _logger.LogError($"[ServersListService]: Unloading a server list save failed, path: {path}, S3 error: {e.Message}");

                    await Task.Delay(5000);

                    goto start;
                }
                catch (Exception e)
                {
                    _logger.LogError($"[ServersListService]: Unloading a server list save failed, path: {path}, error: {e.Message}");

                    await Task.Delay(5000);

                    goto start;
                }
            }

            _logger.LogInformation($"[ServersListService]: Server list save successfully unloaded, path: {path}");
        }

        private string SaveLocally(int index)
        {
            var path = Path.Combine(_filePath, (index + 1) + "server.json");

            var json = JsonSerializer.Serialize(_items[index], _jsonSerializerOptions);

            File.WriteAllText(path, json);

            return path;
        }
    }
}
