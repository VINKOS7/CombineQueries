using System.Reflection;

using CombineQueries.Api.Extensions;

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
builder.Services.ConfigureEntityFramework(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen();

builder.Services.ConfigureApplicationServices(builder.Configuration);

builder.Services.ConfigureInfrastructureServices();

builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly()));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.UseCors("AllowAll");

app.MapControllers();

app.RunMigrations(builder.Configuration);

app.Run();