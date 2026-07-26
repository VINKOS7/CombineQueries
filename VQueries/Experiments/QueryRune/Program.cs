using System.Reflection;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.HttpLogging;

using CombineQueries.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// builder.Services.Configure<JWTOptions>(builder.Configuration.GetSection("JWTOptions"));

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly()));

builder.Services.AddCors();

builder.Services.AddHttpContextAccessor();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin();
        policy.WithOrigins("http://localhost:3000", "https://localhost:3000")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

//builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
//        .AddJwtBearer(options =>
//        {
//            options.RequireHttpsMetadata = false;
//            options.TokenValidationParameters = new TokenValidationParameters
//            {
//                // óêçûâàåò, áóäåò ëè âàëèäèðîâàòüñÿ èçäàòåëü ïðè âàëèäàöèè òîêåíà
//                ValidateIssuer = true,

//                // ñòðîêà, ïðåäñòàâëÿþùàÿ èçäàòåëÿ
//                ValidIssuer = builder.Configuration["JWTOptions:Issuer"],

//                // áóäåò ëè âàëèäèðîâàòüñÿ ïîòðåáèòåëü òîêåíà
//                ValidateAudience = false,

//                // óñòàíîâêà ïîòðåáèòåëÿ òîêåíà
//                //ValidAudience = AuthOptions.AUDIENCE, // ----------------------------------

//                // áóäåò ëè âàëèäèðîâàòüñÿ âðåìÿ ñóùåñòâîâàíèÿ
//                ValidateLifetime = true,

//                // óñòàíîâêà êëþ÷à áåçîïàñíîñòè
//                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["JWTOptions:SecretKey"])),

//                // âàëèäàöèÿ êëþ÷à áåçîïàñíîñòè
//                ValidateIssuerSigningKey = false,
//            };
//        });

builder.Services.AddHttpLogging(logging =>
{
    logging.LoggingFields = HttpLoggingFields.All;
    logging.RequestHeaders.Add("sec-ch-ua");
    logging.ResponseHeaders.Add("MyResponseHeader");
    logging.MediaTypeOptions.AddText("application/javascript");
    logging.RequestBodyLogLimit = 4096;
    logging.ResponseBodyLogLimit = 4096;

});

builder.Services.ConfigureEntityFramework(builder.Configuration);
builder.Services.ConfigureApplicationServices(builder.Configuration);
builder.Services.ConfigureInfrastructureServices();

builder.Services.AddAuthorization();

var app = builder.Build();



app.UseHttpsRedirection();

app.UseCors();

app.UseHttpLogging();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.RunMigrations(builder.Configuration);

app.Run();