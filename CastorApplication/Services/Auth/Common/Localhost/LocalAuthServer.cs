using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CastorApplication.Services.Auth.Abstractions;

namespace CastorApplication.Services.Auth.Common.Localhost
{
    public class LocalAuthServer : ILocalAuthServer
    {
        private readonly HttpListener _listener;

        public LocalAuthServer(HttpListener listener)
        {
            _listener = listener;
            _listener.Prefixes.Add("http://127.0.0.1:45678/");
        }

        public async Task<LocalAuthResult> WaitForCallbackAsync(
            CancellationToken ct = default)
        {
            _listener.Start();

            var context =
                await _listener.GetContextAsync();

            var request = context.Request;

            var code = request.QueryString["code"];
            var state = request.QueryString["state"];

            var response = context.Response;

            var html =
                """
                <html>
                    <body>
                        Authentication completed.
                        You can close this window.
                    </body>
                </html>
                """;

            var buffer = Encoding.UTF8.GetBytes(html);

            response.ContentLength64 = buffer.Length;

            await response.OutputStream.WriteAsync(
                buffer,
                ct);

            response.Close();

            _listener.Stop();

            return new LocalAuthResult
            {
                Code = code!,
                State = state!
            };
        }
    }
}
