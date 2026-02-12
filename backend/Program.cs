
using backend.Models;
using backend.Services;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
namespace backend
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Initialize Firebase Admin SDK 
            var serviceAccountPath = builder.Configuration["Firebase:ServiceAccountKeyPath"];
            var credential = GoogleCredential.FromFile(serviceAccountPath);

            FirebaseApp.Create(new AppOptions()
            {
                Credential = credential,
                ProjectId = builder.Configuration["Firebase:ProjectId"]
            });

            // Add services to the container.
            builder.Services.Configure<BookStoreDatabaseSettings>(
                builder.Configuration.GetSection("BookStoreDatabase"));

            builder.Services.AddSingleton<BooksService>();

            builder.Services.AddControllers()
                .AddJsonOptions(
                    options => options.JsonSerializerOptions.PropertyNamingPolicy = null);
            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
