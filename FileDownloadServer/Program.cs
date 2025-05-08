using FileDownloadServer.Services;

var builder = WebApplication.CreateBuilder(args);

// Agregar servicios gRPC al contenedor
builder.Services.AddGrpc(options =>
{
    options.MaxReceiveMessageSize = 16 * 1024 * 1024; // 16 MB
    options.MaxSendMessageSize = 16 * 1024 * 1024; // 16 MB
});

// Configurar CORS para permitir llamadas desde el cliente
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader()
               .WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding");
    });
});

var app = builder.Build();

// Configurar el pipeline de solicitudes HTTP
app.UseRouting();
app.UseCors();

// Configurar el directorio de archivos para descarga
var downloadDirectory = Path.Combine(Directory.GetCurrentDirectory(), "DownloadFiles");
if (!Directory.Exists(downloadDirectory))
{
    Directory.CreateDirectory(downloadDirectory);
    // Crear un archivo de ejemplo para pruebas
    var sampleFilePath = Path.Combine(downloadDirectory, "sample.txt");
    File.WriteAllText(sampleFilePath, "Este es un archivo de ejemplo para probar la descarga segmentada.".PadRight(1024 * 1024, 'X'));
}

app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
app.MapGrpcService<FileDownloaderService>().RequireCors("AllowAll");

app.MapGet("/", () => "Servidor de descarga de archivos segmentada mediante gRPC. Utilice un cliente gRPC para conectarse.");

app.Run();