# Pattern: JWT Authentication

## What
`AuthService` generates JWT access tokens + refresh tokens on login. `VerifyTokenAsync()` validates and resolves the user. `[Authorize]` on controllers triggers ASP.NET Core's JWT Bearer middleware.

## Token claims (generated on login)

```csharp
var claims = new List<Claim>
{
    new Claim(JwtRegisteredClaimNames.Sub,  user.UserName),  // subject
    new Claim(JwtRegisteredClaimNames.Jti,  Guid.NewGuid().ToString()),  // token ID
    new Claim(ClaimTypes.NameIdentifier,    user.Id),        // user ID — used by VerifyTokenAsync
    new Claim(ClaimTypes.Name,              user.UserName),
    new Claim(ClaimTypes.Email,             user.Email)      // if present
};
// Roles would be added here:
// var roles = await _userManager.GetRolesAsync(user);
// foreach (var role in roles) claims.Add(new Claim(ClaimTypes.Role, role));
```

## Login flow

```
POST /api/auth/login
  → AuthService.Login()
  → UserManager.FindByNameAsync()     // find user
  → UserManager.CheckPasswordAsync()  // verify password
  → GenerateJwtToken()                // create signed JWT
  → GenerateRefreshToken()            // random base64 bytes
  → return LoginResponseDto(UserName, AccessToken, RefreshToken, Expiration)
```

## Register flow

```
POST /api/auth/register
  → AuthService.Register()
  → Validate required fields
  → UserManager.FindByNameAsync()     // check username uniqueness
  → UserManager.FindByEmailAsync()    // check email uniqueness
  → UserManager.CreateAsync(user, password)
  → throw InvalidOperationException on failure
```

## Token verification

```csharp
public async Task<AppUser?> VerifyTokenAsync(string token)
{
    // Validates: signature, issuer, audience, expiry
    var principal = tokenHandler.ValidateToken(token, validationParameters, out _);

    // Extract user ID from NameIdentifier claim
    var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    // Resolve live user object
    return await _userManager.FindByIdAsync(userId);
}
```

## appsettings.json configuration

```json
"Jwt": {
  "Key": "THIS_IS_A_SUPER_SECRET_KEY_CHANGE_ME_1234567890",
  "Issuer": "MyCompany.AuthServer",
  "Audience": "MyCompany.ApiClients",
  "AccessTokenExpirationMinutes": 60
}
```

> **Security:** The key must be ≥ 32 characters for HMAC-SHA256. Never commit real secrets — use user secrets or environment variables in production.

## DI registration (`AddAuthentication` in ServiceCollectionExtensions)

```csharp
public static void AddAuthentication(this IServiceCollection services, IConfiguration configuration)
{
    services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        var key = configuration["Jwt:Key"]!;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ValidateIssuer           = true,
            ValidIssuer              = configuration["Jwt:Issuer"],
            ValidateAudience         = true,
            ValidAudience            = configuration["Jwt:Audience"],
            ValidateLifetime         = true,
            ClockSkew                = TimeSpan.FromSeconds(30)
        };
    });
}
```

## Controller authorization

```csharp
// Whole controller requires auth
[Authorize]
[ApiController]
[Route("api/product")]
public class ProductController : ControllerBase { ... }

// Mixed: public reads, protected writes
[ApiController]
[Route("api/blog")]
public class BlogController : ControllerBase
{
    [HttpGet]               // public
    public async Task<IActionResult> GetAll(...) { ... }

    [HttpPost]
    [Authorize]             // requires valid JWT
    public async Task<IActionResult> Create(...) { ... }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]   // requires Admin role claim
    public async Task<IActionResult> Delete(string id) { ... }
}
```

## Reading current user in a service

```csharp
// Inject IHttpContextAccessor, then:
var userId = _httpContextAccessor.HttpContext?.User
    .FindFirst(ClaimTypes.NameIdentifier)?.Value;

// Or use the existing UserInfo singleton (tenant context placeholder):
// HW.Infrastructure/MultiTenant/UserInfo.cs
public class UserInfo { public string Id { get; set; } public string UserName { get; set; } }
```

## Identity password policy (current config)

```
RequireDigit: false
RequireLowercase: false
RequireUppercase: false
RequireNonAlphanumeric: false
RequiredLength: 3
RequiredUniqueChars: 1
RequireConfirmedEmail: true
RequireUniqueEmail: true
Lockout: 5 attempts → 5 min lockout
```
