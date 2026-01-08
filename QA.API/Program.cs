using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// JWT Configuration
var jwtSettings = builder.Configuration.GetSection("Jwt");

string keyString = jwtSettings["Key"]
    ?? throw new InvalidOperationException("JWT 'Key' is missing in configuration.");

if (keyString.Length < 32)
    throw new InvalidOperationException("JWT Key must be at least 32 characters long for HS256.");

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyString));

// Register signing key as singleton (optional but good practice)
builder.Services.AddSingleton(signingKey);

// JWT Authentication with cookie support for ALL endpoints
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

        // Critical: Read JWT token from "jwtToken" HttpOnly cookie for every request
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue("jwtToken", out var token) && !string.IsNullOrEmpty(token))
                {
                    context.Token = token;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Swagger
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "QA.API",
        Version = "v1",
        Description = "Quality Assurance Department API - Manappuram Finance Limited"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using Bearer scheme. Example: 'Bearer {token}'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// CORS - Allow frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowWebApp", policy =>
    {
        policy.WithOrigins("https://localhost:7087")  // Remove extra < > 
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // Required for cookies
    });
});

var app = builder.Build();

// Subpath support (e.g., /MAFIL_QA)
var pathBase = builder.Configuration["AppSettings:PathBase"];
if (!string.IsNullOrEmpty(pathBase))
{
    app.UsePathBase(pathBase);
    app.UseRouting();
}

// Development middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "QA.API v1"));
}

app.UseHttpsRedirection();

// Middleware order (critical!)
app.UseCors("AllowWebApp");     // Before authentication
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();