using System.ComponentModel.DataAnnotations.Schema;

namespace SampleApp.Domain;

[Table("Documents")]
public sealed class Document
{
    public required string Id { get; init; }
    public string? FilePath { get; init; }
}

public interface IDocumentRepository
{
    Task<Document?> GetAsync(string id);
}
