using backend.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace backend.Services
{
    public class BooksService
    {
        private readonly IMongoCollection<Book> _booksCollection;

        public BooksService(
            IOptions<BookStoreDatabaseSettings> bookStoreDatabaseSettings)
        {
            var mongoClient = new MongoClient(
                bookStoreDatabaseSettings.Value.ConnectionString);

            var mongoDatabase = mongoClient.GetDatabase(
                bookStoreDatabaseSettings.Value.DatabaseName);

            _booksCollection = mongoDatabase.GetCollection<Book>(
                bookStoreDatabaseSettings.Value.BooksCollectionName);
        }

        public async Task<List<Book>> GetAsync() =>
            await _booksCollection.Find(_ => true).ToListAsync();

        public async Task<Book?> GetAsync(string id) =>
            await _booksCollection.Find(x => x.Id == id).FirstOrDefaultAsync();

        public async Task CreateAsync(Book newBook) =>
            await _booksCollection.InsertOneAsync(newBook);

        // PUT - updates the entire book document
        public async Task UpdateAsync(string id, Book updatedBook) =>
            await _booksCollection.ReplaceOneAsync(x => x.Id == id, updatedBook);

        // PATCH - updates specific fields of the book document
        public async Task<bool> PatchAsync(string id, BookPartialUpdateDto dto)
        {
            var updates = new List<UpdateDefinition<Book>>();
            var u = Builders<Book>.Update;

            if (dto.BookName is not null) updates.Add(u.Set(x => x.BookName, dto.BookName));
            if (dto.Price.HasValue) updates.Add(u.Set(x => x.Price, dto.Price.Value));
            if (dto.Category is not null) updates.Add(u.Set(x => x.Category, dto.Category));
            if (dto.Author is not null) updates.Add(u.Set(x => x.Author, dto.Author));

            if (updates.Count == 0) return false;

            var combined = u.Combine(updates);

            var result = await _booksCollection.UpdateOneAsync(
                x => x.Id == id,
                combined,
                new UpdateOptions { IsUpsert = false } // Don't create a new document if the ID doesn't exist
            );

            return result.MatchedCount == 1;
        }

        public async Task RemoveAsync(string id) =>
            await _booksCollection.DeleteOneAsync(x => x.Id == id);
    }
}
