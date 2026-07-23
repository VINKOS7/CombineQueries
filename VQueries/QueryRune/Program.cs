using System.Reflection;
using System.Text.Json.Serialization;
using CombineQueries.Api.Extensions;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
//SecretKey
builder.Services.Configure<SecretKey>
    (builder.Configuration.GetSection("SecretKey"));

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        builder =>
        {
            builder.SetIsOriginAllowedToAllowWildcardSubdomains();
            builder.AllowAnyHeader();
            builder.AllowAnyMethod();
            builder.WithOrigins(
                "http://localhost:3000",
                "chrome-extension://*");
        });
});

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

app.UseHttpsRedirection();

app.UseAuthorization();

app.UseCors("AllowAll");

app.MapControllers();

app.Run();

public class QueryRune : Controller
{
    [JsonProperty]
}