using TaskManager.Application.Interfaces;
using TaskManager.Application.Services;
using TaskManager.Infrastructure;
using TaskManager.WebUI.Services;
using TaskManager.WebUI.BackgroundServices;
using TaskManager.WebUI.Options;
using TaskManager.WebUI.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddControllersWithViews()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<GuestCookieService>();
builder.Services.AddScoped<BoardOwnerCookieService>();
builder.Services.AddScoped<IGuestService, GuestService>();
builder.Services.AddScoped<IBoardService, BoardService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<ExpiredDataCleanupOptions>()
    .Bind(builder.Configuration.GetSection(ExpiredDataCleanupOptions.SectionName))
    .Validate(settings => settings.IntervalMinutes is >= 1 and <= 1440,
        "Cleanup IntervalMinutes must be between 1 and 1440.")
    .Validate(settings => settings.BatchSize is >= 1 and <= 1000,
        "Cleanup BatchSize must be between 1 and 1000.")
    .ValidateOnStart();
builder.Services.AddHostedService<ExpiredDataCleanupBackgroundService>();
builder.Services.AddSignalR();
builder.Services.AddSingleton<BoardPresenceService>();
builder.Services.AddHostedService<BoardPresenceBackgroundService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRequestLocalization("th-TH");
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapHub<BoardHub>("/hubs/board");

app.Run();
