using Gms.Api.Hosting;

// Kredensial TIDAK boleh ada di repository: gunakan user-secrets (dev) atau environment variable
// (ConnectionStrings__Default, Seed__AdminPassword) pada lingkungan lain.
var app = ApiHost.Build(args);
await DatabaseInitializer.InitializeAsync(app.Services, app.Configuration);
await app.RunAsync();
