using PatchHound.Api.Hubs;
using PatchHound.Api.Middleware;
using PatchHound.Api.Startup;

var builder = WebApplication.CreateBuilder(args);
var frontendOrigin = builder.Configuration["Frontend:Origin"];

builder.Services.AddPatchHoundAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddPatchHoundAuthorizationPolicies();
builder.Services.AddPatchHoundApiServices(builder.Configuration);
builder.Services.AddPatchHoundRateLimiting();
builder.Services.AddPatchHoundFrontendCors(frontendOrigin);
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddPatchHoundFeatureManagement(builder.Configuration);

var app = builder.Build();

await app.MigrateDatabaseOnStartupAsync();

Console.WriteLine("[startup] PatchHound.Api application configured, starting web host");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHsts();
}

app.UseMiddleware<ExceptionHandlerMiddleware>();
app.UseHttpsRedirection();
app.UsePatchHoundSecurityHeaders();

if (!string.IsNullOrWhiteSpace(frontendOrigin))
{
    app.UseCors();
}

app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications");
await app.RunAsync();
