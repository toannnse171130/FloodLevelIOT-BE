using Core.Interfaces;
using Core.Services;
using Infrastructure.DBContext;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using WebAPI.Errors;
using WebAPI.Models;

namespace WebAPI.Extensions
{
    public static class ApiRequestration
    {
        public static IServiceCollection AddAPIRequestration(this IServiceCollection services)
        {
            //AutoMapper
            services.AddAutoMapper((serviceProvider, cfg) =>
            {
                // Configure AutoMapper to use the service provider
                var configuration = serviceProvider.GetRequiredService<IConfiguration>();
                cfg.ConstructServicesUsing(serviceProvider.GetService);
            },
                typeof(MappingUser).Assembly);


            //FileProvider
            var wwwrootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            if (!Directory.Exists(wwwrootPath))
            {
                Directory.CreateDirectory(wwwrootPath);
            }
            services.AddSingleton<IFileProvider>
                (
                    new PhysicalFileProvider(wwwrootPath)
                );

            services.Configure<ApiBehaviorOptions>(opt =>
            {
                opt.InvalidModelStateResponseFactory = context =>
                {
                    var errorReponse = new ApiValidationErrorResponse
                    {
                        Errors = context.ModelState
                            .Where(x => x.Value.Errors.Count() > 0)
                            .SelectMany(x => x.Value.Errors)
                            .Select(x => x.ErrorMessage).ToArray()
                    };
                    return new BadRequestObjectResult(errorReponse);
                };
            });

            services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>());
            services.AddScoped<IHistoryService, HistoryService>();

            services.AddCors(opt =>
            {
                opt.AddPolicy("CorsPolicy", pol =>
                {
                    pol.AllowAnyHeader()
                       .AllowAnyMethod()
                       .AllowAnyOrigin(); // Allow all origins for Swagger and API access
                });
            });

            return services;
        }
    }
}
