using DataIngestion.Model.DTOs;

namespace DataIngestion.Svc.Services;

public interface IIngestionService
{
    Task<IngestionResult> IngestAsync(string zipUrl);
    Task<IngestionResult> IngestAsync(byte[] zipBytes, string sourceLabel);
}
