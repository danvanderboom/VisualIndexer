using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Polly;

namespace VisualIndexer.WebApi.Controllers;

[ApiController]
[Route("[controller]")]
public class DocumentsController : ControllerBase
{
    const string storageConnectionStringName = "VisualIndexer_AzureStorage_ConnectionString";
    const string documentsQueueName = "documents-queue";

    private readonly TelemetryClient _telemetryClient;
    private readonly ILogger<DocumentsController> _logger;
    private readonly IConfiguration _configuration;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly QueueClient _queueClient;

    public DocumentsController(IConfiguration configuration, TelemetryClient telemetryClient, ILogger<DocumentsController> logger)
    {
        _configuration = configuration;
        _telemetryClient = telemetryClient;
        _logger = logger;

        _blobServiceClient = new BlobServiceClient(_configuration[storageConnectionStringName]);
        _queueClient = new QueueClient(_configuration[storageConnectionStringName], documentsQueueName);
        _queueClient.CreateIfNotExists();
    }

    [HttpPost("upload")]
    [RequestSizeLimit(100_000_000)] // Increase the request size limit to 100 MB
    [RequestFormLimits(MultipartBodyLengthLimit = 100_000_000)] // Specifically for multipart form data
    public async Task<IActionResult> UploadDocument(IFormFile document)
    {
        if (document == null || document.Length == 0)
        {
            _telemetryClient.TrackEvent("UploadAttemptedButNoDocumentFound");
            return BadRequest("No document uploaded.");
        }

        if (document.ContentType != "application/pdf")
        {
            _telemetryClient.TrackEvent("UploadAttemptedWithInvalidDocumentType",
                new Dictionary<string, string> { { "ContentType", document.ContentType } });
            return BadRequest("Only PDF documents are allowed.");
        }

        var blobRetryPolicy = Policy
            .Handle<Exception>(ex => IsTransient(ex))
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), 
                onRetry: (Exception exception, TimeSpan timespan, int retryCount, Context context) =>
                {
                    _telemetryClient.TrackException(exception,
                       new Dictionary<string, string>
                       {
                            { "Type", "Retry" },
                            { "RetryCount", retryCount.ToString() },
                            { "Policy", "UploadDocumentToBlobStorage" }
                       });

                    Console.WriteLine($"Retrying due to: {exception.Message}. Waiting {timespan} before next retry.");
                });

        var queueRetryPolicy = Policy
            .Handle<Exception>(ex => IsTransient(ex))
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (Exception exception, TimeSpan timespan, int retryCount, Context context) =>
                {
                    _telemetryClient.TrackException(exception,
                       new Dictionary<string, string>
                       {
                            { "Type", "Retry" },
                            { "RetryCount", retryCount.ToString() },
                            { "Policy", "EnqueueDocumentMessage" }
                       });

                    Console.WriteLine($"Retrying due to: {exception.Message}. Waiting {timespan} before next retry.");
                });

        try
        {
            var dependencyName = "AzureBlobStorageUpload";
            var startTime = DateTime.UtcNow;
            var timer = System.Diagnostics.Stopwatch.StartNew();

            // upload document to Azure Blob Storage with retry
            await blobRetryPolicy.ExecuteAsync(async () =>
            {
                var blobContainerClient = _blobServiceClient.GetBlobContainerClient("documents");
                await blobContainerClient.CreateIfNotExistsAsync();
                var blobClient = blobContainerClient.GetBlobClient(document.FileName);
                using var stream = document.OpenReadStream();
                await blobClient.UploadAsync(stream);
            });

            timer.Stop();
            _telemetryClient.TrackDependency(dependencyName, "Upload", document.FileName, startTime, timer.Elapsed, true);

            // Log custom metrics
            _telemetryClient.TrackMetric("DocumentSize", document.Length);

            // place a message in the queue with retry
            await queueRetryPolicy.ExecuteAsync(async () =>
            {
                var message = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(document.FileName));
                await _queueClient.SendMessageAsync(message);
            });

            _telemetryClient.TrackEvent("DocumentUploaded", new Dictionary<string, string> { { "FileName", document.FileName } });

            // return a 202 Accepted status code
            return Accepted($"Document {document.FileName} uploaded successfully.");
        }
        catch (Exception ex)
        {
            _telemetryClient.TrackException(ex, new Dictionary<string, string> { { "FileName", document.FileName } });

            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    bool IsTransient(Exception ex)
    {
        if (ex is HttpRequestException || ex is TimeoutException)
            return true;

        if (ex is RequestFailedException requestFailedException)
            // retry on request timeout, server errors, or network issues
            return requestFailedException.Status == 408 ||
                   (requestFailedException.Status >= 500 && requestFailedException.Status < 600);

        return false;
    }
}
