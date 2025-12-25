using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

public class Startup
{
    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public IConfiguration Configuration { get; }

    // This method gets called by the runtime. Use this method to add services to the container.
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddControllers();
        // Swagger/OpenAPI
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        var clientID = Configuration["APS_CLIENT_ID"];
        var clientSecret = Configuration["APS_CLIENT_SECRET"];
        var bucket = Configuration["APS_BUCKET"]; // Optional
        if (string.IsNullOrEmpty(clientID) || string.IsNullOrEmpty(clientSecret))
        {
            throw new ApplicationException("Missing required environment variables APS_CLIENT_ID or APS_CLIENT_SECRET.");
        }
        services.AddSingleton(new APS(clientID, clientSecret, bucket));
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        else
        {
            // Optionally expose Swagger in non-dev environments
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value;
            if (string.IsNullOrEmpty(path) || path == "/")
            {
                context.Response.Redirect("/home.html");
                return;
            }
            await next();
        });
        // Serve home.html as the default landing page
        var defaultFiles = new DefaultFilesOptions();
        defaultFiles.DefaultFileNames.Clear();
        defaultFiles.DefaultFileNames.Add("home.html");
        app.UseDefaultFiles(defaultFiles);
        app.UseStaticFiles();
        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });
    }
}
