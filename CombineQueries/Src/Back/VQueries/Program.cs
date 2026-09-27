using System.Reflection;

using Microsoft.AspNetCore.HttpOverrides;

using CombineQueries.Api.Extensions;
using CombineQueries.Api.Services.Auth;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

builder.Services.AddHttpContextAccessor();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        builder =>
        {
            builder.SetIsOriginAllowedToAllowWildcardSubdomains();
            builder.AllowAnyHeader();
            builder.AllowAnyMethod();
            builder.WithOrigins("http://localhost:3000", "chrome-extension://*");
        });
});
builder.Services.ConfigureEntityFramework(builder.Configuration, builder.Environment.IsDevelopment());

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    // Handlers.Combine и Handlers.CombineNew оба объявляют CombineResponse (и CombineRequest).
    // Дефолтный schemaId - КОРОТКОЕ имя типа, поэтому Swashbuckle видит конфликт и валит генерацию
    // операции ("Failed to generate Operation for action ... CombineNew"). Берём полное имя:
    // типы переименовывать не надо, старый Combine доживает до переезда в Archive.
    options.CustomSchemaIds(type => type.FullName?.Replace('+', '.'));
});

builder.Services.ConfigureApplicationServices(builder.Configuration);

builder.Services.ConfigureInfrastructureServices();

builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly()));

var app = builder.Build();

app.UseForwardedHeaders();

// Перед гейтом: ловит доменные отказы хендлеров и отдаёт их кодом, а не 500.
app.UseMiddleware<Faults>();

app.UseMiddleware<MasterGate>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Alpha"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.UseCors("AllowAll");

app.MapControllers();

app.RunMigrations(builder.Configuration);

app.Run();