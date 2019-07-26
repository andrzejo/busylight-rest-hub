using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;

namespace BusylightRestHost.SimpleHttpServer
{
    public enum HttpMethod
    {
        Get,
        Post
    }

    public delegate Response RequestHandler(HttpListenerRequest request, HttpListenerContext context);

    internal class Route
    {
        public HttpMethod Method { get; }
        public RequestHandler Handler { get; }

        public Route(HttpMethod method, RequestHandler handler)
        {
            Method = method;
            Handler = handler;
        }
    }

    public class Server
    {
        private readonly int _port;
        private readonly List<string> _addresses;
        private readonly HttpListener _listener;
        private readonly Logger _logger;
        private readonly Dictionary<string, Route> _routes;

        public Server(int port, string address = "127.0.0.1")
        {
            _addresses = new List<string>();

            ValidateHttpListenerSupported();
            AppendLocalhostAliases(address);
            _addresses.Add(address);
            _port = port;
            _logger = Logger.GetLogger();
            _listener = new HttpListener();
            _routes = new Dictionary<string, Route>();
        }

        private void AppendLocalhostAliases(string address)
        {
            switch (address)
            {
                case "127.0.0.1":
                    _addresses.Add("localhost");
                    break;
                case "localhost":
                    _addresses.Add("127.0.0.1");
                    break;
            }
        }

        private static void ValidateHttpListenerSupported()
        {
            if (!HttpListener.IsSupported)
            {
                throw new NotSupportedException(
                    "Windows XP SP2 or Server 2003 is required to use the HttpListener class.");
            }
        }

        public void Start()
        {
            _listener.Start();
            ThreadPool.QueueUserWorkItem(o =>
            {
                _logger.Debug("SimpleHttpServer running...");
                try
                {
                    while (_listener.IsListening)
                    {
                        ThreadPool.QueueUserWorkItem(ctx =>
                        {
                            if (!(ctx is HttpListenerContext context))
                            {
                                return;
                            }

                            try
                            {
                                var response = HandleRequest(context);
                                var buffer = Encoding.UTF8.GetBytes(response.Body);
                                context.Response.StatusCode = response.HttpCode;
                                context.Response.ContentLength64 = buffer.Length;
                                foreach (var header in response.Headers)
                                {
                                    context.Response.Headers.Add(header.Key, header.Value);
                                }

                                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                            }
                            catch (Exception e)
                            {
                                _logger.Error(e, "Failed to handle request.");
                                context.Response.StatusCode = 500;
                            }
                            finally
                            {
                                context.Response.OutputStream.Close();
                            }
                        }, _listener.GetContext());
                    }
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Failed to handle request.");
                }
            });
        }

        private Response HandleRequest(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;
            try
            {
                var route = FindRoute(path);
                var method = ParseMethod(context.Request.HttpMethod);
                if (route.Method == method)
                {
                    return route.Handler(context.Request, context);
                }

                _logger.Error($"No route for method '{context.Request.HttpMethod}' and path '{path}'.");
                return Response.Respond404();
            }
            catch (KeyNotFoundException e)
            {
                _logger.Error(e, $"Route {path} not exists.");
                return Response.Respond404();
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to handle request {context.Request.HttpMethod} {path}.");
                return Response.Respond500();
            }
        }

        private Route FindRoute(string path)
        {
            foreach (var route in _routes)
            {
                if (route.Key == path || route.Key == path + "/")
                {
                    return route.Value;
                }
            }

            throw new KeyNotFoundException();
        }

        private static HttpMethod ParseMethod(string httpMethod)
        {
            return (HttpMethod) Enum.Parse(typeof(HttpMethod), httpMethod, true);
        }

        public void RegisterPath(HttpMethod method, string path, RequestHandler handler)
        {
            var validPath = path.EndsWith("/") ? path : path + "/";
            _routes.Add(path, new Route(method, handler));
            AddPrefix(validPath);
        }

        private void AddPrefix(string validPath)
        {
            foreach (var address in _addresses)
            {
                var url = new UriBuilder("http", address, _port, validPath).ToString();
                _listener.Prefixes.Add(url);
            }
        }

        public void Stop()
        {
            _listener.Stop();
            _listener.Close();
        }
    }
}