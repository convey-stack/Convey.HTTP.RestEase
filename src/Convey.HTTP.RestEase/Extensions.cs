using System;
using System.Linq;
using System.Net.Http;
using Convey.Discovery.Consul;
using Convey.HTTP.RestEase.Builders;
using Convey.HTTP.RestEase.Serializers;
using Convey.LoadBalancing.Fabio;
using Convey.LoadBalancing.Fabio.MessageHandlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestEase;

namespace Convey.HTTP.RestEase
{
    public static class Extensions
    {
        private const string SectionName = "restEase";
        private const string RegistryName = "http.restEase";

        public static IConveyBuilder AddServiceForwarder<T>(this IConveyBuilder builder, string serviceName,
            string sectionName = SectionName, string consulSectionName = "consul", string fabioSectionName = "fabio")
            where T : class
        {
            var options = builder.GetOptions<RestEaseOptions>(sectionName);
            return builder.AddServiceForwarder<T>(serviceName, options,
                b => b.AddFabio(fabioSectionName, consulSectionName));
        }

        public static IConveyBuilder AddServiceForwarder<T>(this IConveyBuilder builder, string serviceName,
            Func<IRestEaseOptionsBuilder, IRestEaseOptionsBuilder> buildOptions,
            Func<IConsulOptionsBuilder, IConsulOptionsBuilder> buildConsulOptions,
            Func<IFabioOptionsBuilder, IFabioOptionsBuilder> buildFabioOptions)
            where T : class
        {
            var options = buildOptions(new RestEaseOptionsBuilder()).Build();
            return builder.AddServiceForwarder<T>(serviceName, options,
                b => b.AddFabio(buildFabioOptions, buildConsulOptions));
        }

        public static IConveyBuilder AddServiceForwarder<T>(this IConveyBuilder builder, string serviceName,
            RestEaseOptions options, ConsulOptions consulOptions, FabioOptions fabioOptions)
            where T : class
            => builder.AddServiceForwarder<T>(serviceName, options, b => b.AddFabio(fabioOptions, consulOptions));

        private static IConveyBuilder AddServiceForwarder<T>(this IConveyBuilder builder, string serviceName, 
            RestEaseOptions options, Action<IConveyBuilder> registerFabio)
            where T : class
        {
            if (!builder.TryRegister(RegistryName))
            {
                return builder;
            }
            
            var clientName = typeof(T).ToString();
            
            switch (options.LoadBalancer?.ToLowerInvariant())
            {
                case "consul":
                    builder.AddConsulHttpClient(clientName, serviceName);
                    break;
                case "fabio":
                    builder.AddFabioHttpClient(clientName, serviceName);
                    break;
                default:
                    ConfigureDefaultClient(builder.Services, clientName, serviceName, options);
                    break;
            }

            ConfigureForwarder<T>(builder.Services, clientName);

            registerFabio(builder);

            return builder;
        }

        private static void ConfigureDefaultClient(IServiceCollection services, string clientName,
            string serviceName, RestEaseOptions options)
        {
            services.AddHttpClient(clientName, client =>
            {
                var service = options.Services.SingleOrDefault(s => s.Name.Equals(serviceName,
                    StringComparison.InvariantCultureIgnoreCase));
                if (service == null)
                {
                    throw new RestEaseServiceNotFoundException($"RestEase service: '{serviceName}' was not found.",
                        serviceName);
                }

                client.BaseAddress = new UriBuilder
                {
                    Scheme = service.Scheme,
                    Host = service.Host,
                    Port = service.Port
                }.Uri;
            });
        }

        private static void ConfigureForwarder<T>(IServiceCollection services, string clientName) where T : class
        {
            services.AddTransient<T>(c => new RestClient(c.GetService<IHttpClientFactory>().CreateClient(clientName))
            {
                RequestQueryParamSerializer = new QueryParamSerializer()
            }.For<T>());
        }
    }
}