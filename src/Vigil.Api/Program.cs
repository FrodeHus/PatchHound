using Microsoft.Identity.Web;
using Vigil.Core.Interfaces;
using Vigil.Api.Auth;
using Vigil.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Authentication - Entra ID multi-tenant
builder.Services.AddAuthentication()
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorization();

// Database
builder.Services.AddDbContext<VigilDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Vigil")));

// Tenant context (scoped - one per request)
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddHttpContextAccessor();

// OpenAPI
builder.Services.AddOpenApi();
builder.Services.AddControllers();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
