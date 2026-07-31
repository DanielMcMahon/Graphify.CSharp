using SampleApp.Domain;

namespace SampleApp.Services;

public sealed class DocumentRepository : IDocumentRepository
{
    public Task<Document?> GetAsync(string id)
    {
        var sql = "SELECT Id, FilePath FROM Documents WHERE Id = @id";
        return Task.FromResult<Document?>(new Document { Id = id, FilePath = "C:\\files\\doc.pdf" });
    }
}
