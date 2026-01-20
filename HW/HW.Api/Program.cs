using FluentValidation;
using HW.Api.DI;
using HW.Api.Middlewares;
using HW.Application.DI;
using HW.Domain.Entities;
using HW.Infrastructure.DI;
using static HW.Application.Validators.BlogValidator;
using static HW.Infrastructure.DI.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddValidatorsFromAssemblyContaining<CreateBlogRequestDtoValidator>();

builder.Services.AddInterceptorDbContext();
builder.Services.ConfigureMariaDbRetryOptions(builder.Configuration.GetSection(nameof(MariaDbRetryOptions)));
builder.Services.AddMariaDbConfiguration(builder.Configuration);
builder.Services.AddAuthentication(builder.Configuration);
builder.Services.AddInfrastructureServices();
builder.Services.AddApplicationServices();
builder.Services.AddSwaggerDocumentation();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors(opt => opt.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<FluentValidationMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.MapControllers();

try
{
    await app.RunAsync();
}
catch (Exception ex)
{
    await app.StopAsync();
}
finally
{
    await app.DisposeAsync();
}

public partial class Program { }
