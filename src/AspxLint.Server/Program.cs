using AspxLint.Server;

int port = 5173;
string? preferredInterface = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--port" when i + 1 < args.Length:
            if (!int.TryParse(args[++i], out port))
            {
                Console.Error.WriteLine("--port doit etre un entier.");
                return 1;
            }
            break;

        case "--interface" when i + 1 < args.Length:
            preferredInterface = args[++i];
            break;

        case "--help":
        case "-h":
            Console.WriteLine("Usage: AspxLint.Server [--port <p>] [--interface <name-substring>]");
            Console.WriteLine();
            Console.WriteLine("  --port        Port d'ecoute (defaut : 5173)");
            Console.WriteLine("  --interface   Substring pour forcer l'interface reseau (ex. 'wi-fi', 'ethernet')");
            return 0;

        default:
            // Args inconnus ignores : permet a WebApplicationFactory et au host
            // ASP.NET de passer leurs propres flags (--applicationName, --environment, ...)
            // sans que notre CLI les rejette.
            break;
    }
}

var builder = WebApplication.CreateBuilder(args);
var session = ServerHost.Configure(builder, port);
var app = builder.Build();
ServerHost.MapRoutes(app);

var (localUrl, lanUrl) = ServerHost.ResolveUrls(port, session.Token, preferredInterface);
ServerHost.PrintBannerAndQr(session.BuildId, localUrl, lanUrl, session.LogFile);
session.Log("INFO", $"server listening on :{port}, lanUrl={lanUrl}");

await app.RunAsync();
return 0;

// Necessaire pour que WebApplicationFactory<Program> trouve la classe d'entree
// (les top-level statements la generent en `internal`, on la rend `public`).
public partial class Program { }
