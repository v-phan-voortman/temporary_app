using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace backend.Models
{
    public class Book
    {
        [BsonId] // Primary key
        [BsonRepresentation(BsonType.ObjectId)] // Allow passing the parameter as a string instead of an ObjectId
        public string? Id { get; set; }

        [BsonElement("Name")] // Map to "Name" field in MongoDB
        [JsonPropertyName("Name")] // Map to "Name" field in JSON
        public string BookName { get; set; } = null!;
        public decimal Price { get; set; }
        public string Category { get; set; } = null!;
        public string Author { get; set; } = null!;

    }

    public class BookPartialUpdateDto
    {
        public string? BookName { get; set; }
        public decimal? Price { get; set; }
        public string? Category { get; set; }
        public string? Author { get; set; }
    }

}
