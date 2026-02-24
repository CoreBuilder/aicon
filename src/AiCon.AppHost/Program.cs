var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.AiCon_Api>("aicon-api");

builder.Build().Run();
