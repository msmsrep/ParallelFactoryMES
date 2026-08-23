using MesApp.Api;

var app = MesAppHost.Build(args);
await MesAppHost.InitializeAsync(app);
app.Run();

/// <summary>統合テスト（WebApplicationFactory）用</summary>
public partial class Program;
