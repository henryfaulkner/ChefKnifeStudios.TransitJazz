using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.TransitJazz.Client.Core.Services;

public interface IHttpServiceFactory
{
    IHttpService Create(string name);
}

public class HttpServiceFactory : IHttpServiceFactory
{
    private readonly Func<string, HttpClient> _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;

    public HttpServiceFactory(Func<string, HttpClient> httpClientFactory, ILoggerFactory loggerFactory)
    {
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
    }

    public IHttpService Create(string name)
    {
        var client = _httpClientFactory(name);
        return new HttpService(client, _loggerFactory.CreateLogger<HttpService>());
    }
}
