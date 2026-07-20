using DataIngestion.Model.Data;
using DataIngestion.Svc.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddRazorPages();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var dbPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "dataingestionv2.db"));
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddHttpClient<IIngestionService, IngestionService>();
builder.Services.AddScoped<IClientQueryService, ClientQueryService>();
builder.Services.AddSingleton<IIngestionQueue, IngestionChannel>();
builder.Services.AddHostedService<IngestionBackgroundService>();

var app = builder.Build();

// Ensure DB is created on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseStaticFiles();
app.MapControllers();
app.MapRazorPages();

app.Run();
