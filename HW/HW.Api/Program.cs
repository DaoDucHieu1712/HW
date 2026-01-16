using FluentValidation;
using HW.Api.Middlewares;
using HW.Application.DI;
using HW.Infrastructure.DI;
using static HW.Application.Validators.BlogValidator;
using static HW.Infrastructure.DI.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddValidatorsFromAssemblyContaining<CreateBlogDtoRequestValidator>();

builder.Services.AddInterceptorDbContext();
builder.Services.ConfigureMariaDbRetryOptions(builder.Configuration.GetSection(nameof(MariaDbRetryOptions)));
builder.Services.AddMariaDbConfiguration(builder.Configuration);
builder.Services.AddInfrastructureServices();

builder.Services.AddApplicationServices();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();
app.UseMiddleware<FluentValidationMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.MapControllers();

app.Run();
