using FluentValidation;
using HW.Api.DI;
using HW.Api.Middlewares;
using HW.Application.DI;
using HW.Infrastructure.DI;
using static HW.Infrastructure.DI.Options;

var builder = WebApplication.CreateBuilder(args);
var isDev = builder.Environment.IsDevelopment();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddValidatorsFromAssembly(HW.Application.AssemblyReference.Assembly);

builder.Services.AddInterceptorDbContext();
builder.Services.ConfigureMariaDbRetryOptions(builder.Configuration.GetSection(nameof(MariaDbRetryOptions)));
builder.Services.AddMariaDbConfiguration(builder.Configuration, isDev);
builder.Services.AddAuthentication(builder.Configuration);
builder.Services.AddCaching(builder.Configuration);
builder.Services.AddOrderSagaMessaging();
builder.Services.AddMessaging(builder.Configuration);
builder.Services.AddInfrastructureServices();
builder.Services.AddApplicationServices();
builder.Services.AddSwaggerDocumentation();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.UseCors(opt => opt.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.MapControllers();

await app.RunAsync();

public partial class Program { }
