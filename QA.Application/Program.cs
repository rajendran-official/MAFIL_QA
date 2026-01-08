using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllersWithViews();

// JWT Authentication - reads jwtToken from HttpOnly cookie and validates it
var jwtSettings = builder.Configuration.GetSection("Jwt");

string keyString = jwtSettings["Key"]
    ?? throw new InvalidOperationException("JWT Key is missing in configuration.");

if (keyString.Length < 32)
    throw new InvalidOperationException("JWT Key must be at least 32 characters long for HS256 security.");

var keyBytes = Encoding.UTF8.GetBytes(keyString);
var signingKey = new SymmetricSecurityKey(keyBytes);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings["Issuer"],
            ValidAudience = jwtSettings["Audience"],
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.Zero
        };

        // Crucial: Read JWT token from the "jwtToken" HttpOnly cookie
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue("jwtToken", out var token))
                {
                    context.Token = token;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Subpath support for /MAFIL_QA (both local and UAT)
var pathBase = builder.Configuration["AppSettings:PathBase"];
if (!string.IsNullOrEmpty(pathBase))
{
    app.UsePathBase(pathBase);
    app.UseRouting(); // Required after UsePathBase
}

// Exception handling
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();  // Must be before UseAuthorization
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Login}/{id?}");

app.MapFallbackToController("Login", "Home"); // Root → Login page

app.Run();