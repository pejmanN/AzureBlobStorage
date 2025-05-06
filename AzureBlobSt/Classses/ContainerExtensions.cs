using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;

namespace AzureBlobSt.Classses
{
    public static class ContainerExtensions
    {
        public static WebApplicationBuilder AddAzureBlobStorage(this WebApplicationBuilder builder)
        {
            var blobSetting = builder.Configuration.GetSection(nameof(BlobSettings))
                                                        .Get<BlobSettings>();

            if (string.IsNullOrEmpty(blobSetting?.Url)) return builder;

            builder.Services.AddSingleton(serviceProvider =>
            {
                var environment = serviceProvider.GetRequiredService<IHostEnvironment>();
                return environment.IsDevelopment() ?
                    new BlobServiceClient(blobSetting.Url) :
                    new BlobServiceClient(new Uri(blobSetting.Url), new DefaultAzureCredential());
            });
            return builder;
        }

        public static IServiceCollection AddImageUploader(this IServiceCollection services)
        {
            services.AddTransient<IImageUploader, ImageUploader>();
            return services;
        }
    }
}
